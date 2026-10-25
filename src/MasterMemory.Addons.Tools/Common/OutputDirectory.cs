using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// 設定ファイルの出力先(source)ディレクトリを扱う。出力先は生成専用とみなし、中身を丸ごと入れ替える
/// (生成するファイルとフォルダの Unity の .meta だけは残す)。
/// </summary>
public static class OutputDirectory
{
    /// <summary>
    /// 出力先を丸ごと削除してよいかを確かめ、問題を返す(空なら問題なし)。
    /// 誤設定で手書きのファイルを消さないための確認。
    /// </summary>
    public static List<string> Validate(ToolsConfig config)
    {
        var problems = new List<string>();
        var outputs = config.Outputs;

        foreach (var output in outputs)
        {
            var source = output.Source;
            var label = $"outputs[{output.Name}].source ({source})";

            if (PathUtility.IsRoot(source))
            {
                problems.Add($"{label} がドライブのルートです。");
            }

            if (PathUtility.IsSameOrUnder(config.ConfigDirectory, source))
            {
                problems.Add($"{label} が、設定ファイルのディレクトリと同じか、その上位にあります。");
            }

            if (PathUtility.IsSameOrUnder(config.Definitions, source)
                || PathUtility.IsSameOrUnder(source, config.Definitions))
            {
                problems.Add($"{label} が、definitions ({config.Definitions}) と同じか、その上位・下位にあります。");
            }

            if (config.Csv != null
                && (PathUtility.IsSameOrUnder(config.Csv, source) || PathUtility.IsSameOrUnder(source, config.Csv)))
            {
                problems.Add($"{label} が、csv ({config.Csv}) と同じか、その上位・下位にあります。");
            }
        }

        for (var i = 0; i < outputs.Count; i++)
        {
            for (var j = i + 1; j < outputs.Count; j++)
            {
                if (PathUtility.IsSameOrUnder(outputs[i].Source, outputs[j].Source)
                    || PathUtility.IsSameOrUnder(outputs[j].Source, outputs[i].Source))
                {
                    problems.Add(
                        $"outputs[{outputs[i].Name}] と outputs[{outputs[j].Name}] の source が重なっています"
                        + "(同じか、一方がもう一方の中にあります)。");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// ディレクトリの中身を削除し、生成したファイルを書き込む。生成するファイルとフォルダの Unity の <c>.meta</c> は残す
    /// (作り直すたびに GUID が変わり、.meta の差分が出ないように)。なくなるファイルの .meta は消す。
    /// </summary>
    public static void Replace(string directory, IEnumerable<GeneratedFile> files)
    {
        var generated = files.ToList();
        if (Directory.Exists(directory))
        {
            var keep = MetaFilesToKeep(generated);
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!keep.Contains(Normalize(Path.GetRelativePath(directory, file))))
                {
                    File.Delete(file);
                }
            }

            // 残した .meta がないフォルダは消す(深い方から)
            foreach (var subdirectory in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(subdirectory).Any())
                {
                    Directory.Delete(subdirectory);
                }
            }
        }
        else
        {
            Directory.CreateDirectory(directory);
        }

        foreach (var file in generated)
        {
            var destination = Path.Combine(directory, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, file.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    /// <summary>
    /// <see cref="Replace"/> が残す .meta の相対パス(区切りは `/`。英大文字・小文字は区別しない)。
    /// 生成するファイル(Tables/Skill.cs)と、それを入れるフォルダ(Tables)の .meta。
    /// </summary>
    public static HashSet<string> MetaFilesToKeep(IEnumerable<GeneratedFile> files)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var path = Normalize(file.RelativePath);
            keep.Add(path + ".meta");
            for (var slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
            {
                keep.Add(path.Substring(0, slash) + ".meta");
            }
        }

        return keep;
    }

    private static string Normalize(string relativePath)
    {
        return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
