namespace MasterMemory.Addons.Tools.Common;

/// <summary>enum のメンバー1つ。<see cref="InputName"/> は <c>[InputName]</c> の名前で、付いていなければ null。</summary>
public sealed record EnumMemberSchema(string Name, long Value, string? InputName);
