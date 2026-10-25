using System.IO;
using System.Linq;
using MasterMemory.Addons.Tools.Common;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests.Common;

public class ConfigLoaderTests
{
    private static readonly string BaseDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mma-config"));

    private const string Yaml = """
        definitions: Definitions
        outputs:
          - name: unity
            target: client
            source: ../Client/Generated
            namespace: Sample.MasterData
          - name: api-server
            target: server
            source: ../Server/Api/Generated
            namespace: Sample.MasterData.Api
          - name: battle-server
            target: server
            source: ../Server/Battle/Generated
            namespace: Sample.MasterData.Battle
        environments:
          dev:
            outputs:
              unity:
                source: ../Client/GeneratedDev
          staging:
            definitions: DefinitionsStaging
          production: {}
          empty:
        """;

    private static string Resolve(string relative)
    {
        return Path.GetFullPath(Path.Combine(BaseDirectory, relative));
    }

    [Fact]
    public void Parse_ReadsOutputs_AndResolvesPathsFromConfigDirectory()
    {
        // 検証: 環境を指定せずに設定を読む
        // 期待: outputs が順に読め、パスは設定ファイルのディレクトリを基準にした絶対パスになる
        var config = ConfigLoader.Parse(Yaml, BaseDirectory, null);

        Assert.Equal(BaseDirectory, config.ConfigDirectory);
        Assert.Equal(Resolve("Definitions"), config.Definitions);
        Assert.Equal(new[] { "unity", "api-server", "battle-server" }, config.Outputs.Select(o => o.Name));
        Assert.Equal(GenerationTarget.Client, config.Outputs[0].Target);
        Assert.Equal(GenerationTarget.Server, config.Outputs[1].Target);
        Assert.Equal(Resolve("../Client/Generated"), config.Outputs[0].Source);
        Assert.Equal(Resolve("../Server/Battle/Generated"), config.Outputs[2].Source);
    }

    [Fact]
    public void Parse_WithEnvironment_OverridesOnlyWrittenItems()
    {
        // 検証: dev 環境を指定して読む
        // 期待: dev に書いた unity の source だけが変わり、他の項目と出力先は基本の設定のまま
        var config = ConfigLoader.Parse(Yaml, BaseDirectory, "dev");

        var unity = config.Outputs[0];
        Assert.Equal(Resolve("../Client/GeneratedDev"), unity.Source);
        Assert.Equal(GenerationTarget.Client, unity.Target);

        // 書いていない項目と出力先は基本の設定のまま
        Assert.Equal(Resolve("Definitions"), config.Definitions);
        Assert.Equal(Resolve("../Server/Api/Generated"), config.Outputs[1].Source);
        Assert.Equal(3, config.Outputs.Count);
    }

    [Fact]
    public void Parse_WithEnvironment_CanOverrideDefinitions()
    {
        // 検証: definitions を上書きする staging 環境を指定して読む
        // 期待: definitions だけが変わる
        var config = ConfigLoader.Parse(Yaml, BaseDirectory, "staging");

        Assert.Equal(Resolve("DefinitionsStaging"), config.Definitions);
        Assert.Equal(Resolve("../Client/Generated"), config.Outputs[0].Source);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("empty")]
    public void Parse_WithEmptyEnvironment_EqualsBase(string environment)
    {
        // 検証: 中身が空の環境を指定して読む
        // 期待: 環境を指定しないときと同じ設定になる
        var baseConfig = ConfigLoader.Parse(Yaml, BaseDirectory, null);
        var config = ConfigLoader.Parse(Yaml, BaseDirectory, environment);

        Assert.Equal(baseConfig.Definitions, config.Definitions);
        Assert.Equal(baseConfig.Outputs, config.Outputs);
    }

