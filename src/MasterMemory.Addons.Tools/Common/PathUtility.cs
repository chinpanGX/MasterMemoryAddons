using System;
using System.IO;

namespace MasterMemory.Addons.Tools.Common;

internal static class PathUtility
{
    /// <summary>
    /// <paramref name="path"/> が <paramref name="directory"/> と同じか、その下にあるか。どちらも絶対パス。
    /// 大文字小文字は区別しない(区別するファイルシステムでは、安全側=「重なっている」と判定しやすくなる)。
    /// </summary>
    public static bool IsSameOrUnder(string path, string directory)
    {
        var p = Normalize(path);
        var d = Normalize(directory);
        if (string.Equals(p, d, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = d.EndsWith(Path.DirectorySeparatorChar) ? d : d + Path.DirectorySeparatorChar;
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ドライブ(ファイルシステム)のルートか。</summary>
    public static bool IsRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return root is not null && string.Equals(Normalize(path), Normalize(root), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";

        // ルート("C:\" や "/")の末尾の区切りは残す
        return full.Length > root.Length
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }
}
