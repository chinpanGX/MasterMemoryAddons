// コード生成(code-gen)とマスタ生成(master-gen)に渡すサンプル定義(このファイルはどのプロジェクトにも含まれない)。
// Unity にソースで入れるため、C# 9 で通る書き方(ブロック namespace)にしている。
using MasterMemory.Addons;
using MasterMemory;
using MessagePack;

namespace Sample.Definitions
{
    public enum ItemCategory
    {
        [InputName("武器")] Weapon = 1,
        [InputName("防具")] Armor = 2,
    }

    [MemoryTable("item"), MessagePackObject(true)]
    public class Item
    {
        [PrimaryKey]
        public int ItemId { get; init; }

        [SecondaryKey(0), NonUnique]
        public ItemCategory Category { get; init; }

        public string Name { get; init; } = "";

        [ClientOnly]
        public string IconAddress { get; init; } = "";

        [ServerOnly]
        public int SellPrice { get; init; }
    }

    // サーバーだけが使うテーブルの例(クライアント用の出力には含まれない)
    [ServerOnly, MemoryTable("dropTable"), MessagePackObject(true)]
    public class DropTable
    {
        [PrimaryKey]
        public int DropTableId { get; init; }

        // データ段の検証の例: item テーブルの主キー(ItemId)にある値だけを書ける(マスタ生成で確かめる)
        [Exists(typeof(Item))]
        public int ItemId { get; init; }

        public int Weight { get; init; }
    }
}
