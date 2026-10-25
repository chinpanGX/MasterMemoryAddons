using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MasterMemory.Addons;
using MasterMemory.Addons.Tools.MasterGen;
using MasterMemory.Addons.Tools.Common;
using MasterMemory.Addons.Tools.CodeGen;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests.MasterGen;

public class MasterGenCommandTests
{
    private const string Definition = """
        using MasterMemory;
        using MasterMemory.Addons;
        using MessagePack;

        namespace Sample.Definitions
        {
            public enum ItemCategory
            {
                [InputName("武器")] Weapon = 1,
                [InputName("防具")] Armor = 2,
            }

            [MemoryTable("item"), MessagePackObject(true)]
            public class Item
            {
                [PrimaryKey]
                public int ItemId { get; init; }

                public ItemCategory Category { get; init; }

                public string Name { get; init; } = "";

                [ClientOnly]
                public string IconAddress { get; init; } = "";

                [ServerOnly]
                public int SellPrice { get; init; }
            }

            [ServerOnly, MemoryTable("dropTable"), MessagePackObject(true)]
            public class DropTable
            {
                [PrimaryKey]
                public int DropTableId { get; init; }

                public int Weight { get; init; }
            }
        }
        """;

    private const string Config = """
        definitions: Definitions
        csv: csv
        outputs:
          - { name: unity, target: client, source: Client/Generated, namespace: Sample.MasterData, binary: Client/masterdata.bytes, encrypt: true }
          - { name: api-server, target: server, source: Server/Generated, namespace: Sample.MasterData, binary: Server/masterdata.bytes }
          - { name: battle-server, target: server, source: Battle/Generated, namespace: Sample.MasterData }
        """;

    private const string Prefix = "comment\ncomment\ntype\n";

