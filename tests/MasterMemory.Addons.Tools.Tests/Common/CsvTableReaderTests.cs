using System.Collections.Generic;
using System.Linq;
using MasterMemory.Addons.Tools.Common;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests.Common;

public class CsvTableReaderTests
{
    private const string Prefix = "comment\ncomment\nint,string\n";

    // マスタ生成と同じ経路(ファイルの列挙 → テーブルごとにまとめる → 読む)で、テーブル item の CSV を読む
    private static CsvTable? ReadItem(TempDirectory temp, List<CsvError> errors)
    {
        var csvDirectory = temp.Combine("csv");
        var filesByTable = CsvTableReader.GroupFilesByTable(CsvTableReader.EnumerateFiles(csvDirectory));
        var files = filesByTable.TryGetValue("item", out var found) ? found : new List<string>();
        return CsvTableReader.Read(csvDirectory, "item", files, errors);
    }

    [Fact]
    public void Read_SkipsCommentAndTypeRows_AndReadsHeaderAndData()
    {
        // 検証: 1〜2行目がコメント、3行目が型、4行目がヘッダの CSV を読む
        // 期待: 4行目がヘッダになり、5行目以降がデータになる。行の番号はファイルの行の番号
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", Prefix + "ItemId,Name\n1,剣\n2,盾\n");
        var errors = new List<CsvError>();

        var table = ReadItem(temp, errors);

        Assert.Empty(errors);
        Assert.NotNull(table);
        Assert.Equal(new[] { "ItemId", "Name" }, table!.Header);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(new[] { "1", "剣" }, table.Rows[0].Values);
        Assert.Equal(5, table.Rows[0].Line);
        Assert.Equal(4, table.HeaderLine);
    }

    [Fact]
    public void Read_ReportsActualHeaderLine_WhenCommentHasQuotedNewline()
    {
        // 検証: 1行目のコメントに、引用符で囲んだ改行がある CSV を読む(review の指摘 6 の再現)
        // 期待: ヘッダの行の番号は、ファイルの実際の行(5行目)になる
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", "\"comment\nnext\"\ncomment\nint,string\nItemId,Name\n1,剣\n");
        var errors = new List<CsvError>();

        var table = ReadItem(temp, errors);

