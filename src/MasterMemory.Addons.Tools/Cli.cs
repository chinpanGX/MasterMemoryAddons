using System.IO;
using MasterMemory.Addons.Tools.MasterGen;
using MasterMemory.Addons.Tools.CodeGen;

namespace MasterMemory.Addons.Tools;

/// <summary>先頭の引数でコマンドを選び、そのコマンドに残りの引数を渡す。</summary>
public static class Cli
{
    private const string Usage = """
        MasterMemory.Addons.Tools

        マスターデータを扱うツールです。1回の実行で、1つのコマンドを行います。
        複数のコマンドを順につなぐのは Makefile などの役割です(master-gen は、生成ソースが古ければコード生成もやり直します)。

        使い方:
          MasterMemory.Addons.Tools <command> [options]

        コマンド:
          code-gen     コード生成。定義(C# ソース)から、クライアント用/サーバー用のソースを出力する
          master-gen   マスタ生成。CSV を検証し、MasterMemory のバイナリを作る(--check で検証だけ)

        各コマンドのオプションは MasterMemory.Addons.Tools <command> --help で表示します。
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine(Usage);
            return 2;
        }

        var command = args[0];
        var rest = args[1..];
        switch (command)
        {
            case "-h":
            case "--help":
                stdout.WriteLine(Usage);
                return 0;
            case "code-gen":
                return CodeGenCommand.Run(rest, stdout, stderr);
            case "master-gen":
                return MasterGenCommand.Run(rest, stdout, stderr);
            default:
                stderr.WriteLine($"不明なコマンドです: {command}");
                stderr.WriteLine(Usage);
                return 2;
        }
    }
}
