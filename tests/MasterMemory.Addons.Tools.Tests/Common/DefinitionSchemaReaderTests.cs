using System.Linq;
using MasterMemory.Addons.Tools.Common;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests.Common;

public class DefinitionSchemaReaderTests
{
    private const string Source = """
        using MasterMemory;
        using MasterMemory.Addons;
        using MessagePack;

        namespace Sample.Definitions
        {
            public enum ItemCategory
            {
                [InputName("なし")] None,
                [InputName("武器")] Weapon = 10,
                Armor,
                [InputName("負")] Negative = -1,
            }

            [MemoryTable("item"), MessagePackObject(true)]
            public class Item
            {
                [PrimaryKey]
                public int ItemId { get; init; }

                public ItemCategory Category { get; init; }

                [ServerOnly]
                public int SellPrice { get; set; }

                public string Computed => "";

                public static int Shared { get; set; }
            }
        }
        """;

    // マスタ生成と同じく、DefinitionSource で1回だけパースした定義を読む
    private static DefinitionSchema Read(TempDirectory temp)
    {
        return DefinitionSchemaReader.Read(DefinitionSource.Load(temp.Combine("Definitions"), System.Array.Empty<string>()));
    }

    [Fact]
    public void Read_ReadsTableColumns_IncludingOnlyColumns()
    {
        // 検証: [MemoryTable] の型を含む定義を読む
        // 期待: テーブル名・名前空間・全カラム([ServerOnly] を含む)が読まれる。get だけのプロパティと static は列にならない
        using var temp = new TempDirectory();
        temp.Write("Definitions/Item.cs", Source);

        var schema = Read(temp);

        Assert.Empty(schema.Errors);
        var table = Assert.Single(schema.Tables);
        Assert.Equal("item", table.TableName);
        Assert.Equal("Sample.Definitions.Item", table.FullTypeName);
        Assert.Equal(new[] { "ItemId", "Category", "SellPrice" }, table.Columns.Select(c => c.Name));
        Assert.True(table.Columns[0].IsPrimaryKey);
        Assert.False(table.Columns[1].IsPrimaryKey);
        Assert.Equal("ItemCategory", table.Columns[1].TypeName);
    }

    [Fact]
    public void Read_ReadsEnumValuesAndInputNames()
    {
        // 検証: 値を省略したメンバー・負の値・[InputName] のないメンバーを含む enum を読む
        // 期待: 値は C# と同じ規則(省略したら直前 + 1、先頭は 0)。[InputName] がなければ null
        using var temp = new TempDirectory();
        temp.Write("Definitions/Item.cs", Source);

        var schema = Read(temp);

        var category = Assert.Single(schema.Enums);
        Assert.Equal("Sample.Definitions.ItemCategory", category.FullName);
        Assert.Equal(new long[] { 0, 10, 11, -1 }, category.Members.Select(m => m.Value));
        Assert.Equal(new[] { "なし", "武器", null, "負" }, category.Members.Select(m => m.InputName));
    }

    [Fact]
    public void Read_ReadsEnumNestedInType()
    {
        // 検証: 型の中に入れ子にした enum を読む
        // 期待: 外側の型名を持ち、C# の完全名は . で、CLR の完全名は + でつながる
        using var temp = new TempDirectory();
        temp.Write("Definitions/Item.cs", """
            namespace Sample.Definitions
            {
                public class Outer
                {
                    public class Item
                    {
                        public enum Kind { A = 1 }
                    }
                }
            }
            """);

        var schema = Read(temp);

        var kind = Assert.Single(schema.Enums);
        Assert.Equal(new[] { "Outer", "Item" }, kind.ContainingTypes);
        Assert.Equal("Sample.Definitions.Outer.Item.Kind", kind.FullName);
        Assert.Equal("Sample.Definitions.Outer+Item+Kind", kind.ClrFullName);
    }

    [Fact]
    public void Read_ReadsValidationAttributes()
    {
        // 検証: [Unique] と、3通りの書き方の [Exists] を付けた定義を読む
        // 期待: 列ごとに、一意性と参照先(型名・列・AllowEmpty)を読む。エラーはない
        using var temp = new TempDirectory();
        temp.Write("Definitions/Item.cs", """
            namespace Sample
            {
                [MasterMemory.MemoryTable("item")]
                public class Item
                {
                    [MasterMemory.PrimaryKey] public int ItemId { get; init; }
                    [Unique] public string Code { get; init; } = "";
                }

                [MasterMemory.MemoryTable("drop")]
                public class Drop
                {
                    [MasterMemory.PrimaryKey] public int DropId { get; init; }
                    [Exists(typeof(Item))] public int ItemId { get; init; }
                    [Exists(typeof(Sample.Item), nameof(Item.Code), AllowEmpty = true)] public string ItemCode { get; init; } = "";
                    [Exists(typeof(Item), "Code")] public string Other { get; init; } = "";
                }
            }
            """);

        var schema = Read(temp);

        Assert.Empty(schema.Errors);
        Assert.True(schema.Tables.Single(t => t.TableName == "item").Columns.Single(c => c.Name == "Code").IsUnique);
        var drop = schema.Tables.Single(t => t.TableName == "drop").Columns;
        Assert.Equal(new ExistsRule("Item", null, false), drop.Single(c => c.Name == "ItemId").Exists);
        Assert.Equal(new ExistsRule("Sample.Item", "Code", true), drop.Single(c => c.Name == "ItemCode").Exists);
        Assert.Equal(new ExistsRule("Item", "Code", false), drop.Single(c => c.Name == "Other").Exists);
    }

