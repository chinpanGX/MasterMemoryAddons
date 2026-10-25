# MasterMemory.Addons

[Cysharp/MasterMemory](https://github.com/Cysharp/MasterMemory) の **非公式アドオン(ヘルパー)** です。
Unity のクライアントと C# のサーバーの両方で、同じ定義からマスターデータを扱えるようにします。
MasterMemory の API を包んだり置き換えたりはしません。

> Cysharp および MasterMemory の公式のものではありません。

## できること

- **1つの定義から、クライアント用・サーバー用のソースを作る(コード生成)**
  C# の定義の列や型に `[ServerOnly]` / `[ClientOnly]` を付けると、それぞれの側に要るものだけを残したソースを出力します。
  出力したソースは、各プロジェクトで MasterMemory の Source Generator がデータベースのコードにします。
- **CSV を検証して、バイナリを作る(マスタ生成)**
  スプレッドシートから書き出した CSV を、定義に合わせて検証(型、enum の入力名、主キーの重複、`[Unique]` / `[Exists]`)し、
  クライアント用・サーバー用の MasterMemory のバイナリを作ります。
- **バイナリのヘッダと暗号化**
  バイナリには平文のヘッダ(スキーマの ID、データのハッシュ)が付き、アプリとデータの定義の食い違いや壊れたデータを読み込む前に見分けられます。
  クライアント用のバイナリは暗号化できます。バイナリごとに、配信に使えるマニフェスト(`masterdata.manifest.json`)も出します。

## 定義の例

```csharp
using MasterMemory;
using MasterMemory.Addons;
using MessagePack;

[MemoryTable("item"), MessagePackObject(true)]
public class Item
{
    [PrimaryKey]
    public int ItemId { get; init; }

    public string Name { get; init; } = "";

    [ClientOnly]   // クライアント用の出力にだけ残る
    public string IconAddress { get; init; } = "";

    [ServerOnly]   // サーバー用の出力にだけ残る
    public int SellPrice { get; init; }
}
```

## 必要なもの

- .NET 10 SDK
- make(Windows では scoop / choco で入れるか、WSL を使う)
- Unity 2022.3.12f1 以上(MasterMemory の要件)

## 使い方

```
make build                # ビルド(dotnet build MasterMemory.Addons.slnx)
make test                 # テスト(dotnet test MasterMemory.Addons.slnx)
make code-gen             # samples/masterdata.yaml の出力先に、クライアント用・サーバー用のソースを置く
make master-gen           # samples/csv を検証して、バイナリを作る
make master-gen-check     # 検証だけを行い、何も書き込まない
```

自分のプロジェクトで使うときは、`Makefile` をコピーして `CONFIG`(設定ファイル)と `TOOLS` を書き換えます。
導入方法(配布の形態)は、まだ決まっていません。

詳しくは次のドキュメントを参照してください。

- [はじめに](docs/guide/getting-started.md)
- [マスターデータの定義方法](docs/guide/defining-master-data.md)
- [生成・ビルド・配信の手順](docs/guide/build-and-distribute.md)
- [設計書・開発ルールの索引](docs/README.md)

## フォルダ構成

| フォルダ | 内容 |
|---|---|
| `src/MasterMemory.Addons.Attributes/` | 定義に付ける属性(`netstandard2.1`) |
| `src/MasterMemory.Addons.Core/` | バイナリの読み書き・暗号化など、Unity とサーバーとツールで共通に使う処理(`netstandard2.1`) |
| `src/MasterMemory.Addons.Tools/` | コード生成(`code-gen`)とマスタ生成(`master-gen`)の CLI(`net10.0`) |
| `tests/` | テスト(xunit) |
| `samples/` | サンプルの定義・CSV・設定ファイルと、生成したソースを取り込むプロジェクト |
| `docs/` | 設計書・利用ガイド・開発ルール |

## ライセンス

[MIT License](LICENSE)

MasterMemory・MessagePack for C# などの依存ライブラリは、それぞれのライセンスに従います。