    private static TempDirectory CreateProject()
    {
        var temp = new TempDirectory();
        temp.Write("Definitions/Item.cs", Definition);
        temp.Write("masterdata.yaml", Config);
        temp.Write("csv/item_weapon.csv", Prefix + "ItemId,Category,Name,IconAddress,SellPrice\n1,武器,銅の剣,Icons/sword,10\n2,武器,鉄の剣,Icons/iron,50\n");
        temp.Write("csv/item_armor.csv", Prefix + "ItemId,Category,Name,IconAddress,SellPrice\n101,防具,\"布の服, 軽い\",Icons/cloth,5\n102,防具,革の鎧,,\n");
        temp.Write("csv/dropTable.csv", Prefix + "DropTableId,Weight\n1,70\n");
        return temp;
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(TempDirectory temp, string command, params string[] options)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var args = new[] { "--config", temp.Combine("masterdata.yaml") }.Concat(options).ToArray();
        var code = command == "code-gen"
            ? CodeGenCommand.Run(args, stdout, stderr)
            : MasterGenCommand.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static (int ExitCode, string Stdout, string Stderr) CodeGenAndMasterGen(TempDirectory temp)
    {
        var codeGen = Run(temp, "code-gen");
        Assert.True(codeGen.ExitCode == 0, codeGen.Stderr);
        return Run(temp, "master-gen");
    }

    // バイナリを MasterDataBinary.Read で読み(ヘッダの確認と復号)、生成ソースをコンパイルした型の MemoryDatabase で読み直して、
    // テーブル名 → 行の一覧にする
    private static Dictionary<string, List<object>> Load(string sourceDirectory, GenerationTarget target, byte[] binary)
    {
        var bytes = MasterDataBinary.Read(binary, ReadSchemaHash(sourceDirectory));
        // 出力先に書かれた生成ソースを、マスタ生成と同じ入口(GeneratedFile の一覧)でコンパイルする
        var sources = Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(f => new GeneratedFile(Path.GetRelativePath(sourceDirectory, f), File.ReadAllText(f)))
            .ToList();
        var compiled = InMemoryCompiler.Compile(sources, target);
        Assert.True(compiled.Assembly is not null, string.Join(Environment.NewLine, compiled.Errors));
        var databaseType = compiled.Assembly!.GetTypes().Single(t => t.Name == "MemoryDatabase");
        var database = Activator.CreateInstance(databaseType, bytes, true, null, 1)!;

        var result = new Dictionary<string, List<object>>();
        foreach (var property in databaseType.GetProperties().Where(p => p.Name.EndsWith("Table", StringComparison.Ordinal)))
        {
            var table = property.GetValue(database)!;
            var all = (IEnumerable)table.GetType().GetProperty("All")!.GetValue(table)!;
            result[property.Name] = all.Cast<object>().ToList();
        }

        return result;
    }

    private static object? Value(object row, string property)
    {
        return row.GetType().GetProperty(property)?.GetValue(row);
    }

    private static string ReadSchemaHash(string sourceDirectory)
    {
        var source = File.ReadAllText(Path.Combine(sourceDirectory, MasterDataSchemaGenerator.FileName));
        return Regex.Match(source, "Hash = \"(?<hash>[0-9a-f]+)\"").Groups["hash"].Value;
    }

    [Fact]
    public void MasterGen_WritesClientAndServerBinaries()
    {
        // 検証: client(暗号化あり)と server の出力先に binary を書き、code-gen → master-gen を実行する
        // 期待: client のバイナリは MasterDataSchema.Hash で読め(ヘッダの確認と復号)、水平分割した CSV の 4 行が入り、サーバー専用の列・テーブルは入らない。
        //       server のバイナリは暗号化されず、ヘッダが付き、サーバー専用の列・テーブルが入る。binary のない出力先は対象にならない
        using var temp = CreateProject();

        var (code, stdout, stderr) = CodeGenAndMasterGen(temp);

        Assert.True(code == 0, stderr);
        Assert.Contains("battle-server: binary がないため", stdout);

        var encrypted = File.ReadAllBytes(temp.Combine("Client/masterdata.bytes"));
        Assert.Equal("MMAD", System.Text.Encoding.ASCII.GetString(encrypted, 0, 4));
        Assert.Equal(1, BitConverter.ToUInt16(encrypted, 6));
        var client = Load(temp.Combine("Client/Generated"), GenerationTarget.Client, encrypted);
        var items = client["ItemTable"];
        Assert.Equal(new object[] { 1, 2, 101, 102 }, items.Select(r => Value(r, "ItemId")));
        Assert.Equal("布の服, 軽い", Value(items[2], "Name"));
        Assert.Equal("", Value(items[3], "IconAddress"));
        Assert.Equal("Armor", Value(items[3], "Category")!.ToString());
        Assert.Null(items[0].GetType().GetProperty("SellPrice"));
        Assert.DoesNotContain("DropTableTable", client.Keys);

        var server = Load(temp.Combine("Server/Generated"), GenerationTarget.Server, File.ReadAllBytes(temp.Combine("Server/masterdata.bytes")));
        Assert.Equal(new object[] { 10, 50, 5, 0 }, server["ItemTable"].Select(r => Value(r, "SellPrice")));
        Assert.Single(server["DropTableTable"]);
        Assert.Null(server["ItemTable"][0].GetType().GetProperty("IconAddress"));

        Assert.False(File.Exists(temp.Combine("Battle/masterdata.bytes")));
    }

    [Fact]
    public void MasterGen_WritesManifestNextToEachBinary()
    {
        // 検証: client(暗号化あり)と server の出力先に binary を書いて master-gen を実行する(D4)
        // 期待: バイナリの隣に masterdata.manifest.json ができる。schemaId は各出力先の MasterDataSchema.Hash から計算した ID
        //       (鍵のハッシュそのものは書かない)、
        //       dataHash は暗号化する前の中身の SHA-256、size はバイナリの長さ。作った日時はどの出力先も同じ
        using var temp = CreateProject();

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.True(code == 0, stderr);
        var created = new List<string>();
        foreach (var (name, encrypted) in new[] { ("Client", true), ("Server", false) })
        {
            var binary = File.ReadAllBytes(temp.Combine($"{name}/masterdata.bytes"));
            var schemaHash = ReadSchemaHash(temp.Combine($"{name}/Generated"));
            using var json = JsonDocument.Parse(File.ReadAllText(temp.Combine($"{name}/masterdata.manifest.json")));
            var root = json.RootElement;

            Assert.Equal(2, root.GetProperty("formatVersion").GetInt32());
            Assert.Equal(MasterDataBinary.ComputeSchemaId(schemaHash), root.GetProperty("schemaId").GetString());
            Assert.False(root.TryGetProperty("schemaHash", out _));
            Assert.DoesNotContain(schemaHash, root.GetRawText());
            var data = MasterDataBinary.Read(binary, schemaHash);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), root.GetProperty("dataHash").GetString());
            Assert.Equal(encrypted, root.GetProperty("encrypted").GetBoolean());
            Assert.Equal(binary.Length, root.GetProperty("size").GetInt64());
            var createdAt = root.GetProperty("createdAt").GetString()!;
            Assert.Matches(new Regex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z$"), createdAt);
            created.Add(createdAt);
        }

        Assert.Single(created.Distinct());
    }

