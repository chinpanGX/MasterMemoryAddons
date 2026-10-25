using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// テーブルの CSV を読む。形式は docs/design/master-gen.md の「CSV とテーブルの対応」「CSV の読み込み」。
/// 1〜2行目はコメント、3行目は型(参考)、4行目がヘッダ、5行目以降がデータ。
/// </summary>
public static class CsvTableReader
{
    /// <summary>水平分割の区切り。`item_weapon.csv` はテーブル item の CSV。テーブル名には使えない。</summary>
    public const char SplitSeparator = '_';

    private const int HeaderLine = 4;

    /// <summary>
    /// <paramref name="tableName"/> の CSV(<see cref="GroupFilesByTable"/> が返す、`name.csv` と水平分割した `name_*.csv` の
    /// CSV のディレクトリからの相対パス)をファイル名の昇順に読んで結合する。ファイルは1つのフォルダ(スプレッドシート)の中にあること。
    /// 読めない場合は null を返し、理由を <paramref name="errors"/> に足す。
    /// </summary>
    public static CsvTable? Read(string csvDirectory, string tableName, IReadOnlyList<string> files, List<CsvError> errors)
    {
        if (files.Count == 0)
        {
            errors.Add(new CsvError($"{tableName}.csv", null, null,
                $"テーブル {tableName} の CSV が見つかりません({tableName}.csv か {tableName}{SplitSeparator}<任意>.csv を置いてください)。"));
            return null;
        }

        var directories = files.Select(DirectoryOf).Distinct(StringComparer.Ordinal).ToList();
        if (directories.Count > 1)
        {
            errors.Add(new CsvError(files[0], null, null,
                $"テーブル {tableName} の CSV が、複数のフォルダにあります: {string.Join(", ", files)}(1つのフォルダ(スプレッドシート)にまとめてください)。"));
            return null;
        }

        IReadOnlyList<string>? header = null;
        var headerFile = "";
        var headerLine = 0;
        var rows = new List<CsvRow>();
        var ok = true;

        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            List<CsvRecord> records;
            try
            {
                // File.ReadAllText は BOM を見て UTF-8 として読む(BOM がなければ UTF-8)
                records = CsvParser.Parse(File.ReadAllText(Path.Combine(csvDirectory, file)));
            }
            catch (CsvFormatException ex)
            {
                errors.Add(new CsvError(file, ex.Line, null, ex.Message));
                ok = false;
                continue;
            }

            if (records.Count < HeaderLine)
            {
                errors.Add(new CsvError(file, null, null, $"ヘッダ({HeaderLine}行目)がありません。"));
                ok = false;
                continue;
            }

            // スプレッドシートの書き出しでは、全行がいちばん長い行(コメントなど)の列数まで空のセルで埋められる。
            // ヘッダの末尾の空の列は列として扱わない
            var headerRecord = records[HeaderLine - 1];
            var rawHeader = headerRecord.Fields;
            var width = rawHeader.Count;
            while (width > 0 && rawHeader[width - 1].Length == 0)
            {
                width--;
            }

            var fileHeader = rawHeader.Take(width).ToList();
            if (header is null)
            {
                header = fileHeader;
                headerFile = file;
                headerLine = headerRecord.Line;
            }
            else if (!header.SequenceEqual(fileHeader, StringComparer.Ordinal))
            {
                errors.Add(new CsvError(file, headerRecord.Line, null,
                    $"ヘッダが {headerFile} と一致しません(水平分割したファイルは、ヘッダを列の順番まで同じにしてください)。"));
                ok = false;
                continue;
            }

            foreach (var record in records.Skip(HeaderLine))
            {
                // すべてのセルが空の行(シートの末尾の空行など)は読み飛ばす
                if (record.Fields.All(string.IsNullOrEmpty))
                {
                    continue;
                }

                // 名前のある列がすべてあれば、ヘッダの末尾の空の列の分は、あってもなくてもよい
                // (テキストエディタで、空の列を付けずに行を足した場合)
                if (record.Fields.Count < width || record.Fields.Count > rawHeader.Count)
                {
                    var expected = width == rawHeader.Count ? $"{width}" : $"{width}〜{rawHeader.Count}";
                    errors.Add(new CsvError(file, record.Line, null,
                        $"列の数がヘッダと違います(ヘッダ {expected} 列、この行 {record.Fields.Count} 列)。"));
                    ok = false;
                    continue;
                }

                var outside = Enumerable.Range(width, record.Fields.Count - width).FirstOrDefault(i => record.Fields[i].Length > 0, -1);
                if (outside >= 0)
                {
                    errors.Add(new CsvError(file, record.Line, null,
                        $"ヘッダのない列({outside + 1}列目)に値があります: '{record.Fields[outside]}'"));
                    ok = false;
                    continue;
                }

                rows.Add(new CsvRow(file, record.Line, record.Fields.Take(width).ToList()));
            }
        }

        if (!ok || header is null)
        {
            return null;
        }

        return new CsvTable(tableName, files.OrderBy(f => f, StringComparer.Ordinal).ToList(), header, headerLine, rows);
    }

    /// <summary>
    /// <see cref="EnumerateFiles"/> が返したファイルを、テーブル名(<see cref="GetTableName"/>)ごとにまとめる。順番は保つ。
    /// どのテーブル名にもならないファイルは含めない。
    /// </summary>
    public static Dictionary<string, List<string>> GroupFilesByTable(IEnumerable<string> files)
    {
        // ファイル名の最初の `_` より前がテーブル名なので、1つのファイルが一致するテーブルは1つだけ
        return files
            .Where(f => GetTableName(f) is not null)
            .GroupBy(f => GetTableName(f)!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    /// <summary>
    /// CSV のディレクトリの下(フォルダの中も含む)にある `*.csv` を、相対パスの昇順で返す。
    /// 区切りは `/`(エラーの表示を OS によらず同じにするため)。
    /// </summary>
    public static List<string> EnumerateFiles(string csvDirectory)
    {
        if (!Directory.Exists(csvDirectory))
        {
            return new List<string>();
        }

        return Directory.EnumerateFiles(csvDirectory, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(csvDirectory, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// CSV のファイル名から、テーブル名を返す。`item.csv` と `item_weapon.csv` は item。
    /// `.csv` でない、または `_` で始まる・終わる名前は null(どのテーブルにも一致しない)。
    /// </summary>
    public static string? GetTableName(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var stem = fileName[..^".csv".Length];
        var separator = stem.IndexOf(SplitSeparator);
        if (separator < 0)
        {
            return stem.Length > 0 ? stem : null;
        }

        return separator > 0 && separator < stem.Length - 1 ? stem[..separator] : null;
    }

    private static string DirectoryOf(string relativePath)
    {
        var index = relativePath.LastIndexOf('/');
        return index < 0 ? "" : relativePath[..index];
    }
}
