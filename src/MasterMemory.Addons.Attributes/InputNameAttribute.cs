using System;

namespace MasterMemory.Addons
{
    /// <summary>
    /// enum のメンバーに、スプレッドシート(CSV)で入力する名前を付ける。
    /// CSV にはこの名前を書く(C# のメンバー名や数値は受け付けない)。コード生成の出力からは取り除かれる。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class InputNameAttribute : Attribute
    {
        public InputNameAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
