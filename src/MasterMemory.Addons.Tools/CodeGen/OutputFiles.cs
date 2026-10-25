using System.Collections.Generic;
using MasterMemory.Addons.Tools.Common;

namespace MasterMemory.Addons.Tools.CodeGen;

/// <summary>
/// 1つの出力先に置くファイル(<c>MasterDataSchema.cs</c> を含む)。<see cref="SchemaHash"/> は <c>MasterDataSchema.Hash</c> の値。
/// <see cref="Omitted"/> は、この対象に型が残らず出力しない定義のファイルの数。
/// </summary>
public sealed record OutputFiles(OutputConfig Output, IReadOnlyList<GeneratedFile> Files, string SchemaHash, int Omitted);