    [Fact]
    public void MasterGen_Returns2_WhenManifestsOverlap()
    {
        // 検証: 同じディレクトリに、拡張子だけが違う binary(Out/data.bytes と Out/data.dat)を2つの出力先に書く
        // 期待: 終了コード 2。マニフェスト(Out/data.manifest.json)が重なるため
        using var temp = CreateProject();
        temp.Write("masterdata.yaml", Config
            .Replace("binary: Client/masterdata.bytes", "binary: Out/data.bytes")
            .Replace("binary: Server/masterdata.bytes", "binary: Out/data.dat"));

        var (code, _, stderr) = Run(temp, "master-gen");

        Assert.Equal(2, code);
        Assert.Contains("マニフェスト", stderr);
    }

    [Fact]
    public void MasterGen_ServerBinary_CannotBeReadWithClientSchema()
    {
        // 検証: server のバイナリを、client の MasterDataSchema.Hash で読む
        // 期待: スキーマが違うので SchemaMismatch になる(client と server でスキーマのハッシュは違う)
        using var temp = CreateProject();
        Assert.Equal(0, CodeGenAndMasterGen(temp).ExitCode);

        var server = File.ReadAllBytes(temp.Combine("Server/masterdata.bytes"));
        var ex = Assert.Throws<MasterDataFormatException>(() => MasterDataBinary.Read(server, ReadSchemaHash(temp.Combine("Client/Generated"))));
        Assert.Equal(MasterDataFormatErrorReason.SchemaMismatch, ex.Reason);
    }

    [Fact]
    public void MasterGen_Succeeds_WhenDefinitionHasTypeNamedMasterDataSchema()
    {
        // 検証: 定義に MasterDataSchema という名前の型を(別のファイル名で)置く(review の指摘 4 の再現。以前は同名の型で例外になった)
        // 期待: 終了コード 0。生成した MasterDataSchema を型の名前で探さないので、取り違えも例外も起きない
        using var temp = CreateProject();
        temp.Write("Definitions/Other.cs", "namespace Sample.Definitions { public static class MasterDataSchema { public const int Value = 1; } }");

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.True(code == 0, stderr);
        Assert.Equal(4, Load(temp.Combine("Client/Generated"), GenerationTarget.Client, File.ReadAllBytes(temp.Combine("Client/masterdata.bytes")))["ItemTable"].Count);
    }

    [Fact]
    public void MasterGen_Succeeds_WhenDefinitionHasTypeNamedDatabaseBuilder()
    {
        // 検証: 定義に DatabaseBuilder という名前の型を置く(review の E3 の再現。以前は同名の型が2つになり、バイナリを作れなかった)
        // 期待: 終了コード 0。生成した DatabaseBuilder を名前空間まで含めて探すので、取り違えない
        using var temp = CreateProject();
        temp.Write("Definitions/Other.cs", "namespace Sample.Definitions { public class DatabaseBuilder { } }");

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.True(code == 0, stderr);
        Assert.Equal(4, Load(temp.Combine("Client/Generated"), GenerationTarget.Client, File.ReadAllBytes(temp.Combine("Client/masterdata.bytes")))["ItemTable"].Count);
    }

    [Fact]
    public void MasterGen_Returns1_AndKeepsAllOldFiles_WhenTemporaryFileCannotBeWritten()
    {
        // 検証: 一度バイナリを作った後、server のマニフェストの一時ファイルの場所にディレクトリを置いて、もう一度 master-gen を実行する(review の E4・E5)
        // 期待: 終了コード 1。先に書いた client の一時ファイルも消え、client・server のバイナリとマニフェストは元のまま
        using var temp = CreateProject();
        Assert.Equal(0, CodeGenAndMasterGen(temp).ExitCode);
        var paths = new[] { "Client/masterdata.bytes", "Client/masterdata.manifest.json", "Server/masterdata.bytes", "Server/masterdata.manifest.json" };
        var before = paths.Select(p => File.ReadAllBytes(temp.Combine(p))).ToList();
        Directory.CreateDirectory(temp.Combine("Server/masterdata.manifest.json.tmp"));

        var (code, _, stderr) = Run(temp, "master-gen");

        Assert.Equal(1, code);
        Assert.Contains("書き込めません", stderr);
        Assert.Contains("どの出力先のバイナリもマニフェストも書き換えていません", stderr);
        Assert.Equal(before, paths.Select(p => File.ReadAllBytes(temp.Combine(p))).ToList());
        Assert.Empty(Directory.GetFiles(temp.Combine("Client"), "*.tmp"));
        Assert.Empty(Directory.GetFiles(temp.Combine("Server"), "*.tmp"));
    }

