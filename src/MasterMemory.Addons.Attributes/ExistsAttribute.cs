using System;

namespace MasterMemory.Addons
{
    /// <summary>
    /// 列の値が、別のテーブルの列にあることをマスタ生成で確かめる(参照の整合性)。コード生成の出力からは取り除かれる。
    /// <c>[Exists(typeof(Item))]</c> は Item の主キー(1列)を、<c>[Exists(typeof(Item), nameof(Item.Code))]</c> は Item の Code 列を参照する。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class ExistsAttribute : Attribute
    {
        public ExistsAttribute(Type table, string? column = null)
        {
            Table = table;
            Column = column;
        }

        /// <summary>参照するテーブルの型(<c>[MemoryTable]</c> を付けた型)。</summary>
        public Type Table { get; }

        /// <summary>参照する列の名前。省くと、テーブルの主キー(1列)。</summary>
        public string? Column { get; }

        /// <summary>true なら、空欄のセルは「参照なし」として確かめない。</summary>
        public bool AllowEmpty { get; set; }
    }
}
