using System.IO;
using System.Linq;
using MasterMemory.Addons.Tools.CodeGen;
using MasterMemory.Addons.Tools.MasterGen;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests.MasterGen;

/// <summary>データ段の検証([Unique] / [Exists] と、MasterMemory の db.Validate())。</summary>
public class DataValidationTests
{
    private const string Definition = """
        using MasterMemory;
        using MasterMemory.Addons;
        using MessagePack;

        namespace Sample.Definitions
        {
            public enum Rarity
            {
                [InputName("なし")] None = 0,
                [InputName("レア")] Rare = 1,
            }

            [MemoryTable("item"), MessagePackObject(true)]
            public class Item
            {
                [PrimaryKey]
                public int ItemId { get; init; }

                [Unique]
                public string Code { get; init; } = "";

                public Rarity Rarity { get; init; }
            }

            // サーバーだけのテーブルから、両方にあるテーブルを参照する
            [ServerOnly, MemoryTable("drop"), MessagePackObject(true)]
            public class Drop
            {
                [PrimaryKey]
                public int DropId { get; init; }

                [Exists(typeof(Item))]
                public int ItemId { get; init; }

                [Exists(typeof(Item), nameof(Item.Code), AllowEmpty = true)]
                public string ItemCode { get; init; } = "";
            }
        }
        """;

    private const string Config = """
        definitions: Definitions
        csv: csv
        outputs:
          - { name: unity, target: client, source: Client/Generated, namespace: Sample.MasterData, binary: Client/masterdata.bytes }
          - { name: api-server, target: server, source: Server/Generated, namespace: Sample.MasterData, binary: Server/masterdata.bytes }
        """;

    private const string Prefix = "comment\ncomment\ntype\n";

    private static TempDirectory CreateProject(string items = "1,a,レア\n2,b,\n", string drops = "1,1,a\n2,2,\n", string definition = Definition)
    {
        var temp = new TempDirectory();
        temp.Write("Definitions/Item.cs", definition);
        temp.Write("masterdata.yaml", Config);
        temp.Write("csv/item.csv", Prefix + "ItemId,Code,Rarity\n" + items);
        temp.Write("csv/drop.csv", Prefix + "DropId,ItemId,ItemCode\n" + drops);
        return temp;
    }

    private static (int ExitCode, string Stdout, string Stderr) MasterGen(TempDirectory temp)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = MasterGenCommand.Run(new[] { "--config", temp.Combine("masterdata.yaml") }, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Succeeds_WhenDataIsValid_AndStripsValidationAttributes()
    {
        // 検証: [Unique] と [Exists] を満たす CSV で master-gen を実行する
        // 期待: 終了コード 0 でバイナリができる。生成ソースには [Unique] / [Exists] が残らない
        using var temp = CreateProject();

        var (code, _, stderr) = MasterGen(temp);

        Assert.True(code == 0, stderr);
        Assert.True(File.Exists(temp.Combine("Server/masterdata.bytes")));
        var server = File.ReadAllText(temp.Combine("Server/Generated/Item.cs"));
        Assert.DoesNotContain("Unique", server);
        Assert.DoesNotContain("Exists", server);
    }

    [Fact]
    public void Returns1_WhenUniqueValueIsDuplicated()
    {
        // 検証: [Unique] の列 Code に、同じ値を2回書く
        // 期待: 終了コード 1。後の行に、最初の行の場所付きのエラーを出し、何も書かない
        using var temp = CreateProject(items: "1,a,レア\n2,b,\n3,a,\n");

        var (code, _, stderr) = MasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("item.csv 7行 列 Code: [Unique] の値 'a' が重複しています(最初は item.csv 5行)。", stderr);
        Assert.False(File.Exists(temp.Combine("Client/masterdata.bytes")));
        Assert.False(File.Exists(temp.Combine("Server/masterdata.bytes")));
    }

    [Theory]
    [InlineData("1,9,\n", "drop.csv 5行 列 ItemId: [Exists] の値 '9' が、item の ItemId にありません。")]
    [InlineData("1,,\n", "drop.csv 5行 列 ItemId: [Exists] の値 (空欄) が、item の ItemId にありません。")]
    [InlineData("1,1,zz\n", "drop.csv 5行 列 ItemCode: [Exists] の値 'zz' が、item の Code にありません。")]
    public void Returns1_WhenReferencedValueDoesNotExist(string drops, string message)
    {
        // 検証: [Exists] の列に、参照先にない値・空欄(AllowEmpty なし)・参照先の列(Code)にない値を書く
        // 期待: 終了コード 1。参照先のテーブルと列が分かるエラー
        using var temp = CreateProject(drops: drops);

        var (code, _, stderr) = MasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains(message, stderr);
    }

    [Fact]
    public void ReportsDataErrorsTogetherWithConversionErrors()
    {
        // 検証: [Unique] の重複と、int に変換できないセルを両方含める
        // 期待: 両方のエラーを1回の実行で表示する
        using var temp = CreateProject(items: "1,a,レア\n2,a,\n", drops: "1,x,\n");

        var (code, _, stderr) = MasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("[Unique] の値 'a' が重複しています", stderr);
        Assert.Contains("drop.csv 5行 列 ItemId: int に変換できません: 'x'", stderr);
    }

    [Fact]
    public void Returns1_WhenMemoryDatabaseValidateFails()
    {
        // 検証: 定義に MasterMemory の IValidatable<T> を実装し、検証に失敗するデータで master-gen を実行する
        // 期待: db.Validate() の結果で終了コード 1 になり、何も書かない
        var definition = Definition
            .Replace("using MessagePack;", "using MessagePack;\n        using MasterMemory.Validation;")
            .Replace("public class Item\n", "public class Item : IValidatable<Item>\n")
            .Replace("public Rarity Rarity { get; init; }", "public Rarity Rarity { get; init; }\n\n        public void Validate(IValidator<Item> validator) => validator.Validate(x => x.Code != \"ng\", \"Code に ng は使えません\");");
        using var temp = CreateProject(items: "1,a,レア\n2,ng,\n", drops: "1,1,\n", definition: definition);

        var (code, _, stderr) = MasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("MasterMemory の db.Validate() で検証に失敗しました", stderr);
        Assert.Contains("Code に ng は使えません", stderr);
        Assert.False(File.Exists(temp.Combine("Client/masterdata.bytes")));
    }
}
