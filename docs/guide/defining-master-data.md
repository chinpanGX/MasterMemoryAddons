---
title: マスターデータの定義方法
status: draft
updated: 2026-10-25
---

# マスターデータの定義方法

設計: [design/definition-model](../design/definition-model.md)。サンプル: `samples/Definitions/Item.cs`。

## 書き方

1. `class` に `[MemoryTable("名前")]` と `[MessagePackObject(true)]` を付ける。名前に `_` は使えない(CSV の水平分割の区切りに使うため。`dropTable` のように付ける)。
2. 主キーに `[PrimaryKey]`、検索用の列に `[SecondaryKey(n)]`(重複を許すなら `[NonUnique]`)を付ける。
3. プロパティは `{ get; init; }` にする。
   **テーブルの型は `class` だけ**。`record`(`record class`・`record struct`・位置指定 record も)や `struct` は使えない。書くと、コード生成とマスタ生成で定義のエラーになる。
   行は主キーが同じなら同じものとして扱い、比べるときは `a.ItemId == b.ItemId` のようにキーで比べる。
   列の型は `int` / `long` / `string` / `bool` / enum だけ。
4. クライアントだけが使う列に `[ClientOnly]`、サーバーだけが使う列に `[ServerOnly]` を付ける。型全体に付けると、その側だけの表になる。

## キーと enum

- 複合キーは `[PrimaryKey(keyOrder: 0)]`, `[PrimaryKey(keyOrder: 1)]` のように書く。
- enum は C# で定義し、スプレッドシートに書く名前を `[InputName("武器")]` で付ける(CSV にはこの名前だけを書ける)。CSV の列で使う enum は、すべてのメンバーに付ける。値 `0` のメンバーがない enum の列は空欄にできない
  (詳細は [definition-model](../design/definition-model.md))。

## データの検証

- 列の値が重ならないことを確かめたいときは `[Unique]` を付ける。
- 別のテーブルにある値だけを書けるようにしたいときは `[Exists(typeof(Item))]`(Item の主キー)か `[Exists(typeof(Item), nameof(Item.Code))]`(Item の Code 列)を付ける。
  空欄を「参照なし」として許すときは `AllowEmpty = true` を足す。
- どちらもマスタ生成(`make master-gen`)が確かめ、違反があれば「ファイル 行 列」でエラーになる。生成ソースには残らない。
- MasterMemory の `IValidatable<T>` も使える(マスタ生成が `db.Validate()` を呼ぶ)。ただし検証のコードが client 用の出力にも入るので、
  `[ServerOnly]` の列・テーブルを参照する検証は書けない。できるだけ属性で書く。
- 例は `samples/Definitions/Item.cs` の `DropTable.ItemId`。詳細は [pipeline](../design/pipeline.md) の「検証の項目」。

## Unity に入れるソースとしての注意

- `namespace X { ... }` のブロック形式で書く (ファイルスコープは不可)。
- `required` は使わない。
- `init` を使う場合、Unity 側に `IsExternalInit` の polyfill が必要。
- `record struct` は使わない(行の中の値型にも)。
- nullable 型(`int?` など)は使わない。CSV の空欄は型の既定値(`0` / `false` / `""` / enum の値 `0`)として読み込まれる(主キーの空欄はエラー)。

## 注意

- `ServerOnly` の列・型は、クライアント用の出力に含まれない。
- コード生成は構文だけを見るので、定義の型名や属性名の綴りの間違いは、マスタ生成(`make master-gen`)のコンパイルか、出力を各プロジェクトでビルドしたときに分かる。
