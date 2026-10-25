using System.Text.RegularExpressions;
using MasterMemory.Addons.Tools.Common;
using MasterMemory.Addons.Tools.CodeGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests.CodeGen;

public class MasterDataSchemaGeneratorTests
{
    private const string ItemSource = """
        using MasterMemory.Addons;

        namespace Sample
        {
            public class Item
            {
                public int ItemId { get; init; }

                [ClientOnly]
                public string IconAddress { get; init; } = "";

                [ServerOnly]
                public int SellPrice { get; init; }
            }
        }
        """;

    private const string EnumSource = """
        namespace Sample
        {
            public enum ItemCategory
            {
                Weapon = 1,
                Armor = 2,
            }
        }
        """;

    // 定義から、code-gen コマンドと同じ手順でクライアント用のソースを作ってスキーマのハッシュを計算する
    private static string ComputeClientKey(params (string Path, string Source)[] definitions)
    {
        var files = new System.Collections.Generic.List<GeneratedFile>();
        foreach (var (path, source) in definitions)
        {
            var text = DefinitionTransformer.Transform(DefinitionSource.Parse(path, source).Tree, GenerationTarget.Client);
            if (text is not null)
            {
                files.Add(new GeneratedFile(path, text));
            }
        }

        return MasterDataSchemaGenerator.Compute(files);
    }

    [Fact]
    public void Compute_ReturnsLowercaseSha256Hex()
    {
        // 検証: クライアント用のソースからスキーマのハッシュを計算する
        // 期待: 小文字 16 進数 64 文字(SHA-256)になる
        var key = ComputeClientKey(("Item.cs", ItemSource));

        Assert.Matches(new Regex("^[0-9a-f]{64}$"), key);
    }

    [Fact]
    public void Compute_IgnoresCommentsAndWhitespace()
    {
        // 検証: コメントと空白だけを変えた定義で計算する
        // 期待: 元の定義と同じ値になる
        var commented = ItemSource
            .Replace("public int ItemId", "// アイテムの ID\n        public   int   ItemId")
            .Replace("namespace Sample", "/* サンプル */\nnamespace Sample");

        Assert.Equal(ComputeClientKey(("Item.cs", ItemSource)), ComputeClientKey(("Item.cs", commented)));
    }

    [Fact]
    public void Compute_IgnoresServerOnlyChanges()
    {
        // 検証: [ServerOnly] の列の型だけを変えた定義で計算する
        // 期待: 元の定義と同じ値になる(クライアント用のソースに含まれないため)
        var changed = ItemSource.Replace("public int SellPrice", "public long SellPrice");

        Assert.Equal(ComputeClientKey(("Item.cs", ItemSource)), ComputeClientKey(("Item.cs", changed)));
    }

    [Theory]
    [InlineData("public string IconAddress", "public string IconPath")]
    [InlineData("public int ItemId", "public long ItemId")]
    [InlineData("[ClientOnly]", "[ClientOnly, PrimaryKey]")]
    public void Compute_ChangesWhenClientDefinitionChanges(string from, string to)
    {
        // 検証: クライアント用のソースに残る列の名前・型・属性を変えた定義で計算する
        // 期待: 元の定義と違う値になる
        var changed = ItemSource.Replace(from, to);

        Assert.NotEqual(ComputeClientKey(("Item.cs", ItemSource)), ComputeClientKey(("Item.cs", changed)));
    }

    [Fact]
    public void Compute_DoesNotDependOnFileNamesOrOrder()
    {
        // 検証: 同じ定義をファイル名と順番を変えて計算する
        // 期待: 同じ値になる
        var key = ComputeClientKey(("Item.cs", ItemSource), ("ItemCategory.cs", EnumSource));
        var renamed = ComputeClientKey(("Enums/Category.cs", EnumSource), ("Tables/Item.cs", ItemSource));

        Assert.Equal(key, renamed);
    }

    [Fact]
    public void Generate_ProducesValidCSharp9WithHash()
    {
        // 検証: MasterDataSchema のソースを出力する
        // 期待: C# 9 で構文エラーがなく、指定した名前空間・クラス名・値が入る
        var text = MasterDataSchemaGenerator.Generate("Sample.MasterData", "abc123");

        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp9));
        Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("namespace Sample.MasterData", text);
        Assert.Contains("public static class MasterDataSchema", text);
        Assert.Contains("public const string Hash = \"abc123\";", text);
    }
}
