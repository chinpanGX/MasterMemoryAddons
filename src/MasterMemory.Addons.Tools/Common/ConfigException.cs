using System;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>設定ファイルの内容が不正。</summary>
public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message)
    {
    }
}
