---
title: マスターデータ定義モデル
status: draft
updated: 2026-10-25
---

# マスターデータ定義モデル

## 方針

- 定義元は **C# のソース**。テーブルの型は **`class`** で書く(`record`・`struct` は使えない。下の「型についての制約」)。YAML は使わない。
- [MasterMemory](https://github.com/Cysharp/MasterMemory) の属性 (`[MemoryTable]`, `[PrimaryKey]`, `[SecondaryKey]`,
  `[NonUnique]`)と、MessagePack for C# の `[MessagePackObject(true)]` を付ける (MasterMemory はシリアライズに MessagePack
  を使う)。
- クライアントとサーバーで列が異なる場合は、 **`[ServerOnly]` / `[ClientOnly]`** で印を付ける。印のないものは共通。
- 属性は `MasterMemory.Addons.Attributes`(`netstandard2.1`、名前空間 `MasterMemory.Addons`)が提供する。

## Only 属性の規則

| 付ける場所                                              | クライアント用の出力                     | サーバー用の出力                         |
|---------------------------------------------------------|------------------------------------------|------------------------------------------|
| プロパティ / フィールド                                 | `ServerOnly` は削除、`ClientOnly` は残す | `ClientOnly` は削除、`ServerOnly` は残す |
| 位置指定 record の引数(`[property: ServerOnly] int X`)※ | 同上                                     | 同上                                     |
| 型(class / struct / record)                             | `ServerOnly` の型は丸ごと削除            | `ClientOnly` の型は丸ごと削除            |

- ※ テーブルの型は `class` + `{ get; init; }`(下の例)で書く。**テーブルの型に `record`・`struct` は使えない**(位置指定 record も)。
  位置指定 record の引数の規則は、テーブルではない型 (`[MemoryTable]` を付けない型)を `record` で書いた場合のもの。
- 残した側の Only 属性は、出力から取り除く。
- 出力から `using MasterMemory.Addons;` も取り除く。生成物は `MasterMemory.Addons.Attributes` を参照しなくてよい。
- `ServerOnly` の列・型がクライアント用の出力に残らないことを、テストで保証する。

## キー

- 複合主キー・複合セカンダリキーは MasterMemory の機能を使う (`[PrimaryKey(keyOrder: 0)]`, `[PrimaryKey(keyOrder: 1)]`)。
- キーは既定で一意。重複を許す場合は `[NonUnique]`。

## enum

C# の `enum` を直接定義する。スプレッドシートでの入力のしやすさのため、メンバーに `[InputName]` で入力名を付ける。

```csharp
public enum ItemCategory
{
    [InputName("武器")] Weapon = 1,
    [InputName("防具")] Armor = 2,
}
```

- CSV には入力名 (`武器`)を書く。値は enum の数値 (`1`)になる。
- CSV に書けるのは入力名だけ。C# のメンバー名 (`Weapon`)や数値 (`1`)は受け付けない。
- CSV の列で使う enum は、**すべてのメンバーに `[InputName]` を付ける**(`None = 0` のような内部用の値にも)。付いていないと Validate でエラーになる。
- 値 `0` のメンバーがない enum の列は、空欄にできない(Validate でエラー)。
- `InputName` はコード生成の出力から取り除かれる。

## 型についての制約

[MasterMemory の README](https://github.com/Cysharp/MasterMemory) に基づく。

- **テーブルの型は `class` だけ** (2026-10-25 に決定)。`record`(`record class`・`record struct`・位置指定 record を含む)・`struct`・`interface` は、
  コード生成とマスタ生成で定義のエラー (終了コード 1)になる。
  - 理由: 行は Id (主キー)が同じなら同じものとみなす使い方を前提にする。`record` の値の比べ方 (全プロパティを比べる)はこれと合わず、
    `Equals` / `GetHashCode` が遅くなり、生成されるコードも増える。MasterMemory の README は `record` を推奨しているが、本アドオンでは使わない。
- **`record struct` は使わない**(行の中の値型にも)。MasterMemory の README に記載がないため。
- **nullable 型(`int?` など)は使わない。** 値がないことを表したいときも既定の値(`0` など)を入力してもらう。
- **CSV の列で使える型は `int` / `long` / `string` / `bool` / enum だけ。** 入力をわかりやすくするため、`float` / `double`・ほかの整数・
  `DateTime`・配列・行の中の値型 (入れ子)は扱わない (2026-10-24 に決定)。
- enum の値は `long` に収まること。収まらない値 (`ulong` の大きな値、`long.MaxValue` の次の省略した値)は定義のエラー。CSV の列で使わない enum も確かめる。
- データは readonly。`{ get; init; }` を使う。Unity では `System.Runtime.CompilerServices.IsExternalInit` の polyfill が必要。
- `required` は C# 11 の機能で、Unity では使えない。
- Unity の最小バージョンは 2022.3.12f1 (MasterMemory の Source Generator の要件)。

## 定義ソースの書き方 (Unity にソースで入れる場合)

生成物はソースのまま Unity に入るため、 **Unity のコンパイラ (C# 9 相当)で通る書き方**にする。
ブロック形式の `namespace` を使い (ファイルスコープ `namespace X;` は C# 10)、`required` や raw string は使わない。

## 例

`samples/Definitions/Item.cs` と同じ内容 (サーバー専用のテーブル `DropTable` は省略)。

```csharp
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
}
```

## 検証の宣言 (実装済み)

- 行をまたぐ検証は、定義に属性で宣言する。検証は、client / server に分ける前の全カラムに対して、マスタ生成が1回だけ行う。
  検証の属性はコード生成の出力から取り除く。
- 属性は `[Unique]`(列の一意性)と `[Exists]`(参照の整合性)。詳細は [pipeline](pipeline.md) の「検証の項目」。

```csharp
[ServerOnly, MemoryTable("dropTable"), MessagePackObject(true)]
public class DropTable
{
    [PrimaryKey]
    public int DropTableId { get; init; }

    [Exists(typeof(Item))]                                             // item の主キーにある値だけ
    public int ItemId { get; init; }

    [Exists(typeof(Item), nameof(Item.Name), AllowEmpty = true)]       // 空欄は参照なし
    public string ItemName { get; init; } = "";
}
```

→ [decisions.md](decisions.md) / 実装: [code-generation.md](code-generation.md)
