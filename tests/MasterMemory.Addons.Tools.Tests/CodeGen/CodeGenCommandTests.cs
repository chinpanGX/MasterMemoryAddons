using System.IO;
using MasterMemory.Addons.Tools.Common;
using MasterMemory.Addons.Tools.CodeGen;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests.CodeGen;

public class CodeGenCommandTests
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

    private const string ConfigYaml = """
        definitions: Definitions
        outputs:
          - { name: unity, target: client, source: ../out/unity, namespace: Sample.MasterData }
          - { name: api-server, target: server, source: ../out/api, namespace: Sample.MasterData }
          - { name: battle-server, target: server, source: ../out/battle, namespace: Sample.MasterData }
        environments:
          dev:
            outputs:
              unity: { source: ../out/unity-dev }
        """;

    private static int Run(params string[] args)
    {
        return CodeGenCommand.Run(args, new StringWriter(), new StringWriter());
    }

    private static TempDirectory CreateProject()
    {
        var temp = new TempDirectory();
        temp.Write("config/Definitions/Item.cs", ItemSource);
        temp.Write("config/masterdata.yaml", ConfigYaml);
        return temp;
    }

    [Fact]
    public void Config_GeneratesEachOutputWithItsTarget()
    {
        // 検証: client 1つ・server 2つの出力先がある設定で実行する
        // 期待: client の出力先には [ClientOnly] の列だけ、server の出力先には [ServerOnly] の列だけが入る
        using var temp = CreateProject();

        Assert.Equal(0, Run("--config", temp.Combine("config/masterdata.yaml")));

        var unity = File.ReadAllText(temp.Combine("out/unity/Item.cs"));
        Assert.Contains("IconAddress", unity);
        Assert.DoesNotContain("SellPrice", unity);

        foreach (var server in new[] { "out/api/Item.cs", "out/battle/Item.cs" })
        {
            var text = File.ReadAllText(temp.Combine(server));
            Assert.Contains("SellPrice", text);
            Assert.DoesNotContain("IconAddress", text);
        }
    }

    [Fact]
    public void Config_WritesMasterDataSchema_ToEachOutput()
    {
        // 検証: 同じ設定で実行する
        // 期待: MasterDataSchema は client / server のすべての出力先に、設定した namespace で出力される。
        //       値は target ごとの生成ソースから計算するので、client と server で違い、同じ target では同じ
        using var temp = CreateProject();

        Assert.Equal(0, Run("--config", temp.Combine("config/masterdata.yaml")));

        var unity = File.ReadAllText(temp.Combine("out/unity/" + MasterDataSchemaGenerator.FileName));
        var api = File.ReadAllText(temp.Combine("out/api/" + MasterDataSchemaGenerator.FileName));
        var battle = File.ReadAllText(temp.Combine("out/battle/" + MasterDataSchemaGenerator.FileName));
        Assert.Contains("namespace Sample.MasterData", unity);
        Assert.Contains("public const string Hash", unity);
        Assert.NotEqual(unity, api);
        Assert.Equal(api, battle);
    }

    [Fact]
    public void Config_Returns2_WhenDefinitionConflictsWithMasterDataSchema()
    {
        // 検証: MasterDataSchema と同じ名前の定義ファイルを置いて実行する
        // 期待: 2 を返し、出力先は作られない
        using var temp = CreateProject();
        temp.Write("config/Definitions/" + MasterDataSchemaGenerator.FileName, ItemSource.Replace("Item", "Other"));

        Assert.Equal(2, Run("--config", temp.Combine("config/masterdata.yaml")));

        Assert.False(Directory.Exists(temp.Combine("out/unity")));
    }

    [Fact]
    public void Config_Returns2_WhenDefinitionDeclaresMasterDataSchemaInOutputNamespace()
    {
        // 検証: 出力先の namespace(Sample.MasterData)に MasterDataSchema 型を宣言した定義を、別のフォルダ・別のファイル名で置く(review の E7)
        // 期待: 2 を返し、ファイルと出力先の名前を表示する。出力先は作られない
        using var temp = CreateProject();
        temp.Write("config/Definitions/Sub/Other.cs", "namespace Sample.MasterData { public static class MasterDataSchema { } }");
        var stderr = new StringWriter();

        var code = CodeGenCommand.Run(new[] { "--config", temp.Combine("config/masterdata.yaml") }, new StringWriter(), stderr);

        Assert.Equal(2, code);
        Assert.Contains("Other.cs: outputs[unity] の namespace(Sample.MasterData)に、生成する MasterDataSchema と同じ名前の型があります", stderr.ToString());
        Assert.False(Directory.Exists(temp.Combine("out/unity")));
    }

    [Theory]
    [InlineData("namespace Sample.Other { public static class MasterDataSchema { } }")]
    [InlineData("namespace Sample.MasterData { public class Outer { public class MasterDataSchema { } } }")]
    public void Config_Succeeds_WhenMasterDataSchemaTypeDoesNotCollide(string source)
    {
        // 検証: 別の名前空間、または型の中に入れ子にした MasterDataSchema 型を宣言した定義を置く
        // 期待: 名前は重ならないので、0 を返す
        using var temp = CreateProject();
        temp.Write("config/Definitions/Other.cs", source);

        Assert.Equal(0, Run("--config", temp.Combine("config/masterdata.yaml")));
    }

    [Fact]
    public void Config_WithEnv_UsesOverriddenSource()
    {
        // 検証: --env dev で実行する
        // 期待: unity は dev の出力先に出力され、元の出力先は作られない。他の出力先は基本の設定のまま
        using var temp = CreateProject();

        Assert.Equal(0, Run("--config", temp.Combine("config/masterdata.yaml"), "--env", "dev"));

        Assert.True(File.Exists(temp.Combine("out/unity-dev/Item.cs")));
        Assert.False(Directory.Exists(temp.Combine("out/unity")));
        Assert.True(File.Exists(temp.Combine("out/api/Item.cs")));
    }

    [Fact]
    public void Config_ReplacesOutputDirectory_AndKeepsFilesOutsideIt()
    {
        // 検証: 以前の生成物と、出力先の外に手書きのファイルがある状態で実行する
        // 期待: 以前の生成物は消え、出力先の外のファイル・定義・設定ファイルは残る
        using var temp = CreateProject();
        var stale = temp.Write("out/unity/Removed.cs", "// 以前の生成物");
        var outside = temp.Write("out/Handwritten.cs", "// 出力先の外の手書きファイル");

        Assert.Equal(0, Run("--config", temp.Combine("config/masterdata.yaml")));

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(outside));
        Assert.True(File.Exists(temp.Combine("config/Definitions/Item.cs")));
        Assert.True(File.Exists(temp.Combine("config/masterdata.yaml")));
    }

    [Fact]
    public void Config_DoesNotTouchOutputs_WhenDefinitionHasSyntaxError()
    {
        // 検証: 定義の1つに構文エラーがある状態で実行する
        // 期待: 1 を返し、既存の出力先は変わらず、新しい出力先も作られない
        using var temp = CreateProject();
        temp.Write("config/Definitions/Broken.cs", "public class {");
        var existing = temp.Write("out/unity/Existing.cs", "existing");

        Assert.Equal(1, Run("--config", temp.Combine("config/masterdata.yaml")));

        Assert.True(File.Exists(existing));
        Assert.False(Directory.Exists(temp.Combine("out/api")));
    }

    [Fact]
    public void Config_Returns1_AndDoesNotTouchOutputs_WhenTableTypeIsRecord()
    {
        // 検証: テーブルの型を record にした定義で実行する
        // 期待: 1 を返し、定義のエラーとして表示する。既存の出力先は変わらず、新しい出力先も作られない
        using var temp = CreateProject();
        temp.Write("config/Definitions/Item.cs", ItemSource.Replace("public class Item", "[MasterMemory.MemoryTable(\"item\")] public record Item"));
        var existing = temp.Write("out/unity/Existing.cs", "existing");
        var stderr = new StringWriter();

        var code = CodeGenCommand.Run(new[] { "--config", temp.Combine("config/masterdata.yaml") }, new StringWriter(), stderr);

        Assert.Equal(1, code);
        Assert.Contains("Item.cs: 定義のエラー", stderr.ToString());
        Assert.Contains("テーブルの型に record は使えません", stderr.ToString());
        Assert.True(File.Exists(existing));
        Assert.False(Directory.Exists(temp.Combine("out/api")));
    }

    [Fact]
    public void Config_DoesNotDeleteAnything_WhenSourceIsUnsafe()
    {
        // 検証: 定義を含むディレクトリを出力先にして実行する
        // 期待: 2 を返し、定義も手書きのファイルも消えない
        using var temp = new TempDirectory();
        var definition = temp.Write("config/Definitions/Item.cs", ItemSource);
        var handwritten = temp.Write("Handwritten.cs", "// 手書き");
        temp.Write("config/masterdata.yaml", """
            definitions: Definitions
            outputs:
              - { name: unity, target: client, source: .., namespace: Sample.MasterData }
            """);

        Assert.Equal(2, Run("--config", temp.Combine("config/masterdata.yaml")));

        Assert.True(File.Exists(definition));
        Assert.True(File.Exists(handwritten));
    }

    [Fact]
    public void Config_DoesNotDeleteCsv_WhenSourceContainsCsv()
    {
        // 検証: csv のディレクトリを含むディレクトリを出力先にして実行する(review の指摘 1 の再現)
        // 期待: 2 を返し、CSV は消えない
        using var temp = new TempDirectory();
        temp.Write("config/Definitions/Item.cs", ItemSource);
        var csv = temp.Write("config/gen2/csv/item.csv", "c\nc\nint\nItemId\n1\n");
        temp.Write("config/masterdata.yaml", """
            definitions: Definitions
            csv: gen2/csv
            outputs:
              - { name: unity, target: client, source: gen2, namespace: Sample.MasterData }
            """);

        Assert.Equal(2, Run("--config", temp.Combine("config/masterdata.yaml")));

        Assert.True(File.Exists(csv));
    }

    [Theory]
    [InlineData("--config", "x.yaml", "--input", "Definitions")]
    [InlineData("--config", "x.yaml", "--target", "client")]
    [InlineData("--env", "dev", "--input", "a", "--target", "client", "--output", "b")]
    public void InvalidArgumentCombination_Returns2(params string[] args)
    {
        // 検証: --config と --input / --target を同時に、または --env を --config なしで指定する
        // 期待: 2 を返す
        Assert.Equal(2, Run(args));
    }

    [Fact]
    public void Config_Returns2_WhenConfigIsInvalid()
    {
        // 検証: 存在しない環境や存在しない設定ファイルを指定する
        // 期待: 2 を返す
        using var temp = CreateProject();

        Assert.Equal(2, Run("--config", temp.Combine("config/masterdata.yaml"), "--env", "unknown"));
        Assert.Equal(2, Run("--config", temp.Combine("config/missing.yaml")));
    }
}