    [Fact]
    public void MasterGen_Returns1_AndReportsReplacedFiles_WhenReplaceFails()
    {
        // 検証: server のマニフェストの場所にディレクトリを置いて master-gen を実行する(置き換えの段階で失敗する)
        // 期待: 終了コード 1。新しくなったファイル(client のバイナリとマニフェスト、server のバイナリ)を表示し、残りの一時ファイルは消える
        using var temp = CreateProject();
        Assert.Equal(0, Run(temp, "code-gen").ExitCode);
        Directory.CreateDirectory(temp.Combine("Server/masterdata.manifest.json"));

        var (code, _, stderr) = Run(temp, "master-gen");

        Assert.Equal(1, code);
        Assert.Contains("置き換えられません", stderr);
        Assert.Contains("次のファイルだけが新しくなっています", stderr);
        Assert.True(File.Exists(temp.Combine("Client/masterdata.manifest.json")));
        Assert.Empty(Directory.GetFiles(temp.Combine("Server"), "*.tmp"));
    }

    [Fact]
    public void Check_Returns1_WhenSourceHasFileThatRegenerationWouldDelete()
    {
        // 検証: code-gen の後に、生成しなくなったファイルの .meta と手で置いたファイルを source に置き、master-gen --check を実行する(review の E6)
        // 期待: 生成ソースが古いとみなし、終了コード 1。何も消さない。生成するファイルの .meta だけなら古いとみなさない
        using var temp = CreateProject();
        Assert.Equal(0, Run(temp, "code-gen").ExitCode);
        temp.Write("Client/Generated/Item.cs.meta", "guid: item");
        Assert.Equal(0, Run(temp, "master-gen", "--check").ExitCode);
        var orphan = temp.Write("Client/Generated/Removed.cs.meta", "guid: removed");
        var handwritten = temp.Write("Client/Generated/Memo.txt", "memo");

        var (code, _, stderr) = Run(temp, "master-gen", "--check");

        Assert.Equal(1, code);
        Assert.Contains("生成ソースが定義と合っていない出力先があります: unity", stderr);
        Assert.True(File.Exists(orphan));
        Assert.True(File.Exists(handwritten));
    }

