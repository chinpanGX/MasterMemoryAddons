using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MasterMemory.Addons.Tools.CodeGen;
using MasterMemory.Addons.Tools.Common;

namespace MasterMemory.Addons.Tools.MasterGen;

/// <summary>master-gen コマンド(マスタ生成)。CSV を検証し、バイナリを作る。仕様は docs/design/master-gen.md。</summary>
public static class MasterGenCommand
{
    private const string Usage = """
        MasterMemory.Addons.Tools master-gen

        CSV を検証し、MasterMemory のバイナリを作ります(マスタ生成)。
        設定ファイルで binary を書いた出力先ごとに、生成ソースをコンパイルし、CSV の行を追加してバイナリを作ります。
        出力先の生成ソース(source)が定義と合っていなければ、先にコード生成をやり直します。

        使い方:
          MasterMemory.Addons.Tools master-gen --config <file> [--env <name>] [--check]

        オプション:
          -c, --config   設定ファイル(YAML)
          -e, --env      設定ファイルの environments から、上書きに使う環境の名前
              --check    検証だけを行い、何も書き込まない(生成ソースも、バイナリも)。
                         生成ソースが定義と合っていなければ、検証が通っても終了コード 1
          -h, --help     このヘルプを表示
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? config = null;
        string? environment = null;
        var check = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    stdout.WriteLine(Usage);
                    return 0;
                case "-c":
                case "--config":
                    if (!TryNext(args, ref i, out config)) return MissingValue(arg, stderr);
                    break;
                case "-e":
                case "--env":
                    if (!TryNext(args, ref i, out environment)) return MissingValue(arg, stderr);
                    break;
                case "--check":
                    check = true;
                    break;
                default:
                    stderr.WriteLine($"不明なオプションです: {arg}");
                    stderr.WriteLine(Usage);
                    return 2;
            }
        }

        if (config is null)
        {
            stderr.WriteLine("--config は必須です。");
            stderr.WriteLine(Usage);
            return 2;
        }

        return RunWithConfig(config, environment, check, stdout, stderr);
    }

    private static int RunWithConfig(string configPath, string? environment, bool check, TextWriter stdout, TextWriter stderr)
    {
        ToolsConfig config;
        try
        {
            config = ConfigLoader.Load(configPath, environment);
        }
        catch (ConfigException ex)
        {
            stderr.WriteLine(ex.Message);
            return 2;
        }

        var outputs = config.Outputs.Where(o => o.Binary is not null).ToList();
        var problems = ValidateConfig(config, outputs);
        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                stderr.WriteLine(problem);
            }

            return 2;
        }

        // 生成ソースを書き直すことがあるので、code-gen と同じく出力先を丸ごと入れ替えてよいかを確かめる
        var unsafeOutputs = OutputDirectory.Validate(config);
        if (unsafeOutputs.Count > 0)
        {
            foreach (var problem in unsafeOutputs)
            {
                stderr.WriteLine(problem);
            }

            stderr.WriteLine("出力先は丸ごと削除されるため、この設定では実行しません。");
            return 2;
        }

        foreach (var skipped in config.Outputs.Where(o => o.Binary is null))
        {
            stdout.WriteLine($"{skipped.Name}: binary がないため、対象にしません。");
        }

        // 0. 定義からメモリ上でコード生成し、出力先の生成ソースと比べる。違えば(古ければ)書き直す
        var sources = UpdateGeneratedSources(config, check, stdout, stderr, out var definitionSources, out var staleOutputs, out var exitCode);
        if (sources is null)
        {
            return exitCode;
        }

        // 1. 定義から、テーブルの全カラムと enum を読む(コード生成でパースした定義を使い回す)
        var schema = DefinitionSchemaReader.Read(definitionSources);
        if (schema.Errors.Count > 0)
        {
            foreach (var error in schema.Errors)
            {
                stderr.WriteLine(error);
            }

            stderr.WriteLine("定義を読めないため、バイナリは作っていません。");
            return 1;
        }

        // `_` は CSV の水平分割の区切りなので、テーブル名には使えない
        var invalidTables = schema.Tables.Where(t => t.TableName.Contains(CsvTableReader.SplitSeparator)).ToList();
        if (invalidTables.Count > 0)
        {
            foreach (var table in invalidTables)
            {
                stderr.WriteLine($"{table.File}: テーブル名 {table.TableName} に '{CsvTableReader.SplitSeparator}' は使えません"
                    + "('_' は CSV の水平分割の区切りです。dropTable のように付けてください)。");
            }

            stderr.WriteLine("定義を読めないため、バイナリは作っていません。");
            return 1;
        }

        // 2. CSV を読み、ヘッダと enum の入力名を確かめる(対象 client / server に関係ない検証)
        var csvErrors = new List<CsvError>();
        var csvTables = ReadCsvTables(config.Csv!, schema, csvErrors, stderr);
        ValidateEnums(schema, csvTables.Keys, csvErrors);

        // データ段の検証([Unique] / [Exists])。CSV の段にエラーがなければ行う。行の変換のエラーも一緒に表示できるよう、別のリストに入れる
        var dataErrors = new List<CsvError>();
        if (csvErrors.Count == 0)
        {
            DataValidator.Validate(schema, csvTables, dataErrors);
        }

        // 3. 出力先ごとに、生成ソースをコンパイルして行を変換し、バイナリを作る。書き込みは最後にまとめて行う。
        //    同じ target の出力先は生成ソースの中身も同じなので、コンパイルは中身ごとに1回だけ行う
        var compileErrors = new List<string>();
        var binaries = new List<(OutputConfig Output, byte[] Bytes, int Tables)>();
        var compilations = new Dictionary<string, CompilationResult>(StringComparer.Ordinal);
        if (csvErrors.Count == 0)
        {
            foreach (var output in outputs)
            {
                var outputFiles = sources[output.Name];
                // 生成ソースは target・定義・namespace だけで決まる。定義の違いはスキーマのハッシュに表れる
                // (MasterDataSchema.cs 以外の生成ソースから計算するので、namespace は別に含める)。target ごとに参照も違う
                var sourceKey = $"{output.Target}:{outputFiles.SchemaHash}:{output.Namespace}";
                if (!compilations.TryGetValue(sourceKey, out var compiled))
                {
                    compiled = InMemoryCompiler.Compile(outputFiles.Files, output.Target);
                    compilations[sourceKey] = compiled;
                }

                var binary = BuildOutput(output, compiled, outputFiles.SchemaHash, schema, csvTables, csvErrors, compileErrors);
                if (binary is not null)
                {
                    binaries.Add((output, binary.Value.Bytes, binary.Value.Tables));
                }
            }
        }

        if (csvErrors.Count > 0 || compileErrors.Count > 0 || dataErrors.Count > 0)
        {
            foreach (var error in compileErrors)
            {
                stderr.WriteLine(error);
            }

            // 対象ごとに同じ CSV を読むので、同じエラーは1回だけ表示する
            foreach (var error in csvErrors.Distinct().Concat(dataErrors))
            {
                stderr.WriteLine(error);
            }

            stderr.WriteLine("エラーがあるため、どの出力先にもバイナリを書き込んでいません。");
            return 1;
        }

        if (check)
        {
            // CI でコード生成のし忘れ(古い生成ソースのコミット)を見逃さないよう、CSV にエラーがなくても失敗にする
            if (staleOutputs.Count > 0)
            {
                stderr.WriteLine($"検証のみ(--check): CSV にエラーはありませんが、生成ソースが定義と合っていない出力先があります: {string.Join(", ", staleOutputs)}。"
                    + "code-gen を実行してください。");
                return 1;
            }

            stdout.WriteLine($"検証のみ(--check): エラーはありません。対象の出力先: {string.Join(", ", binaries.Select(b => b.Output.Name))}");
            return 0;
        }

        // バイナリと、その隣にマニフェストを書く。作った日時は、すべての出力先で同じにする
        var createdAt = DateTime.UtcNow;
        var files = new List<(string Path, byte[] Content)>();
        foreach (var (output, bytes, _) in binaries)
        {
            var manifest = MasterDataManifest.Create(bytes, createdAt);
            files.Add((output.Binary!, bytes));
            files.Add((MasterDataManifest.PathFor(output.Binary!), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(manifest.ToJson())));
        }

        if (!WriteFiles(files, stderr))
        {
            return 1;
        }

        foreach (var (output, bytes, tables) in binaries)
        {
            var encrypted = output.Encrypt ? " / 暗号化" : "";
            stdout.WriteLine($"{output.Name}: 対象: {output.Target} / テーブル: {tables} / {bytes.Length} bytes{encrypted} → {output.Binary}");
        }

        return 0;
    }

    // すべてのファイルを、先に一時ファイル(<パス>.tmp)に書き、すべて書けてから本来の名前に置き換える。
    // 一時ファイルの段階で失敗したら、一時ファイルを消して元のファイルは変えない。失敗したら false
    private static bool WriteFiles(IReadOnlyList<(string Path, byte[] Content)> files, TextWriter stderr)
    {
        var temporaries = new List<string>();
        foreach (var (path, content) in files)
        {
            var temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                temporaries.Add(temporary);
                File.WriteAllBytes(temporary, content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DeleteQuietly(temporaries);
                stderr.WriteLine($"{temporary}: 書き込めません: {ex.Message}");
                stderr.WriteLine("どの出力先のバイナリもマニフェストも書き換えていません。");
                return false;
            }
        }

        var replaced = new List<string>();
        for (var i = 0; i < files.Count; i++)
        {
            try
            {
                File.Move(temporaries[i], files[i].Path, overwrite: true);
                replaced.Add(files[i].Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DeleteQuietly(temporaries.Skip(i));
                stderr.WriteLine($"{files[i].Path}: 置き換えられません: {ex.Message}");
                stderr.WriteLine(replaced.Count == 0
                    ? "どの出力先のバイナリもマニフェストも書き換えていません。"
                    : $"次のファイルだけが新しくなっています(もう一度実行してください): {string.Join(", ", replaced)}");
                return false;
            }
        }

        return true;
    }

    private static void DeleteQuietly(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 消せない一時ファイルは残す(次の実行で上書きする)
            }
        }
    }

    // 定義からメモリ上でコード生成し、出力先ごとの生成ソースを返す。出力先のファイルと違えば書き直す(check のときは書かずに警告し、
    // 出力先の名前を staleOutputs に入れる)。パースした定義を definitionSources に入れる。失敗したら null を返し、終了コードを exitCode に入れる
    private static Dictionary<string, OutputFiles>? UpdateGeneratedSources(
        ToolsConfig config,
        bool check,
        TextWriter stdout,
        TextWriter stderr,
        out IReadOnlyList<DefinitionSource> definitionSources,
        out List<string> staleOutputs,
        out int exitCode)
    {
        staleOutputs = new List<string>();
        exitCode = 0;
        var generated = ConfigCodeGenerator.Generate(config);
        definitionSources = generated.Sources;
        if (generated.SyntaxErrors.Count > 0)
        {
            foreach (var error in generated.SyntaxErrors)
            {
                stderr.WriteLine(error);
            }

            stderr.WriteLine("定義にエラーがあるため、生成ソースもバイナリも書き込んでいません。");
            exitCode = 1;
            return null;
        }

        if (generated.Problem is not null)
        {
            stderr.WriteLine(generated.Problem);
            exitCode = 2;
            return null;
        }

        var sources = new Dictionary<string, OutputFiles>(StringComparer.Ordinal);
        foreach (var outputFiles in generated.Outputs)
        {
            var (output, files, _, _) = outputFiles;
            sources[output.Name] = outputFiles;
            if (GeneratedSourceComparer.IsUpToDate(output.Source, files))
            {
                continue;
            }

            if (check)
            {
                staleOutputs.Add(output.Name);
                stderr.WriteLine($"警告: {output.Name}: 生成ソース({output.Source})が定義と合っていません。code-gen を実行してください"
                    + "(検証は、定義から作り直した生成ソースで行います)。");
            }
            else
            {
                OutputDirectory.Replace(output.Source, files);
                stdout.WriteLine($"{output.Name}: 生成ソースが定義と合っていないため、コード生成をやり直しました → {output.Source}");
            }
        }

        return sources;
    }

    private static List<string> ValidateConfig(ToolsConfig config, List<OutputConfig> outputs)
    {
        var problems = new List<string>();
        if (outputs.Count == 0)
        {
            problems.Add("binary を書いた出力先がありません(outputs[].binary にバイナリのパスを書いてください)。");
            return problems;
        }

        if (config.Csv is null)
        {
            problems.Add("csv(CSV のディレクトリ)は master-gen では必須です。");
            return problems;
        }

        if (!Directory.Exists(config.Definitions))
        {
            problems.Add($"definitions のディレクトリが見つかりません: {config.Definitions}");
        }

        if (!Directory.Exists(config.Csv))
        {
            problems.Add($"csv のディレクトリが見つかりません: {config.Csv}");
        }

        foreach (var output in outputs)
        {
            var label = $"outputs[{output.Name}].binary ({output.Binary})";

            // コード生成は source を丸ごと入れ替えるので、その中にバイナリを置くと消える。入力と出力も混ぜない
            foreach (var other in config.Outputs)
            {
                if (PathUtility.IsSameOrUnder(output.Binary!, other.Source))
                {
                    problems.Add($"{label} が、outputs[{other.Name}].source の中にあります(コード生成で消えます)。");
                }
            }

            if (PathUtility.IsSameOrUnder(output.Binary!, config.Definitions))
            {
                problems.Add($"{label} が、definitions の中にあります。");
            }

            if (PathUtility.IsSameOrUnder(output.Binary!, config.Csv))
            {
                problems.Add($"{label} が、csv の中にあります。");
            }
        }

        foreach (var duplicate in outputs.GroupBy(o => Path.GetFullPath(o.Binary!), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"binary が重なっています: {string.Join(", ", duplicate.Select(o => o.Name))}");
        }

        // マニフェストは binary の拡張子を .manifest.json に変えたパスに置くので、ほかの出力先のマニフェストやバイナリと重ならないこと
        var binaryPaths = outputs.Select(o => Path.GetFullPath(o.Binary!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in outputs.GroupBy(o => Path.GetFullPath(MasterDataManifest.PathFor(o.Binary!)), StringComparer.OrdinalIgnoreCase))
        {
            if (duplicate.Count() > 1 && duplicate.Select(o => Path.GetFullPath(o.Binary!)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                problems.Add($"マニフェスト({duplicate.Key})が重なっています: {string.Join(", ", duplicate.Select(o => o.Name))}(binary のファイル名を変えてください)。");
            }

            if (binaryPaths.Contains(duplicate.Key))
            {
                problems.Add($"マニフェスト({duplicate.Key})が、ほかの出力先の binary と同じパスです。");
            }
        }

        return problems;
    }

    private static Dictionary<string, CsvTable> ReadCsvTables(
        string csvDirectory,
        DefinitionSchema schema,
        List<CsvError> errors,
        TextWriter stderr)
    {
        var tables = new Dictionary<string, CsvTable>(StringComparer.Ordinal);

        var files = CsvTableReader.EnumerateFiles(csvDirectory);
        var filesByTable = CsvTableReader.GroupFilesByTable(files);

        foreach (var tableSchema in schema.Tables)
        {
            var tableFiles = filesByTable.TryGetValue(tableSchema.TableName, out var found) ? found : new List<string>();
            var table = CsvTableReader.Read(csvDirectory, tableSchema.TableName, tableFiles, errors);
            if (table is null)
            {
                continue;
            }

            ValidateHeader(table, tableSchema, errors);
            tables[tableSchema.TableName] = table;
        }

        var tableNames = schema.Tables.Select(t => t.TableName).ToHashSet(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var tableName = CsvTableReader.GetTableName(file);
            if (tableName is null || !tableNames.Contains(tableName))
            {
                stderr.WriteLine($"警告: {file} はどのテーブルにも一致しないため、読みません。");
            }
        }

        return tables;
    }

    // ヘッダの列は、定義の全カラム(ServerOnly / ClientOnly を含む)と過不足なく一致すること(順番は問わない)
    private static void ValidateHeader(CsvTable table, TableSchema schema, List<CsvError> errors)
    {
        var file = table.Files[0];
        foreach (var duplicate in table.Header.GroupBy(h => h, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add(new CsvError(file, table.HeaderLine, duplicate.Key, "ヘッダに同じ列が複数あります。"));
        }

        var columns = schema.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var unknown in table.Header.Where(h => !columns.Contains(h)).Distinct())
        {
            errors.Add(new CsvError(file, table.HeaderLine, unknown, $"{schema.TypeName} にない列です(列名はプロパティ名と大文字小文字まで一致させてください)。"));
        }

        foreach (var missing in schema.Columns.Where(c => !table.Header.Contains(c.Name, StringComparer.Ordinal)))
        {
            errors.Add(new CsvError(file, table.HeaderLine, missing.Name, $"{schema.TypeName}.{missing.Name} の列がヘッダにありません。"));
        }
    }

    // CSV の列で使う enum は、すべてのメンバーに [InputName] が付いていること
    private static void ValidateEnums(DefinitionSchema schema, IEnumerable<string> tableNames, List<CsvError> errors)
    {
        var used = schema.Tables
            .Where(t => tableNames.Contains(t.TableName))
            .SelectMany(t => t.Columns.Select(c => (Table: t, Column: c)))
            .Select(x => (x.Table, x.Column, Enum: schema.FindEnum(x.Table, x.Column.TypeName)))
            .Where(x => x.Enum is not null)
            .GroupBy(x => x.Enum!.FullName);

        foreach (var group in used)
        {
            var enumSchema = group.First().Enum!;
            var missing = enumSchema.Members.Where(m => m.InputName is null).Select(m => m.Name).ToList();
            if (missing.Count > 0)
            {
                errors.Add(new CsvError(enumSchema.File, null, null,
                    $"{enumSchema.Name} の {string.Join(", ", missing)} に [InputName] がありません(CSV の列で使う enum は、すべてのメンバーに付けてください)。"));
            }

            foreach (var duplicate in enumSchema.Members.Where(m => m.InputName is not null).GroupBy(m => m.InputName).Where(g => g.Count() > 1))
            {
                errors.Add(new CsvError(enumSchema.File, null, null,
                    $"{enumSchema.Name} の入力名 {duplicate.Key} が重複しています: {string.Join(", ", duplicate.Select(m => m.Name))}"));
            }
        }
    }

    private static (byte[] Bytes, int Tables)? BuildOutput(
        OutputConfig output,
        CompilationResult compiled,
        string schemaHash,
        DefinitionSchema schema,
        Dictionary<string, CsvTable> csvTables,
        List<CsvError> csvErrors,
        List<string> compileErrors)
    {
        if (compiled.Assembly is null)
        {
            compileErrors.Add($"{output.Name}: 生成ソース({output.Source})をコンパイルできません。code-gen をやり直してください。");
            compileErrors.AddRange(compiled.Errors.Select(e => $"  {e}"));
            return null;
        }

        var assembly = compiled.Assembly;
        var tables = new List<(Type RowType, IList Rows)>();
        var count = csvErrors.Count;
        foreach (var (tableName, rowType) in MasterDataBinaryBuilder.FindTables(assembly).OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var tableSchema = schema.Tables.FirstOrDefault(t => t.TableName == tableName);
            if (tableSchema is null)
            {
                compileErrors.Add($"{output.Name}: 生成ソースのテーブル {tableName} が定義にありません。code-gen をやり直してください。");
                continue;
            }

            if (!csvTables.TryGetValue(tableName, out var csvTable))
            {
                continue;
            }

            var rows = TableRowConverter.Convert(csvTable, tableSchema, rowType, schema.Enums, csvErrors);
            if (rows is not null)
            {
                tables.Add((rowType, rows));
            }
        }

        if (csvErrors.Count != count || compileErrors.Count > 0)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = MasterDataBinaryBuilder.Build(assembly, tables);
        }
        catch (Exception ex)
        {
            compileErrors.Add($"{output.Name}: バイナリを作れません: {ex.Message}");
            return null;
        }

        // MasterMemory の db.Validate()(IValidatable<T> を実装した型の検証)。実装した型がなければ何もしない
        string? failure;
        try
        {
            failure = MasterDataBinaryBuilder.Validate(assembly, bytes);
        }
        catch (Exception ex)
        {
            compileErrors.Add($"{output.Name}: db.Validate() を実行できません: {ex.Message}");
            return null;
        }

        if (failure is not null)
        {
            compileErrors.Add($"{output.Name}: MasterMemory の db.Validate() で検証に失敗しました:{Environment.NewLine}{failure}");
            return null;
        }

        // 平文のヘッダを付ける。client で encrypt なら、スキーマのハッシュを鍵にして中身を暗号化する
        bytes = MasterDataBinary.Write(bytes, schemaHash, output.Encrypt ? new AesMasterDataCipher() : null);
        return (bytes, tables.Count);
    }

    private static bool TryNext(string[] args, ref int index, out string? value)
    {
        if (index + 1 < args.Length)
        {
            value = args[++index];
            return true;
        }

        value = null;
        return false;
    }

    private static int MissingValue(string option, TextWriter stderr)
    {
        stderr.WriteLine($"{option} には値が必要です。");
        return 2;
    }
}
