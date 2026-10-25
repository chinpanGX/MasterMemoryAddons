using System;
using MasterMemory.Addons.Tools.Common;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests.Common;

public class CellConverterTests
{
    public enum Category
    {
        Weapon = 1,
        Armor = 2,
    }

    public enum Kind
    {
        None = 0,
        Normal = 1,
    }

    private static readonly EnumSchema CategorySchema = new(
        "",
        Array.Empty<string>(),
        "Category",
        new[] { new EnumMemberSchema("Weapon", 1, "武器"), new EnumMemberSchema("Armor", 2, "防具") },
        "Category.cs");

    private static readonly EnumSchema KindSchema = new(
        "",
        Array.Empty<string>(),
        "Kind",
        new[] { new EnumMemberSchema("None", 0, "なし"), new EnumMemberSchema("Normal", 1, "通常") },
        "Kind.cs");

    private static object? Convert(string raw, Type type, bool isPrimaryKey = false, EnumSchema? enumSchema = null)
    {
        Assert.True(CellConverter.TryConvert(raw, type, isPrimaryKey, enumSchema, out var value, out var error), error);
        return value;
    }

    private static string Fail(string raw, Type type, bool isPrimaryKey = false, EnumSchema? enumSchema = null)
    {
        Assert.False(CellConverter.TryConvert(raw, type, isPrimaryKey, enumSchema, out _, out var error));
        return error;
    }

    [Fact]
    public void TryConvert_ConvertsPrimitiveTypes()
    {
        // 検証: int / long / string の値を変換する
        // 期待: それぞれの型の値になる
        Assert.Equal(-12, Convert("-12", typeof(int)));
        Assert.Equal(9_000_000_000L, Convert("9000000000", typeof(long)));
        Assert.Equal("布の服, 軽い", Convert("布の服, 軽い", typeof(string)));
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("1", true)]
    [InlineData("FALSE", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    public void TryConvert_AcceptsBoolNotations(string raw, bool expected)
    {
        // 検証: bool の各表記を変換する
        // 期待: TRUE / True / 1 は true、FALSE / False / 0 は false
        Assert.Equal(expected, Convert(raw, typeof(bool)));
    }

    [Theory]
    [InlineData("abc", typeof(int))]
    [InlineData(" 1", typeof(int))]
    [InlineData("1.5", typeof(long))]
    [InlineData("yes", typeof(bool))]
    [InlineData("true", typeof(bool))]
    public void TryConvert_Fails_OnInvalidValue(string raw, Type type)
    {
        // 検証: 型に合わない値(前後の空白、小文字の true を含む)を変換する
        // 期待: 変換できない
        Assert.NotEmpty(Fail(raw, type));
    }

    [Fact]
    public void TryConvert_UsesDefault_ForEmptyCell()
    {
        // 検証: 空欄を変換する
        // 期待: 型の既定値(0 / false / "")になる。値 0 のメンバーがある enum は、そのメンバーになる
        Assert.Equal(0, Convert("", typeof(int)));
        Assert.Equal(0L, Convert("", typeof(long)));
        Assert.Equal(false, Convert("", typeof(bool)));
        Assert.Equal("", Convert("", typeof(string)));
        Assert.Equal(Kind.None, Convert("", typeof(Kind), enumSchema: KindSchema));
    }

    [Fact]
    public void TryConvert_Fails_ForEmptyPrimaryKey()
    {
        // 検証: 主キーの列の空欄を変換する
        // 期待: 主キーは空欄にできないエラー
        Assert.Contains("主キー", Fail("", typeof(int), isPrimaryKey: true));
    }

    [Fact]
    public void TryConvert_Fails_ForEmptyEnumWithoutZero()
    {
        // 検証: 値 0 のメンバーがない enum の列の空欄を変換する
        // 期待: 空欄にできないエラー
        Assert.Contains("値 0", Fail("", typeof(Category), enumSchema: CategorySchema));
    }

    [Fact]
    public void TryConvert_ConvertsEnumByInputName()
    {
        // 検証: enum の列に、入力名を書く
        // 期待: 入力名のメンバーになる
        Assert.Equal(Category.Armor, Convert("防具", typeof(Category), enumSchema: CategorySchema));
    }

    [Theory]
    [InlineData("Armor")]
    [InlineData("2")]
    [InlineData("盾")]
    public void TryConvert_Fails_ForEnumMemberNameOrNumber(string raw)
    {
        // 検証: enum の列に、C# のメンバー名・数値・定義にない名前を書く
        // 期待: 入力名にないエラー(入力名だけを受け付ける)。エラーには書ける入力名が入る
        var error = Fail(raw, typeof(Category), enumSchema: CategorySchema);

        Assert.Contains("入力名にありません", error);
        Assert.Contains("武器, 防具", error);
    }

    [Fact]
    public void TryConvert_Fails_ForUnsupportedType()
    {
        // 検証: 未対応の型(double)の列を変換する
        // 期待: 未対応の型のエラー
        Assert.Contains("未対応の型", Fail("1.5", typeof(double)));
    }
}