    [Fact]
    public void MasterGen_RegeneratesSources_WhenSourceHasFileThatRegenerationWouldDelete()
    {
        // 検証: code-gen の後に、生成しなくなったファイルの .meta を source に置き、master-gen を実行する(review の E6)
        // 期待: 生成ソースを作り直し、その .meta を消す(コード生成と同じ結果になる)
        using var temp = CreateProject();
        Assert.Equal(0, Run(temp, "code-gen").ExitCode);
        var orphan = temp.Write("Client/Generated/Removed.cs.meta", "guid: removed");

        var (code, stdout, stderr) = Run(temp, "master-gen");

        Assert.True(code == 0, stderr);
        Assert.Contains("unity: 生成ソースが定義と合っていないため", stdout);
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public void MasterGen_Returns1_AndWritesNothing_WhenTableTypeIsRecord()
    {
        // 検証: テーブルの型を record にした定義で、code-gen を実行せずに master-gen を実行する
        // 期待: 終了コード 1。定義のエラーとして表示し、生成ソースもバイナリも書かない
        using var temp = CreateProject();
        temp.Write("Definitions/Item.cs", Definition.Replace("public class Item", "public record Item"));

        var (code, _, stderr) = Run(temp, "master-gen");

        Assert.Equal(1, code);
        Assert.Contains("テーブルの型に record は使えません", stderr);
        Assert.Contains("定義にエラーがあるため", stderr);
        Assert.False(Directory.Exists(temp.Combine("Client/Generated")));
        Assert.False(File.Exists(temp.Combine("Client/masterdata.bytes")));
    }

    [Fact]
    public void MasterGen_WritesEachServerOutput_WhenSourcesAreSame()
    {
        // 検証: 同じ target(server)の2つの出力先に binary を書く(生成ソースの中身が同じなので、コンパイルは1回だけになる)
        // 期待: 両方の出力先に、同じ内容のバイナリが書かれ、それぞれの生成ソースの型で読める
        using var temp = CreateProject();
        temp.Write("masterdata.yaml", Config.Replace("source: Battle/Generated, namespace: Sample.MasterData }", "source: Battle/Generated, namespace: Sample.MasterData, binary: Battle/masterdata.bytes }"));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.True(code == 0, stderr);
        var api = File.ReadAllBytes(temp.Combine("Server/masterdata.bytes"));
        var battle = File.ReadAllBytes(temp.Combine("Battle/masterdata.bytes"));
        Assert.Equal(api, battle);
        Assert.Equal(4, Load(temp.Combine("Battle/Generated"), GenerationTarget.Server, battle)["ItemTable"].Count);
    }

    [Fact]
    public void MasterGen_ReportsCellErrorWithLocation_AndWritesNothing()
    {
        // 検証: CSV に、int に変換できない値と、入力名にない enum の値を書く
        // 期待: 終了コード 1。エラーにファイル・行・列が入り、どの出力先にもバイナリを書かない
        using var temp = CreateProject();
        temp.Write("csv/item_weapon.csv", Prefix + "ItemId,Category,Name,IconAddress,SellPrice\n1,剣,銅の剣,,abc\n");

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("item_weapon.csv 5行 列 Category: '剣' は ItemCategory の入力名にありません", stderr);
        Assert.Contains("item_weapon.csv 5行 列 SellPrice: int に変換できません", stderr);
        Assert.False(File.Exists(temp.Combine("Client/masterdata.bytes")));
        Assert.False(File.Exists(temp.Combine("Server/masterdata.bytes")));
    }

    [Fact]
    public void MasterGen_ReportsHeaderMismatch()
    {
        // 検証: ヘッダから列(SellPrice)を抜き、定義にない列(Price)を足す
        // 期待: 終了コード 1。足りない列と、定義にない列のエラーになる
        using var temp = CreateProject();
        temp.Write("csv/item_weapon.csv", Prefix + "ItemId,Category,Name,IconAddress,Price\n1,武器,銅の剣,,10\n");
        temp.Write("csv/item_armor.csv", Prefix + "ItemId,Category,Name,IconAddress,Price\n101,防具,布の服,,5\n");

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("列 Price: Item にない列です", stderr);
        Assert.Contains("列 SellPrice: Item.SellPrice の列がヘッダにありません", stderr);
    }

    [Fact]
    public void MasterGen_ReportsHeaderMismatch_WithActualLine()
    {
        // 検証: 1行目のコメントに引用符で囲んだ改行があり、ヘッダに定義にない列(Price)がある(review の指摘 6 の再現)
        // 期待: エラーの行の番号は、ファイルの実際のヘッダの行(5行目)になる
        using var temp = CreateProject();
        var header = "\"comment\nnext\"\ncomment\ntype\nItemId,Category,Name,IconAddress,Price\n";
        temp.Write("csv/item_armor.csv", header + "101,防具,布の服,,5\n");
        temp.Write("csv/item_weapon.csv", header + "1,武器,銅の剣,,10\n");

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("item_armor.csv 5行 列 Price: Item にない列です", stderr);
    }

    [Fact]
    public void MasterGen_ReportsEnumMemberWithoutInputName()
    {
        // 検証: CSV の列で使う enum に、[InputName] のないメンバーを足す
        // 期待: 終了コード 1。[InputName] がないメンバーのエラーになる
        using var temp = CreateProject();
        temp.Write("Definitions/Item.cs", Definition.Replace("[InputName(\"防具\")] Armor = 2,", "[InputName(\"防具\")] Armor = 2,\n        Shield = 3,"));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("ItemCategory の Shield に [InputName] がありません", stderr);
    }

    [Fact]
    public void MasterGen_ConvertsEnumNestedInTableType()
    {
        // 検証: テーブルの型の中に入れ子にした enum を列に使う(review の指摘 2 の再現)
        // 期待: 終了コード 0。入力名から enum の値に変換される
        using var temp = CreateProject();
        temp.Write("Definitions/Item.cs", Definition
            .Replace("public ItemCategory Category", "public Kind Category")
            .Replace("[ClientOnly]", "public enum Kind { [InputName(\"武器\")] Weapon = 1, [InputName(\"防具\")] Armor = 2 }\n\n        [ClientOnly]"));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.True(code == 0, stderr);
        var server = Load(temp.Combine("Server/Generated"), GenerationTarget.Server, File.ReadAllBytes(temp.Combine("Server/masterdata.bytes")));
        var category = Value(server["ItemTable"][2], "Category")!;
        Assert.Equal("Sample.Definitions.Item+Kind", category.GetType().FullName);
        Assert.Equal("Armor", category.ToString());
    }

    [Fact]
    public void MasterGen_ReportsMissingInputName_OfEnumNestedInTableType()
    {
        // 検証: 同じ名前の enum が、テーブルの型の中と名前空間の直下にあり、テーブルの型の中の enum に [InputName] のないメンバーがある
        // 期待: 終了コード 1。列が使う、テーブルの型の中の enum のエラーになる(名前空間の直下の enum と取り違えない)
        using var temp = CreateProject();
        temp.Write("Definitions/Item.cs", Definition
            .Replace("public ItemCategory Category", "public Kind Category")
            .Replace("public enum ItemCategory", "public enum Kind")
            .Replace("[ClientOnly]", "public enum Kind { [InputName(\"武器\")] Weapon = 1, [InputName(\"防具\")] Armor = 2, Shield = 3 }\n\n        [ClientOnly]"));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("Kind の Shield に [InputName] がありません", stderr);
    }

    [Fact]
    public void MasterGen_ReportsDuplicatePrimaryKey()
    {
        // 検証: 水平分割した2つのファイルに、同じ主キーの行を書く
        // 期待: 終了コード 1。どの出力先にもバイナリを書かない
        using var temp = CreateProject();
        temp.Write("csv/item_armor.csv", Prefix + "ItemId,Category,Name,IconAddress,SellPrice\n1,防具,重複,,5\n");

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("主キー", stderr);
        Assert.False(File.Exists(temp.Combine("Client/masterdata.bytes")));
    }

    [Fact]
    public void MasterGen_Returns2_WhenBinaryIsInsideSource()
    {
        // 検証: binary を、コード生成が丸ごと入れ替える source の中に置く
        // 期待: 終了コード 2(コード生成で消えるため)
        using var temp = CreateProject();
        temp.Write("masterdata.yaml", Config.Replace("binary: Client/masterdata.bytes", "binary: Client/Generated/masterdata.bytes"));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(2, code);
        Assert.Contains("コード生成で消えます", stderr);
    }

    [Fact]
    public void MasterGen_ReportsClientCompileError_WhenDefinitionUsesApiOutsideNetStandard()
    {
        // 検証: 定義に netstandard2.1 にない API(System.Text.Json)を使う(review の指摘 8 の再現)
        // 期待: 終了コード 1。client(unity)の生成ソースのコンパイルエラーになる(Unity で通らない定義を通さない)
        using var temp = CreateProject();
        temp.Write("Definitions/Item.cs", Definition.Replace(
            "public string Name { get; init; } = \"\";",
            "public string Name { get; init; } = \"\";\n\n        public static string Json() => System.Text.Json.JsonSerializer.Serialize(1);"));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("unity: 生成ソース", stderr);
        Assert.DoesNotContain("api-server: 生成ソース", stderr);
    }

    [Fact]
    public void MasterGen_ReportsServerCompileError_WhenDefinitionUsesToolsDependency()
    {
        // 検証: サーバー専用の列の型に、Tools の依存(YamlDotNet)の型を使う
        // 期待: 終了コード 1。server の生成ソースもコンパイルエラーになる(Tools の依存は参照しない)
        using var temp = CreateProject();
        temp.Write("Definitions/Item.cs", Definition.Replace(
            "public int Weight { get; init; }",
            "public int Weight { get; init; }\n\n        public static object Yaml() => new YamlDotNet.Serialization.Deserializer();"));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("api-server: 生成ソース", stderr);
    }

    [Fact]
    public void MasterGen_GeneratesSources_WhenCodeGenHasNotRun()
    {
        // 検証: code-gen を実行せずに master-gen を実行する(D1)
        // 期待: 終了コード 0。先にコード生成をやり直して生成ソースを置き、バイナリを作る
        using var temp = CreateProject();

        var (code, stdout, stderr) = Run(temp, "master-gen");

        Assert.True(code == 0, stderr);
        Assert.Contains("unity: 生成ソースが定義と合っていないため、コード生成をやり直しました", stdout);
        Assert.True(File.Exists(temp.Combine("Client/Generated/Item.cs")));
        Assert.True(File.Exists(temp.Combine("Battle/Generated/Item.cs")));
        Assert.True(File.Exists(temp.Combine("Server/masterdata.bytes")));
    }

    [Fact]
    public void MasterGen_RegeneratesStaleSources_AfterDefinitionChanged()
    {
        // 検証: code-gen の後に定義へ列(Rarity)を足し、CSV にもその列を足して、code-gen をせずに master-gen を実行する(D1 の再現)
        // 期待: 生成ソースが作り直され、バイナリに新しい列の値が入る
        using var temp = CreateProject();
        Assert.Equal(0, Run(temp, "code-gen").ExitCode);
        temp.Write("Definitions/Item.cs", Definition.Replace(
            "public string Name { get; init; } = \"\";",
            "public string Name { get; init; } = \"\";\n\n        public int Rarity { get; init; }"));
        temp.Write("csv/item_weapon.csv", Prefix + "ItemId,Category,Name,IconAddress,SellPrice,Rarity\n1,武器,銅の剣,,10,3\n");
        temp.Write("csv/item_armor.csv", Prefix + "ItemId,Category,Name,IconAddress,SellPrice,Rarity\n101,防具,布の服,,5,1\n");

        var (code, stdout, stderr) = Run(temp, "master-gen");

        Assert.True(code == 0, stderr);
        Assert.Contains("api-server: 生成ソースが定義と合っていないため", stdout);
        Assert.Contains("Rarity", File.ReadAllText(temp.Combine("Server/Generated/Item.cs")));
        var server = Load(temp.Combine("Server/Generated"), GenerationTarget.Server, File.ReadAllBytes(temp.Combine("Server/masterdata.bytes")));
        Assert.Equal(3, Value(server["ItemTable"][0], "Rarity"));
    }

    [Fact]
    public void MasterGen_RestoresHandEditedSource()
    {
        // 検証: code-gen の後に、Unity の .meta を置き、生成ソースを手で書き換えてから master-gen を実行する
        // 期待: 生成ソースは定義から作り直した内容に戻る。作り直したファイルの .meta は残り、生成しないファイルの .meta は消える(review の R3)
        using var temp = CreateProject();
        Assert.Equal(0, Run(temp, "code-gen").ExitCode);
        var meta = temp.Write("Client/Generated/Item.cs.meta", "guid: item");
        var orphan = temp.Write("Client/Generated/Removed.cs.meta", "guid: removed");
        var path = temp.Combine("Client/Generated/Item.cs");
        var original = File.ReadAllText(path);
        File.WriteAllText(path, original + "\n// 手で書き換えた\n");

        var (code, stdout, stderr) = Run(temp, "master-gen");

        Assert.True(code == 0, stderr);
        Assert.Contains("unity: 生成ソースが定義と合っていないため", stdout);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal("guid: item", File.ReadAllText(meta));
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public void MasterGen_KeepsSources_WhenUpToDate()
    {
        // 検証: code-gen の後に、Unity の .meta を置き、生成ソースの改行を CRLF に変えてから master-gen を実行する
        // 期待: 生成ソースは古いとみなさず、書き直さない(.meta も残る)
        using var temp = CreateProject();
        Assert.Equal(0, Run(temp, "code-gen").ExitCode);
        var meta = temp.Write("Client/Generated/Item.cs.meta", "meta");
        var path = temp.Combine("Client/Generated/Item.cs");
        // 生成ソースには CRLF と LF が混ざっているので、いったん LF にそろえてから CRLF にする
        File.WriteAllText(path, File.ReadAllText(path).Replace("\r\n", "\n").Replace("\n", "\r\n"));

        var (code, stdout, stderr) = Run(temp, "master-gen");

        Assert.True(code == 0, stderr);
        Assert.DoesNotContain("コード生成をやり直しました", stdout);
        Assert.True(File.Exists(meta));
    }

    [Fact]
    public void Check_Returns1_AndWritesNothing_WhenSourcesAreStale()
    {
        // 検証: code-gen を実行せずに master-gen --check を実行する(D6、review の R2)
        // 期待: 終了コード 1。CSV の検証は通るが、生成ソースが古いことを出力先ごとに表示する。生成ソースもバイナリも書かない
        using var temp = CreateProject();

        var (code, stdout, stderr) = Run(temp, "master-gen", "--check");

        Assert.Equal(1, code);
        Assert.Contains("警告: unity: 生成ソース", stderr);
        Assert.Contains("CSV にエラーはありませんが、生成ソースが定義と合っていない出力先があります: unity", stderr);
        Assert.DoesNotContain("検証のみ(--check): エラーはありません", stdout);
        Assert.False(Directory.Exists(temp.Combine("Client/Generated")));
        Assert.False(File.Exists(temp.Combine("Client/masterdata.bytes")));
        Assert.False(File.Exists(temp.Combine("Server/masterdata.bytes")));
        Assert.False(File.Exists(temp.Combine("Server/masterdata.manifest.json")));
    }

    [Fact]
    public void Check_Returns0_WhenSourcesAreUpToDate()
    {
        // 検証: code-gen の後に master-gen --check を実行する
        // 期待: 終了コード 0。警告は出さず、バイナリも書かない
        using var temp = CreateProject();
        Assert.Equal(0, Run(temp, "code-gen").ExitCode);

        var (code, stdout, stderr) = Run(temp, "master-gen", "--check");

        Assert.True(code == 0, stderr);
        Assert.DoesNotContain("警告", stderr);
        Assert.Contains("検証のみ(--check): エラーはありません", stdout);
        Assert.False(File.Exists(temp.Combine("Client/masterdata.bytes")));
    }

    [Fact]
    public void Check_Returns1_OnCsvError_AndWritesNothing()
    {
        // 検証: CSV に変換できない値があり、master-gen --check を実行する
        // 期待: 終了コード 1。master-gen と同じエラーになり、何も書かない
        using var temp = CreateProject();
        Assert.Equal(0, Run(temp, "code-gen").ExitCode);
        temp.Write("csv/item_weapon.csv", Prefix + "ItemId,Category,Name,IconAddress,SellPrice\n1,武器,銅の剣,,abc\n");

        var (code, _, stderr) = Run(temp, "master-gen", "--check");

        Assert.Equal(1, code);
        Assert.Contains("item_weapon.csv 5行 列 SellPrice: int に変換できません", stderr);
        Assert.False(File.Exists(temp.Combine("Server/masterdata.bytes")));
    }

    [Fact]
    public void MasterGen_Returns2_WhenSourceOverlapsCsv()
    {
        // 検証: 出力先の source に、csv のディレクトリを含むディレクトリを指定して master-gen を実行する
        // 期待: 終了コード 2。生成ソースを書き直さない(CSV は消えない)
        using var temp = CreateProject();
        temp.Write("masterdata.yaml", Config.Replace("source: Battle/Generated", "source: ."));

        var (code, _, _) = Run(temp, "master-gen");

        Assert.Equal(2, code);
        Assert.True(File.Exists(temp.Combine("csv/dropTable.csv")));
    }

    [Fact]
    public void MasterGen_Returns2_WithoutCsv()
    {
        // 検証: csv を書かない設定で build を実行する
        // 期待: 終了コード 2
        using var temp = CreateProject();
        temp.Write("masterdata.yaml", Config.Replace("csv: csv\n", ""));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(2, code);
        Assert.Contains("csv", stderr);
    }

    [Fact]
    public void MasterGen_WarnsUnmatchedCsv()
    {
        // 検証: どのテーブルにも一致しない CSV を置く
        // 期待: 成功し、その CSV は読まないという警告が出る
        using var temp = CreateProject();
        temp.Write("csv/unknown.csv", Prefix + "A\n1\n");

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(0, code);
        Assert.Contains("警告: unknown.csv はどのテーブルにも一致しない", stderr);
    }

    [Fact]
    public void MasterGen_ReadsCsvInSpreadsheetFolders()
    {
        // 検証: SheetsDownloader の出力のように、スプレッドシートごとのフォルダ(items/、drops/)に CSV を置く
        // 期待: 成功し、フォルダの中の CSV がそれぞれのテーブルとして読まれる
        using var temp = CreateProject();
        Directory.CreateDirectory(temp.Combine("csv/items"));
        Directory.CreateDirectory(temp.Combine("csv/drops"));
        File.Move(temp.Combine("csv/item_weapon.csv"), temp.Combine("csv/items/item_weapon.csv"));
        File.Move(temp.Combine("csv/item_armor.csv"), temp.Combine("csv/items/item_armor.csv"));
        File.Move(temp.Combine("csv/dropTable.csv"), temp.Combine("csv/drops/dropTable.csv"));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.True(code == 0, stderr);
        var server = Load(temp.Combine("Server/Generated"), GenerationTarget.Server, File.ReadAllBytes(temp.Combine("Server/masterdata.bytes")));
        Assert.Equal(4, server["ItemTable"].Count);
        Assert.Single(server["DropTableTable"]);
    }

    [Fact]
    public void MasterGen_ReportsError_WhenTableIsInMultipleFolders()
    {
        // 検証: item の CSV を、2つのスプレッドシートのフォルダ(a/、b/)に分けて置く
        // 期待: 終了コード 1。複数のフォルダにあるエラーになり、どの出力先にもバイナリを書かない
        using var temp = CreateProject();
        Directory.CreateDirectory(temp.Combine("csv/a"));
        Directory.CreateDirectory(temp.Combine("csv/b"));
        File.Move(temp.Combine("csv/item_weapon.csv"), temp.Combine("csv/a/item_weapon.csv"));
        File.Move(temp.Combine("csv/item_armor.csv"), temp.Combine("csv/b/item_armor.csv"));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("複数のフォルダ", stderr);
        Assert.False(File.Exists(temp.Combine("Client/masterdata.bytes")));
    }

    [Fact]
    public void MasterGen_ReportsError_WhenTableNameContainsSeparator()
    {
        // 検証: テーブル名に _ を使った定義(drop_table)で build を実行する
        // 期待: 終了コード 1。_ はテーブル名に使えないエラーになる
        using var temp = CreateProject();
        temp.Write("Definitions/Item.cs", Definition.Replace("MemoryTable(\"dropTable\")", "MemoryTable(\"drop_table\")"));

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.Equal(1, code);
        Assert.Contains("テーブル名 drop_table に '_' は使えません", stderr);
    }

    [Fact]
    public void MasterGen_AcceptsTrailingEmptyHeaderColumns()
    {
        // 検証: コメントがヘッダより右の列まであり、全行が空のセルで埋められた CSV を置く(docs/review.md の指摘 3)
        // 期待: 成功する
        using var temp = CreateProject();
        temp.Write("csv/dropTable.csv", "comment,,,\ncomment,,,\ntype,,,\nDropTableId,Weight,,\n1,70,,\n");

        var (code, _, stderr) = CodeGenAndMasterGen(temp);

        Assert.True(code == 0, stderr);
    }
}
