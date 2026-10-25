using System;

namespace MasterMemory.Addons.Tools.Common;

public static class GenerationTargets
{
    /// <summary>"client" / "server"(大文字小文字は区別しない)を読む。数値は受け付けない。</summary>
    public static bool TryParse(string? text, out GenerationTarget target)
    {
        if (string.Equals(text, "client", StringComparison.OrdinalIgnoreCase))
        {
            target = GenerationTarget.Client;
            return true;
        }

        if (string.Equals(text, "server", StringComparison.OrdinalIgnoreCase))
        {
            target = GenerationTarget.Server;
            return true;
        }

        target = default;
        return false;
    }
}
