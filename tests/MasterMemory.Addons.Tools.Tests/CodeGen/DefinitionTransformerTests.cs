using System.Linq;
using MasterMemory.Addons.Tools.Common;
using MasterMemory.Addons.Tools.CodeGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests.CodeGen;

public class DefinitionTransformerTests
{
    private const string ClassSource = """
        using MasterMemory;
        using MasterMemory.Addons;

        namespace Sample
        {
            [MemoryTable("item")]
            public class Item
            {
                [PrimaryKey]
                public int ItemId { get; init; }

                public string Name { get; init; } = "";

                [ClientOnly]
                public string IconAddress { get; init; } = "";

                [ServerOnly]
                public int SellPrice { get; init; }
            }
        }
        """;

    private const string RecordSource = """
        using MasterMemory.Addons;

        namespace Sample
        {
            public record Skill(
                [property: PrimaryKey] int Id,
                [property: ServerOnly] int Damage,
                [property: ClientOnly] string EffectAddress,
                string Name);
        }
        """;

    private const string TypeLevelSource = """
        using MasterMemory.Addons;

        namespace Sample
        {
            public class Shared { public int Id { get; init; } }

            [ServerOnly]
            public class ServerTable { public int Id { get; init; } }

            [ClientOnly]
            public class ClientTable { public int Id { get; init; } }
        }
        """;

    // code-gen と同じく、DefinitionSource でパースする
    private static SyntaxTree Parse(string source)
    {
        return DefinitionSource.Parse("Test.cs", source).Tree;
    }

    private static string Transform(string source, GenerationTarget target)
    {
        var result = DefinitionTransformer.Transform(Parse(source), target);
        Assert.NotNull(result);
        return result;
    }

