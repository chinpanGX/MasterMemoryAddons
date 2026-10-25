using System.Collections.Generic;
using MasterMemory.Addons.Tools.Common;

namespace MasterMemory.Addons.Tools.CodeGen;

/// <summary>定義のファイル(<see cref="DefinitionSource.Load"/> でパースしたもの)をまとめて、1つの対象向けに変換する。</summary>
public static class SourceGenerator
{
    public static GenerationResult Generate(IReadOnlyList<DefinitionSource> sources, GenerationTarget target)
    {
        var result = new GenerationResult();
        foreach (var source in sources)
        {
            try
            {
                var text = DefinitionTransformer.Transform(source.Tree, target);
                if (text is null)
                {
                    result.Omitted.Add(source.RelativePath);
                }
                else
                {
                    result.Files.Add(new GeneratedFile(source.RelativePath, text));
                }
            }
            catch (DefinitionParseException ex)
            {
                result.Errors.Add(new GenerationError(source.RelativePath, ex.Message));
            }
        }

        return result;
    }
}
