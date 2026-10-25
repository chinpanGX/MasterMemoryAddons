using System.IO;
using Xunit;

namespace MasterMemory.Addons.Tools.Tests;

public class CliTests
{
    [Fact]
    public void NoArguments_Returns2()
    {
        // 検証: 引数なしで実行する
        // 期待: 使い方を表示して 2 を返す
        var stderr = new StringWriter();

        Assert.Equal(2, Cli.Run(new string[0], new StringWriter(), stderr));
        Assert.Contains("使い方", stderr.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_Returns0(string option)
    {
        // 検証: --help / -h だけで実行する
        // 期待: 使い方を表示して 0 を返す
        var stdout = new StringWriter();

        Assert.Equal(0, Cli.Run(new[] { option }, stdout, new StringWriter()));
        Assert.Contains("code-gen", stdout.ToString());
        Assert.Contains("master-gen", stdout.ToString());
    }

    [Fact]
    public void UnknownCommand_Returns2()
    {
        // 検証: 存在しないコマンド名で実行する
        // 期待: 2 を返す
        Assert.Equal(2, Cli.Run(new[] { "fetch" }, new StringWriter(), new StringWriter()));
    }

    [Theory]
    [InlineData("generate")]
    [InlineData("build")]
    public void OldCommandName_Returns2(string command)
    {
        // 検証: 改名する前のコマンド名(generate / build)で実行する
        // 期待: 不明なコマンドとして 2 を返す
        Assert.Equal(2, Cli.Run(new[] { command }, new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void CodeGen_PassesRemainingArguments()
    {
        // 検証: code-gen --help で実行する
        // 期待: code-gen に残りの引数が渡り、code-gen の使い方を表示して 0 を返す
        var stdout = new StringWriter();

        Assert.Equal(0, Cli.Run(new[] { "code-gen", "--help" }, stdout, new StringWriter()));
        Assert.Contains("code-gen --config", stdout.ToString());
    }

    [Fact]
    public void MasterGen_PassesRemainingArguments()
    {
        // 検証: master-gen --help で実行する
        // 期待: master-gen に残りの引数が渡り、master-gen の使い方を表示して 0 を返す
        var stdout = new StringWriter();

        Assert.Equal(0, Cli.Run(new[] { "master-gen", "--help" }, stdout, new StringWriter()));
        Assert.Contains("master-gen --config", stdout.ToString());
    }
}