    private static void AssertValidCSharp(string code)
    {
        var errors = CSharpSyntaxTree.ParseText(code).GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Client_RemovesServerOnlyMembers_AndKeepsClientOnlyMembers()
    {
        // 検証: [ClientOnly] / [ServerOnly] の列があるクラスを client 向けに変換する
        // 期待: [ServerOnly] の列だけが消え、共通の列と [ClientOnly] の列は残る
        var output = Transform(ClassSource, GenerationTarget.Client);

        Assert.Contains("ItemId", output);
        Assert.Contains("Name", output);
        Assert.Contains("IconAddress", output);
        Assert.DoesNotContain("SellPrice", output);
    }

    [Fact]
    public void Server_RemovesClientOnlyMembers_AndKeepsServerOnlyMembers()
    {
        // 検証: 同じクラスを server 向けに変換する
        // 期待: [ClientOnly] の列だけが消え、[ServerOnly] の列は残る
        var output = Transform(ClassSource, GenerationTarget.Server);

        Assert.Contains("ItemId", output);
        Assert.Contains("SellPrice", output);
        Assert.DoesNotContain("IconAddress", output);
    }

    [Theory]
    [InlineData(GenerationTarget.Client)]
    [InlineData(GenerationTarget.Server)]
    public void Output_HasNoOnlyAttributes_AndNoMarkerUsing(GenerationTarget target)
    {
        // 検証: client / server それぞれに変換する
        // 期待: ClientOnly / ServerOnly 属性と using MasterMemory.Addons が消え、MasterMemory の属性と using は残る
        var output = Transform(ClassSource, target);

        Assert.DoesNotContain("ClientOnly", output.Replace("IconAddress", ""));
        Assert.DoesNotContain("ServerOnly", output);
        Assert.DoesNotContain("using MasterMemory.Addons;", output);
        Assert.Contains("using MasterMemory;", output);
        Assert.Contains("[PrimaryKey]", output);
    }

    [Theory]
    [InlineData(GenerationTarget.Client)]
    [InlineData(GenerationTarget.Server)]
    public void Output_IsValidCSharp(GenerationTarget target)
    {
        // 検証: クラス・位置指定 record・型単位の指定を、それぞれの対象に変換する
        // 期待: どの出力も構文エラーがない
        AssertValidCSharp(Transform(ClassSource, target));
        AssertValidCSharp(Transform(RecordSource, target));
        AssertValidCSharp(Transform(TypeLevelSource, target));
    }

    [Fact]
    public void PositionalRecord_Client_RemovesServerOnlyParameter()
    {
        // 検証: 位置指定 record を client 向けに変換する
        // 期待: [ServerOnly] の引数が消え、残りの引数は残る。Only 属性も残らない
        var output = Transform(RecordSource, GenerationTarget.Client);

        Assert.Contains("int Id", output);
        Assert.Contains("EffectAddress", output);
        Assert.Contains("string Name", output);
        Assert.DoesNotContain("Damage", output);
        Assert.DoesNotContain("Only", output);
    }

    [Fact]
    public void PositionalRecord_Server_RemovesClientOnlyParameter()
    {
        // 検証: 位置指定 record を server 向けに変換する
        // 期待: [ClientOnly] の引数が消える。Only 属性も残らない
        var output = Transform(RecordSource, GenerationTarget.Server);

        Assert.Contains("Damage", output);
        Assert.DoesNotContain("EffectAddress", output);
        Assert.DoesNotContain("Only", output);
    }

    [Fact]
    public void TypeLevel_Client_RemovesServerOnlyType()
    {
        // 検証: 型に [ServerOnly] / [ClientOnly] を付けた定義を client 向けに変換する
        // 期待: [ServerOnly] の型だけが消える
        var output = Transform(TypeLevelSource, GenerationTarget.Client);

        Assert.Contains("Shared", output);
        Assert.Contains("ClientTable", output);
        Assert.DoesNotContain("ServerTable", output);
    }

    [Fact]
    public void TypeLevel_Server_RemovesClientOnlyType()
    {
        // 検証: 同じ定義を server 向けに変換する
        // 期待: [ClientOnly] の型だけが消える
        var output = Transform(TypeLevelSource, GenerationTarget.Server);

        Assert.Contains("Shared", output);
        Assert.Contains("ServerTable", output);
        Assert.DoesNotContain("ClientTable", output);
    }

    [Fact]
    public void QualifiedAndSuffixedAttributeNames_AreRecognized()
    {
        // 検証: 名前空間付き(MasterMemory.Addons.ServerOnly)と Attribute 付き(ServerOnlyAttribute)の書き方を client 向けに変換する
        // 期待: どちらも [ServerOnly] として扱われ、列が消える
        const string source = """
            namespace Sample
            {
                public class A
                {
                    public int Id { get; init; }

                    [MasterMemory.Addons.ServerOnly]
                    public int Qualified { get; init; }

                    [ServerOnlyAttribute]
                    public int Suffixed { get; init; }
                }
            }
            """;

        var output = Transform(source, GenerationTarget.Client);

        Assert.Contains("Id", output);
        Assert.DoesNotContain("Qualified", output);
        Assert.DoesNotContain("Suffixed", output);
    }

    [Fact]
    public void ReturnsNull_WhenNoTypeRemains()
    {
        // 検証: [ServerOnly] の型しかないファイルを変換する
        // 期待: client では null(出力しない)、server では出力がある
        const string source = """
            using MasterMemory.Addons;

            namespace Sample
            {
                [ServerOnly]
                public class OnlyServer { public int Id { get; init; } }
            }
            """;

        Assert.Null(DefinitionTransformer.Transform(Parse(source), GenerationTarget.Client));
        Assert.NotNull(DefinitionTransformer.Transform(Parse(source), GenerationTarget.Server));
    }

    [Theory]
    [InlineData("public record Item { public int ItemId { get; init; } }", "record")]
    [InlineData("public record class Item(int ItemId);", "record")]
    [InlineData("public record struct Item(int ItemId);", "record struct")]
    [InlineData("public struct Item { public int ItemId { get; init; } }", "struct")]
    [InlineData("public interface Item { int ItemId { get; } }", "interface")]
    public void Throws_WhenTableTypeIsNotClass(string declaration, string kind)
    {
        // 検証: [MemoryTable] を付けた class 以外の型(record・record struct・struct・interface)を変換する(review の E1)
        // 期待: DefinitionParseException になり、行と列・型の名前・型の種類・class で書くことが分かる
        var source = "namespace Sample\n{\n    [MasterMemory.MemoryTable(\"item\")]\n    " + declaration + "\n}\n";

        var ex = Assert.Throws<DefinitionParseException>(() => DefinitionTransformer.Transform(Parse(source), GenerationTarget.Client));

        Assert.Contains("(4,", ex.Message);
        Assert.Contains($"Item: テーブルの型に {kind} は使えません。class で書いてください", ex.Message);
    }

    [Fact]
    public void AllowsClassTable()
    {
        // 検証: [MemoryTable] を付けた class を変換する
        // 期待: エラーにならない
        var source = "namespace Sample\n{\n    [MasterMemory.MemoryTable(\"item\")]\n    public class Item { public int ItemId { get; init; } }\n}\n";

        Assert.NotNull(DefinitionTransformer.Transform(Parse(source), GenerationTarget.Client));
    }

    [Fact]
    public void AllowsRecord_WhenTypeIsNotTable()
    {
        // 検証: [MemoryTable] を付けていない record を変換する
        // 期待: テーブルの型ではないので、エラーにならない
        Assert.NotNull(DefinitionTransformer.Transform(Parse(RecordSource), GenerationTarget.Client));
    }

    [Fact]
    public void Throws_WhenSourceHasSyntaxError()
    {
        // 検証: 構文エラーのあるソースを変換する
        // 期待: DefinitionParseException になる
        Assert.Throws<DefinitionParseException>(
            () => DefinitionTransformer.Transform(Parse("public class {"), GenerationTarget.Client));
    }

    [Theory]
    [InlineData(GenerationTarget.Client)]
    [InlineData(GenerationTarget.Server)]
    public void ValidationAttributes_AreStripped(GenerationTarget target)
    {
        // 検証: [Unique] と [Exists] を、ほかの属性と同じリスト・単独のリストで付けた定義を変換する
        // 期待: [Unique] / [Exists] だけが消え、[PrimaryKey] と列は残る。構文エラーもない
        const string source = """
            using MasterMemory;
            using MasterMemory.Addons;

            namespace Sample
            {
                public class Drop
                {
                    [PrimaryKey, Unique]
                    public int DropId { get; init; }

                    [Exists(typeof(Item), nameof(Item.Code), AllowEmpty = true)]
                    public string ItemCode { get; init; } = "";
                }
            }
            """;

        var output = Transform(source, target);

        Assert.DoesNotContain("Unique", output);
        Assert.DoesNotContain("Exists", output);
        Assert.Contains("[PrimaryKey]", output);
        Assert.Contains("ItemCode", output);
        AssertValidCSharp(output);
    }

    [Theory]
    [InlineData(GenerationTarget.Client)]
    [InlineData(GenerationTarget.Server)]
    public void InputName_IsStripped_AndEnumMembersAreKept(GenerationTarget target)
    {
        // 検証: [InputName] を付けた enum を変換する
        // 期待: InputName と入力名は消え、enum のメンバーと値は残る。構文エラーもない
        const string source = """
            using MasterMemory.Addons;

            namespace Sample
            {
                public enum ItemCategory
                {
                    [InputName("武器")] Weapon = 1,
                    [InputName("防具")] Armor = 2,
                }
            }
            """;

        var output = Transform(source, target);

        Assert.Contains("Weapon = 1", output);
        Assert.Contains("Armor = 2", output);
        Assert.DoesNotContain("InputName", output);
        Assert.DoesNotContain("武器", output);
        AssertValidCSharp(output);
    }
}
