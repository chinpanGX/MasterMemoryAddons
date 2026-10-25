using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MasterMemory.Addons.Tools.Common;

namespace MasterMemory.Addons.Tools.CodeGen;

/// <summary>出力先にある生成ソースが、コード生成の結果と同じかを比べる(docs/design/master-gen.md の「生成ソースが古いとき」)。</summary>
public static class GeneratedSourceComparer
{
    /// <summary>
    /// <paramref name="directory"/> を <paramref name="files"/> で作り直しても何も変わらないなら true。
    /// <c>*.cs</c> が過不足なく同じ中身で、作り直すと消えるファイル(生成しなくなったファイル・フォルダの <c>.meta</c>、手で置いたファイル)もないこと。
    /// 生成するファイル・フォルダの <c>.meta</c>(<see cref="OutputDirectory.MetaFilesToKeep"/>)は比べない。改行コードの違い(CRLF / LF)は無視する。
    /// </summary>
    public static bool IsUpToDate(string directory, IReadOnlyList<GeneratedFile> files)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        var keep = OutputDirectory.MetaFilesToKeep(files);
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Normalize(Path.GetRelativePath(directory, path));
            if (keep.Contains(relative))
            {
                continue;
            }

            actual[relative] = path;
        }

        // 生成するファイル以外が残っていれば、作り直すと消える
        if (actual.Count != files.Count)
        {
            return false;
        }

        foreach (var file in files)
        {
            if (!actual.TryGetValue(Normalize(file.RelativePath), out var path)
                || NormalizeNewLines(File.ReadAllText(path)) != NormalizeNewLines(file.Text))
            {
                return false;
            }
        }

        return true;
    }

    private static string Normalize(string relativePath)
    {
        return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string NormalizeNewLines(string text)
    {
        return text.Replace("\r\n", "\n");
    }
}
