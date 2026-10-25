using System;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>CSV として読めない(閉じていない引用符など)。</summary>
public sealed class CsvFormatException : Exception
{
    public CsvFormatException(int line, string message) : base(message)
    {
        Line = line;
    }

    public int Line { get; }
}
