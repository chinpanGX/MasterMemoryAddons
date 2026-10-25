namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// CSV の1つのエラー。<see cref="File"/> は CSV のディレクトリからの相対パス。
/// <see cref="Line"/> と <see cref="Column"/> は分からなければ null。
/// </summary>
public sealed record CsvError(string File, int? Line, string? Column, string Message)
{
    public override string ToString()
    {
        var location = File;
        if (Line is not null)
        {
            location += $" {Line}行";
        }

        if (Column is not null)
        {
            location += $" 列 {Column}";
        }

        return $"{location}: {Message}";
    }
}
