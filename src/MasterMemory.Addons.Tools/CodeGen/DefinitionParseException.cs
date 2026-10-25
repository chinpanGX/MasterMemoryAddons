using System;

namespace MasterMemory.Addons.Tools.CodeGen;

/// <summary>定義ソースに構文エラーがある、またはテーブルの型が record など、定義として使えない。</summary>
public sealed class DefinitionParseException : Exception
{
    public DefinitionParseException(string message) : base(message)
    {
    }
}
