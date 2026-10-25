using System;

namespace MasterMemory.Addons
{
    /// <summary>
    /// 列の値が、テーブルの中で重ならないことをマスタ生成で確かめる(主キー以外の列に使う)。
    /// 空欄のセルは型の既定値(<c>0</c> / <c>""</c> など)として比べる。コード生成の出力からは取り除かれる。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class UniqueAttribute : Attribute
    {
    }
}