    [Theory]
    [InlineData("[Exists(typeof(Missing))] public int ItemId { get; init; }", "typeof(Missing) が、テーブルの型")]
    [InlineData("[Exists(typeof(Pair))] public int ItemId { get; init; }", "Pair の主キーが1列ではありません")]
    [InlineData("[Exists(typeof(Item), \"Nothing\")] public int ItemId { get; init; }", "参照する列 Item.Nothing がありません")]
    [InlineData("[Exists(typeof(Item))] public string ItemId { get; init; } = \"\";", "列の型が合いません(string と int)")]
    [InlineData("[Exists(\"item\")] public int ItemId { get; init; }", "1つ目の引数は typeof(テーブルの型)")]
    [InlineData("[Exists(typeof(Item), AllowEmpty = 1)] public int ItemId { get; init; }", "AllowEmpty は true か false")]
    public void Read_ReportsInvalidExists(string property, string message)
    {
        // 検証: 参照先がない・複合主キーで列を省いた・列がない・型が合わない・引数の書き方が違う [Exists] を読む
        // 期待: 定義のエラーになり、理由が分かる
        using var temp = new TempDirectory();
        temp.Write("Definitions/Item.cs", """
            namespace Sample
            {
                [MasterMemory.MemoryTable("item")]
                public class Item { [MasterMemory.PrimaryKey] public int ItemId { get; init; } }

                [MasterMemory.MemoryTable("pair")]
                public class Pair
                {
                    [MasterMemory.PrimaryKey(keyOrder: 0)] public int A { get; init; }
                    [MasterMemory.PrimaryKey(keyOrder: 1)] public int B { get; init; }
                }

                [MasterMemory.MemoryTable("drop")]
                public class Drop
                {
                    [MasterMemory.PrimaryKey] public int DropId { get; init; }
            """ + "\n        " + property + "\n    }\n}\n");

        var schema = Read(temp);

        Assert.Contains(message, Assert.Single(schema.Errors));
    }

    [Theory]
    [InlineData("public record Item(int ItemId);", "record")]
    [InlineData("public record Item { public int ItemId { get; init; } }", "record")]
    [InlineData("public struct Item { public int ItemId { get; init; } }", "struct")]
    public void Read_ReportsNonClassTable(string declaration, string kind)
    {
        // 検証: class 以外(record・位置指定 record・struct)のテーブルを読む(review の E1)
        // 期待: テーブルの型にその種類は使えないエラー
        using var temp = new TempDirectory();
        temp.Write("Definitions/Item.cs", "namespace Sample\n{\n    [MasterMemory.MemoryTable(\"item\")]\n    " + declaration + "\n}\n");

        var schema = Read(temp);

        Assert.Contains($"テーブルの型に {kind} は使えません", Assert.Single(schema.Errors));
    }

    [Fact]
    public void Read_ReportsDuplicateTableNames()
    {
        // 検証: 同じテーブル名の型を2つ定義する
        // 期待: テーブル名が重複しているエラー
        using var temp = new TempDirectory();
        temp.Write("Definitions/A.cs", "namespace A { [MasterMemory.MemoryTable(\"item\")] public class Item { public int Id { get; set; } } }");
        temp.Write("Definitions/B.cs", "namespace B { [MasterMemory.MemoryTable(\"item\")] public class Item { public int Id { get; set; } } }");

        var schema = Read(temp);

        Assert.Contains("重複", Assert.Single(schema.Errors));
    }

    [Fact]
    public void Read_ReportsNonLiteralEnumValue()
    {
        // 検証: 値を式(1 << 2)で書いた enum を読む
        // 期待: 整数のリテラルで書くよう求めるエラー
        using var temp = new TempDirectory();
        temp.Write("Definitions/Flags.cs", "namespace A { public enum Flags { A = 1 << 2 } }");

        var schema = Read(temp);

        Assert.Contains("整数のリテラル", Assert.Single(schema.Errors));
    }

    [Theory]
    [InlineData("public enum Big : ulong { A = 9223372036854775808 }")]           // long.MaxValue + 1
    [InlineData("public enum Big : ulong { A = 18446744073709551615 }")]          // ulong.MaxValue
    [InlineData("public enum Big : long { A = 9223372036854775807, B }")]         // 省略した値が long.MaxValue + 1 になる
    public void Read_ReportsEnumValueOutOfLongRange(string declaration)
    {
        // 検証: long に収まらない値を持つ enum を読む(review の指摘 5 の再現)
        // 期待: 例外で落ちず、long の範囲を超えているエラー
        using var temp = new TempDirectory();
        temp.Write("Definitions/Big.cs", $"namespace A {{ {declaration} }}");

        var schema = Read(temp);

        Assert.Contains("long の範囲を超えています", Assert.Single(schema.Errors));
    }

    [Fact]
    public void Read_ReadsLongMinValue()
    {
        // 検証: long.MinValue(リテラル 9223372036854775808 は ulong になる)を負の値で書く
        // 期待: エラーにならず、long.MinValue として読む
        using var temp = new TempDirectory();
        temp.Write("Definitions/Small.cs", "namespace A { public enum Small : long { A = -9223372036854775808 } }");

        var schema = Read(temp);

        Assert.Empty(schema.Errors);
        Assert.Equal(long.MinValue, Assert.Single(Assert.Single(schema.Enums).Members).Value);
    }
}
