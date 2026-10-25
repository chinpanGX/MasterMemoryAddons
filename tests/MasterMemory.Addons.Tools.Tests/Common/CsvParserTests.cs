using MasterMemory.Addons.Tools.Common;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests.Common;

public class CsvParserTests
{
    [Fact]
    public void Parse_SplitsFieldsAndRecords()
    {
        // 検証: 引用符のない、LF 区切りの CSV を読む
        // 期待: レコードと値に分かれ、各レコードの行の番号が付く
        var records = CsvParser.Parse("a,b,c\n1,2,3\n");

        Assert.Equal(2, records.Count);
        Assert.Equal(new[] { "a", "b", "c" }, records[0].Fields);
        Assert.Equal(new[] { "1", "2", "3" }, records[1].Fields);
        Assert.Equal(2, records[1].Line);
    }

    [Fact]
    public void Parse_HandlesQuotedCommaNewlineAndEscapedQuote()
    {
        // 検証: 引用符で囲んだ値の中に、`,`・改行・`""` を書く
        // 期待: 値の一部として読まれる。改行を含むレコードの次のレコードの行の番号は、改行の分だけ進む
        var records = CsvParser.Parse("\"a,b\",\"x\ny\",\"say \"\"hi\"\"\"\nnext\n");

        Assert.Equal(new[] { "a,b", "x\ny", "say \"hi\"" }, records[0].Fields);
        Assert.Equal(3, records[1].Line);
    }

    [Fact]
    public void Parse_HandlesCrlfAndMissingTrailingNewline()
    {
        // 検証: CRLF 区切りで、最後の行に改行がない CSV を読む
        // 期待: CR は値に入らず、最後の行も読まれる
        var records = CsvParser.Parse("a,b\r\n1,2");

        Assert.Equal(new[] { "a", "b" }, records[0].Fields);
        Assert.Equal(new[] { "1", "2" }, records[1].Fields);
    }

    [Fact]
    public void Parse_KeepsEmptyFields()
    {
        // 検証: 空の値を含む行を読む
        // 期待: 空の値も列として数えられる
        var records = CsvParser.Parse("1,,3,\n");

        Assert.Equal(new[] { "1", "", "3", "" }, records[0].Fields);
    }

    [Fact]
    public void Parse_Throws_WhenQuoteIsNotClosed()
    {
        // 検証: 引用符が閉じていない CSV を読む
        // 期待: 引用符が始まった行の番号付きで CsvFormatException になる
        var ex = Assert.Throws<CsvFormatException>(() => CsvParser.Parse("a,b\n1,\"open\n2,3\n"));

        Assert.Equal(2, ex.Line);
    }
}