        Assert.Empty(errors);
        Assert.Equal(5, table!.HeaderLine);
        Assert.Equal(6, table.Rows[0].Line);
    }

    [Fact]
    public void Read_ReportsActualHeaderLine_WhenSplitFileHeaderDiffers()
    {
        // 検証: 水平分割したファイルのヘッダが違い、そのファイルのコメントに引用符で囲んだ改行がある
        // 期待: エラーの行の番号は、そのファイルの実際のヘッダの行(5行目)になる
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", Prefix + "ItemId,Name\n1,a\n");
        temp.Write("csv/item_a.csv", "\"comment\nnext\"\ncomment\nint,string\nItemId,Title\n2,b\n");
        var errors = new List<CsvError>();

        ReadItem(temp, errors);

        var error = Assert.Single(errors);
        Assert.Equal("item_a.csv", error.File);
        Assert.Equal(5, error.Line);
    }

    [Fact]
    public void Read_CombinesHorizontallySplitFiles_InFileNameOrder()
    {
        // 検証: item.csv と、水平分割した item_b.csv・item_a.csv を置く
        // 期待: ファイル名の昇順(item.csv、item_a.csv、item_b.csv)に結合される
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", Prefix + "ItemId,Name\n1,a\n");
        temp.Write("csv/item_b.csv", Prefix + "ItemId,Name\n3,c\n");
        temp.Write("csv/item_a.csv", Prefix + "ItemId,Name\n2,b\n");
        var errors = new List<CsvError>();

        var table = ReadItem(temp, errors);

        Assert.Empty(errors);
        Assert.Equal(new[] { "item.csv", "item_a.csv", "item_b.csv" }, table!.Files);
        Assert.Equal(new[] { "1", "2", "3" }, table.Rows.Select(r => r.Values[0]));
        Assert.Equal("item_a.csv", table.Rows[1].File);
    }

    [Fact]
    public void GroupFilesByTable_MatchesOnlyTextBeforeFirstSeparator()
    {
        // 検証: item のほかに、itemEffect.csv、item.weapon.csv、item_.csv、_item.csv を置く
        // 期待: item に一致するのは item.csv と item_weapon_01.csv だけ(最初の _ より前がテーブル名)
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", Prefix + "ItemId,Name\n1,a\n");
        temp.Write("csv/item_weapon_01.csv", Prefix + "ItemId,Name\n2,b\n");
        temp.Write("csv/itemEffect.csv", Prefix + "ItemId,Name\n9,x\n");
        temp.Write("csv/item.weapon.csv", Prefix + "ItemId,Name\n9,x\n");
        temp.Write("csv/item_.csv", Prefix + "ItemId,Name\n9,x\n");
        temp.Write("csv/_item.csv", Prefix + "ItemId,Name\n9,x\n");

        var filesByTable = CsvTableReader.GroupFilesByTable(CsvTableReader.EnumerateFiles(temp.Combine("csv")));

        Assert.Equal(new[] { "item.csv", "item_weapon_01.csv" }, filesByTable["item"]);
        Assert.Equal(new[] { "itemEffect.csv" }, filesByTable["itemEffect"]);
        Assert.Equal(new[] { "item.weapon" }, filesByTable.Keys.Where(k => k.Contains('.')));
        Assert.Equal("itemEffect", CsvTableReader.GetTableName("itemEffect.csv"));
        Assert.Null(CsvTableReader.GetTableName("item_.csv"));
        Assert.Null(CsvTableReader.GetTableName("_item.csv"));
    }

    [Fact]
    public void Read_FindsFilesInSubfolder()
    {
        // 検証: スプレッドシートごとのフォルダ(master/)の中に、item.csv と item_weapon.csv を置く
        // 期待: フォルダの中のファイルを結合し、ファイルは csv からの相対パス(区切りは /)で表す
        using var temp = new TempDirectory();
        temp.Write("csv/master/item.csv", Prefix + "ItemId,Name\n1,a\n");
        temp.Write("csv/master/item_weapon.csv", Prefix + "ItemId,Name\n2,b\n");
        var errors = new List<CsvError>();

        var table = ReadItem(temp, errors);

        Assert.Empty(errors);
        Assert.Equal(new[] { "master/item.csv", "master/item_weapon.csv" }, table!.Files);
        Assert.Equal("master/item_weapon.csv", table.Rows[1].File);
    }

    [Fact]
    public void Read_ReportsError_WhenFilesAreInMultipleFolders()
    {
        // 検証: 同じテーブルの CSV を、2つのフォルダ(a/、b/)に置く
        // 期待: 複数のフォルダにあるエラーになり、テーブルは null
        using var temp = new TempDirectory();
        temp.Write("csv/a/item.csv", Prefix + "ItemId,Name\n1,a\n");
        temp.Write("csv/b/item_weapon.csv", Prefix + "ItemId,Name\n2,b\n");
        var errors = new List<CsvError>();

        var table = ReadItem(temp, errors);

        Assert.Null(table);
        Assert.Contains("複数のフォルダ", Assert.Single(errors).Message);
    }

    [Fact]
    public void Read_ReportsError_WhenSplitFileHeaderDiffers()
    {
        // 検証: 水平分割したファイルのヘッダの列の順番を変える
        // 期待: ヘッダが一致しないエラーになり、テーブルは null
        using var temp = new TempDirectory();
        temp.Write("csv/item_a.csv", Prefix + "ItemId,Name\n1,a\n");
        temp.Write("csv/item_b.csv", Prefix + "Name,ItemId\nb,2\n");
        var errors = new List<CsvError>();

        var table = ReadItem(temp, errors);

        Assert.Null(table);
        var error = Assert.Single(errors);
        Assert.Equal("item_b.csv", error.File);
        Assert.Contains("ヘッダが item_a.csv と一致しません", error.Message);
    }

    [Fact]
    public void Read_IgnoresTrailingEmptyHeaderColumns()
    {
        // 検証: コメントがヘッダより右の列まであり、全行が空のセルで埋められた CSV を読む。分割したファイルは埋められた幅が違う
        // 期待: ヘッダの末尾の空の列は無視され、ヘッダは ItemId,Name。データも2列になる
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", "comment,,,\ncomment,,,\nint,string,,\nItemId,Name,,\n1,a,,\n");
        temp.Write("csv/item_a.csv", "comment,,\ncomment,,\nint,string,\nItemId,Name,\n2,b,\n");
        var errors = new List<CsvError>();

        var table = ReadItem(temp, errors);

        Assert.Empty(errors);
        Assert.Equal(new[] { "ItemId", "Name" }, table!.Header);
        Assert.Equal(new[] { "1", "a" }, table.Rows[0].Values);
        Assert.Equal(new[] { "2", "b" }, table.Rows[1].Values);
    }

    [Fact]
    public void Read_ReportsError_WhenValueIsInColumnWithoutHeader()
    {
        // 検証: ヘッダの末尾の空の列に、値を書いたデータ行を含む CSV を読む
        // 期待: 行の番号付きのエラーになり、テーブルは null
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", Prefix + "ItemId,Name,\n1,a,\n2,b,memo\n");
        var errors = new List<CsvError>();

        Assert.Null(ReadItem(temp, errors));
        var error = Assert.Single(errors);
        Assert.Equal(6, error.Line);
        Assert.Contains("ヘッダのない列(3列目)に値があります", error.Message);
    }

    [Fact]
    public void Read_AcceptsRowsWithoutTrailingEmptyColumns()
    {
        // 検証: ヘッダが ItemId,Name,, のファイルに、空の列を付けずに足した行(9,x)と、一部だけ付けた行(8,y,)を含めて読む(review の R4 の再現)
        // 期待: エラーにならず、どの行も名前のある2列の値になる
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", "comment,,,\ncomment,,,\nint,string,,\nItemId,Name,,\n1,a,,\n9,x\n8,y,\n");
        var errors = new List<CsvError>();

        var table = ReadItem(temp, errors);

        Assert.Empty(errors);
        Assert.Equal(new[] { "9", "x" }, table!.Rows[1].Values);
        Assert.Equal(new[] { "8", "y" }, table.Rows[2].Values);
    }

    [Theory]
    [InlineData("1\n", "ヘッダ 2〜4 列、この行 1 列")]
    [InlineData("1,a,,,\n", "ヘッダ 2〜4 列、この行 5 列")]
    public void Read_ReportsColumnCountMismatch_WhenTrailingEmptyColumnsExist(string row, string message)
    {
        // 検証: ヘッダが ItemId,Name,, のファイルに、名前のある列より少ない行・ヘッダの列より多い行を含めて読む
        // 期待: 列の数のエラーになり、受け付ける列の数の範囲を表示する
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", "comment,,,\ncomment,,,\nint,string,,\nItemId,Name,,\n" + row);
        var errors = new List<CsvError>();

        Assert.Null(ReadItem(temp, errors));
        Assert.Contains(message, Assert.Single(errors).Message);
    }

    [Fact]
    public void Read_ReportsError_WhenNoFileMatches()
    {
        // 検証: テーブルの CSV を置かない
        // 期待: 見つからないエラーになる
        using var temp = new TempDirectory();
        temp.Write("csv/other.csv", Prefix + "ItemId,Name\n1,a\n");
        var errors = new List<CsvError>();

        Assert.Null(ReadItem(temp, errors));
        Assert.Contains("見つかりません", Assert.Single(errors).Message);
    }

    [Fact]
    public void Read_SkipsEmptyRows_AndReportsColumnCountMismatch()
    {
        // 検証: すべてのセルが空の行と、列の数が足りない行を含む CSV を読む
        // 期待: 空の行は読み飛ばし、列の数が違う行は行の番号付きのエラーになる
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", Prefix + "ItemId,Name\n1,a\n,\n2\n");
        var errors = new List<CsvError>();

        Assert.Null(ReadItem(temp, errors));
        var error = Assert.Single(errors);
        Assert.Equal(7, error.Line);
        Assert.Contains("列の数", error.Message);
    }

    [Fact]
    public void Read_AcceptsUtf8Bom()
    {
        // 検証: BOM 付きの UTF-8 で書いた CSV を読む
        // 期待: BOM は値に入らない
        using var temp = new TempDirectory();
        temp.Write("csv/item.csv", "﻿" + Prefix + "ItemId,Name\n1,a\n");
        var errors = new List<CsvError>();

        var table = ReadItem(temp, errors);

        Assert.Equal("ItemId", table!.Header[0]);
    }
}
