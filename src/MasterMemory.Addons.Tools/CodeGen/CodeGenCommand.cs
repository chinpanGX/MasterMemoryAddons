using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MasterMemory.Addons.Tools.Common;

namespace MasterMemory.Addons.Tools.CodeGen;

/// <summary>code-gen コマンド(コード生成)。引数処理とファイル入出力。仕様は docs/design/code-generation.md。</summary>
public static class CodeGenCommand
{
    private const string Usage = """
        MasterMemory.Addons.Tools code-gen

        定義(C# ソース)から、クライアント用/サーバー用のソースを出力します(コード生成)。

        使い方:
          MasterMemory.Addons.Tools code-gen --config <file> [--env <name>]
          MasterMemory.Addons.Tools code-gen --input <dir> --target <client|server> --output <dir>

        オプション:
          -c, --config   設定ファイル(YAML)。outputs の出力先ごとに生成し、source のディレクトリへ置く
                         (source の中身は丸ごと入れ替える)
          -e, --env      設定ファイルの environments から、上書きに使う環境の名前
          -i, --input    定義ソース(*.cs)のディレクトリ。サブディレクトリも対象
          -t, --target   client または server
          -o, --output   出力先ディレクトリ(入力と同じ相対パスで出力)
          -h, --help     このヘルプを表示
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? config = null;
        string? environment = null;
        string? input = null;
        string? output = null;
        string? targetText = null;

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
                case "-i":
                case "--input":
                    if (!TryNext(args, ref i, out input)) return MissingValue(arg, stderr);
                    break;
                case "-t":
                case "--target":
                    if (!TryNext(args, ref i, out targetText)) return MissingValue(arg, stderr);
                    break;
                case "-o":
                case "--output":
                    if (!TryNext(args, ref i, out output)) return MissingValue(arg, stderr);
                    break;
                default:
                    stderr.WriteLine($"不明なオプションです: {arg}");
                    stderr.WriteLine(Usage);
                    return 2;
            }
        }

        if (config is not null)
        {
            if (input is not null || output is not null || targetText is not null)
            {
                stderr.WriteLine("--config と --input / --target / --output は同時に指定できません。");
                return 2;
            }

            return RunWithConfig(config, environment, stdout, stderr);
        }

        if (environment is not null)
        {
            stderr.WriteLine("--env は --config と一緒に指定してください。");
            return 2;
        }

        if (input is null || output is null || targetText is null)
        {
            stderr.WriteLine("--config を使わない場合、--input, --target, --output はすべて必須です。");
            stderr.WriteLine(Usage);
            return 2;
        }

        if (!GenerationTargets.TryParse(targetText, out var target))
        {
            stderr.WriteLine($"--target は client または server を指定してください: {targetText}");
            return 2;
        }

        return RunWithArguments(input, target, output, stdout, stderr);
    }

    private static int RunWithConfig(string configPath, string? environment, TextWriter stdout, TextWriter stderr)
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

        if (!Directory.Exists(config.Definitions))
        {
            stderr.WriteLine($"definitions のディレクトリが見つかりません: {config.Definitions}");
            return 2;
        }

        var problems = OutputDirectory.Validate(config);
        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                stderr.WriteLine(problem);
            }

            stderr.WriteLine("出力先は丸ごと削除されるため、この設定では実行しません。");
            return 2;
        }

        // 先にすべての対象を生成し、定義のエラーが1つでもあれば、どの出力先にも触れない
        var generated = ConfigCodeGenerator.Generate(config);
        if (generated.SyntaxErrors.Count > 0)
        {
            foreach (var error in generated.SyntaxErrors)
            {
                stderr.WriteLine(error);
            }

            stderr.WriteLine("定義にエラーがあるため、出力先には何も書き込んでいません。");
            return 1;
        }

        if (generated.Problem is not null)
        {
            stderr.WriteLine(generated.Problem);
            return 2;
        }

        if (generated.SourceCount == 0)
        {
            stderr.WriteLine($"警告: {config.Definitions} に *.cs が見つかりませんでした。");
        }

        foreach (var (output, files, _, omitted) in generated.Outputs)
        {
            OutputDirectory.Replace(output.Source, files);
            stdout.WriteLine($"{output.Name}: 対象: {output.Target} / 出力: {files.Count} / 対象外: {omitted} → {output.Source}");
        }

        return 0;
    }

    private static int RunWithArguments(
        string input,
        GenerationTarget target,
        string output,
        TextWriter stdout,
        TextWriter stderr)
    {
        var inputFull = Path.GetFullPath(input);
        var outputFull = Path.GetFullPath(output);
        if (!Directory.Exists(inputFull))
        {
            stderr.WriteLine($"入力ディレクトリが見つかりません: {inputFull}");
            return 2;
        }

        var result = SourceGenerator.Generate(DefinitionSource.Load(inputFull, new[] { outputFull }), target);

        foreach (var file in result.Files)
        {
            var destination = Path.Combine(outputFull, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, file.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        // この対象に残る型がない。以前の出力があれば削除して同期を保つ
        foreach (var relative in result.Omitted)
        {
            var destination = Path.Combine(outputFull, relative);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        foreach (var error in result.Errors)
        {
            stderr.WriteLine($"{error.RelativePath}: 定義のエラー{Environment.NewLine}{error.Message}");
        }

        stdout.WriteLine(
            $"対象: {target} / 出力: {result.Files.Count} / 対象外: {result.Omitted.Count} / 失敗: {result.Errors.Count}");
        if (result.SourceCount == 0)
        {
            stderr.WriteLine($"警告: {inputFull} に *.cs が見つかりませんでした。");
        }

        return result.Errors.Count > 0 ? 1 : 0;
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