    [Fact]
    public void Parse_Throws_WhenEnvironmentIsUnknown()
    {
        // 検証: environments にない環境を指定する
        // 期待: 環境名を含む ConfigException になる
        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Parse(Yaml, BaseDirectory, "qa"));
        Assert.Contains("qa", ex.Message);
    }

    [Fact]
    public void Parse_Throws_WhenOverrideNamesUnknownOutput()
    {
        // 検証: 環境の上書きに、outputs にない出力先の名前を書く
        // 期待: その名前を含む ConfigException になる
        const string yaml = """
            definitions: Definitions
            outputs:
              - { name: unity, target: client, source: out/client, namespace: Sample.MasterData }
            environments:
              dev:
                outputs:
                  untiy: { source: out/dev }
            """;

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, "dev"));
        Assert.Contains("untiy", ex.Message);
    }

    [Theory]
    [InlineData("binaryPath: out/masterdata.bytes")]
    [InlineData("encryption: aes")]
    public void Parse_Throws_OnUnknownOutputItem(string item)
    {
        // 検証: 出力先に、ない項目(binary / encrypt の打ち間違いなど)を書く
        // 期待: 「設定できない項目です」の ConfigException になる
        var yaml = $"""
            definitions: Definitions
            outputs:
              - name: unity
                target: client
                source: out/client
                namespace: Sample.MasterData
                {item}
            """;

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, null));
        Assert.Contains(item.Split(':')[0] + " は設定できない項目です", ex.Message);
    }

    [Fact]
    public void Parse_ReadsBuildItems()
    {
        // 検証: csv、binary、encrypt を書いた設定を読む
        // 期待: csv と binary は設定ファイルのディレクトリから解決される。encrypt を省略した出力先は false、binary を省略した出力先は null
        const string yaml = """
            definitions: Definitions
            csv: csv
            outputs:
              - { name: unity, target: client, source: out/client, namespace: Sample.MasterData, binary: out/client.bytes, encrypt: true }
              - { name: api-server, target: server, source: out/server, namespace: Sample.MasterData, binary: out/server.bytes }
              - { name: battle-server, target: server, source: out/battle, namespace: Sample.MasterData }
            """;

        var config = ConfigLoader.Parse(yaml, BaseDirectory, null);

        Assert.Equal(Resolve("csv"), config.Csv);
        Assert.Equal(Resolve("out/client.bytes"), config.Outputs[0].Binary);
        Assert.True(config.Outputs[0].Encrypt);
        Assert.Equal(Resolve("out/server.bytes"), config.Outputs[1].Binary);
        Assert.False(config.Outputs[1].Encrypt);
        Assert.Null(config.Outputs[2].Binary);
    }

    [Fact]
    public void Parse_WithoutCsv_HasNullCsv()
    {
        // 検証: csv を書かない設定を読む(code-gen だけを使う場合)
        // 期待: エラーにならず、Csv は null
        var config = ConfigLoader.Parse(Yaml, BaseDirectory, null);

        Assert.Null(config.Csv);
    }

    [Fact]
    public void Parse_WithEnvironment_CanOverrideBuildItems()
    {
        // 検証: 環境で csv、binary、encrypt を上書きする
        // 期待: 上書きした値になる
        const string yaml = """
            definitions: Definitions
            csv: csv
            outputs:
              - { name: unity, target: client, source: out/client, namespace: Sample.MasterData, binary: out/client.bytes, encrypt: true }
            environments:
              dev:
                csv: csv-dev
                outputs:
                  unity: { binary: out/client-dev.bytes, encrypt: false }
            """;

        var config = ConfigLoader.Parse(yaml, BaseDirectory, "dev");

        Assert.Equal(Resolve("csv-dev"), config.Csv);
        Assert.Equal(Resolve("out/client-dev.bytes"), config.Outputs[0].Binary);
        Assert.False(config.Outputs[0].Encrypt);
    }

    [Fact]
    public void Parse_Throws_WhenServerEncrypts()
    {
        // 検証: target が server の出力先に encrypt: true を書く
        // 期待: ConfigException になる(暗号化のパスワードは client 用だけにあるため)
        const string yaml = """
            definitions: Definitions
            outputs:
              - { name: api-server, target: server, source: out/server, namespace: Sample.MasterData, binary: out/server.bytes, encrypt: true }
            """;

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, null));
        Assert.Contains("encrypt", ex.Message);
    }

    [Fact]
    public void Parse_Throws_OnUnknownTopLevelItem()
    {
        // 検証: 最上位に、未知の項目(spreadsheet)を書く
        // 期待: ConfigException になる
        const string yaml = """
            definitions: Definitions
            spreadsheet:
              id: abc
            outputs:
              - { name: unity, target: client, source: out/client, namespace: Sample.MasterData }
            """;

        Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, null));
    }

    [Fact]
    public void Parse_Throws_WhenNamesAreDuplicated()
    {
        // 検証: 出力先の name を重複させる
        // 期待: 「重複」を含む ConfigException になる
        const string yaml = """
            definitions: Definitions
            outputs:
              - { name: server, target: server, source: out/a, namespace: Sample.MasterData }
              - { name: server, target: server, source: out/b, namespace: Sample.MasterData }
            """;

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, null));
        Assert.Contains("重複", ex.Message);
    }

    [Theory]
    [InlineData("web")]
    [InlineData("0")]
    public void Parse_Throws_WhenTargetIsInvalid(string target)
    {
        // 検証: target に client / server 以外(数値を含む)を書く
        // 期待: ConfigException になる
        var yaml = $"""
            definitions: Definitions
            outputs:
              - name: unity
                target: "{target}"
                source: out/client
            """;

        Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, null));
    }

    [Theory]
    [InlineData("outputs:\n  - { name: unity, target: client, source: out }")]
    [InlineData("definitions: Definitions")]
    [InlineData("definitions: Definitions\noutputs:\n  - { name: unity, source: out }")]
    [InlineData("definitions: Definitions\noutputs:\n  - { target: client, source: out }")]
    [InlineData("definitions: Definitions\noutputs:\n  - { name: unity, target: client }")]
    [InlineData("")]
    public void Parse_Throws_WhenRequiredItemIsMissing(string yaml)
    {
        // 検証: definitions / outputs / name / target / source のどれかを省く
        // 期待: ConfigException になる
        Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, null));
    }

    [Fact]
    public void Parse_ReadsNamespace_ForClientAndServer()
    {
        // 検証: client と server の出力先がある設定を読む
        // 期待: どちらにも namespace が入る
        var config = ConfigLoader.Parse(Yaml, BaseDirectory, null);

        Assert.Equal("Sample.MasterData", config.Outputs[0].Namespace);
        Assert.Equal("Sample.MasterData.Api", config.Outputs[1].Namespace);
    }

    [Fact]
    public void Parse_WithEnvironment_CanOverrideNamespace()
    {
        // 検証: namespace を上書きする環境を指定して読む
        // 期待: namespace が上書きした値になる
        const string yaml = """
            definitions: Definitions
            outputs:
              - { name: unity, target: client, source: out/client, namespace: Sample.MasterData }
            environments:
              dev:
                outputs:
                  unity: { namespace: Sample.MasterData.Dev }
            """;

        var config = ConfigLoader.Parse(yaml, BaseDirectory, "dev");

        Assert.Equal("Sample.MasterData.Dev", config.Outputs[0].Namespace);
    }

    [Theory]
    [InlineData("unity: { target: server }")]   // client → server(review の指摘 7 の再現)
    [InlineData("unity: { target: client }")]   // 同じ値でも書けない
    public void Parse_Throws_WhenEnvironmentOverridesTarget(string item)
    {
        // 検証: 環境の上書きに target を書く
        // 期待: target は上書きできないという ConfigException になる
        var yaml = $$"""
            definitions: Definitions
            outputs:
              - { name: unity, target: client, source: out/client, namespace: Sample.MasterData, encrypt: true }
            environments:
              dev:
                outputs:
                  {{item}}
            """;

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, "dev"));
        Assert.Contains("target は環境で上書きできません", ex.Message);
    }

    [Fact]
    public void Parse_Throws_WhenClientHasNoNamespace()
    {
        // 検証: client の出力先で namespace を省く
        // 期待: namespace を含む ConfigException になる
        const string yaml = """
            definitions: Definitions
            outputs:
              - { name: unity, target: client, source: out/client }
            """;

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, null));
        Assert.Contains("namespace", ex.Message);
    }

    [Fact]
    public void Parse_Throws_WhenServerHasNoNamespace()
    {
        // 検証: server の出力先で namespace を省く(MasterDataSchema は server にも出す)
        // 期待: namespace を含む ConfigException になる
        const string yaml = """
            definitions: Definitions
            outputs:
              - { name: api-server, target: server, source: out/server }
            """;

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, null));
        Assert.Contains("namespace", ex.Message);
    }

    [Theory]
    [InlineData("Sample..MasterData")]
    [InlineData("Sample.class")]
    [InlineData("1Sample")]
    [InlineData("Sample-MasterData")]
    public void Parse_Throws_WhenNamespaceIsInvalid(string @namespace)
    {
        // 検証: C# の名前空間として正しくない namespace を書く
        // 期待: ConfigException になる
        var yaml = $"""
            definitions: Definitions
            outputs:
              - name: unity
                target: client
                source: out/client
                namespace: "{@namespace}"
            """;

        Assert.Throws<ConfigException>(() => ConfigLoader.Parse(yaml, BaseDirectory, null));
    }

    [Fact]
    public void Load_ResolvesPathsFromConfigFileDirectory()
    {
        // 検証: ファイルから設定を読む
        // 期待: パスは設定ファイルのあるディレクトリを基準に解決される
        using var temp = new TempDirectory();
        var path = temp.Write("config/masterdata.yaml", """
            definitions: Definitions
            outputs:
              - { name: unity, target: client, source: ../out/client, namespace: Sample.MasterData }
            """);

        var config = ConfigLoader.Load(path, null);

        Assert.Equal(temp.Combine("config/Definitions"), config.Definitions);
        Assert.Equal(temp.Combine("out/client"), config.Outputs[0].Source);
    }
}
