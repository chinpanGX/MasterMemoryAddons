using System.Collections.Generic;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>CSV の1レコード。<see cref="Line"/> はレコードが始まる行の番号(1 から)。</summary>
public sealed record CsvRecord(int Line, IReadOnlyList<string> Fields);
