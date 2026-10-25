using System;
using System.IO;

namespace MasterMemory.Addons.Tools.Tests;

/// <summary>テスト用の一時ディレクトリ。Dispose で削除する。</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mma-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>相対パスでファイルを書く(ディレクトリも作る)。書いたファイルの絶対パスを返す。</summary>
    public string Write(string relativePath, string text)
    {
        var full = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    public string Combine(string relativePath)
    {
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relativePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
