---
title: 生成・ビルド・配信の手順
status: draft
updated: 2026-10-25
---

# 生成・ビルド・配信の手順

設計: [code-generation](../design/code-generation.md) / [master-gen](../design/master-gen.md) / [distribution](../design/distribution.md)。

## 1. クライアント用/サーバー用ソースの出力

### 設定ファイルを使う (通常はこちら)

定義の場所と出力先を YAML に書きます (形式は [config](../design/config.md)、例は `samples/masterdata.yaml`)。

```yaml
definitions: Definitions
outputs:
  - name: unity
    target: client
    source: ../Client/Assets/MasterData/Generated
    namespace: Sample.MasterData
  - name: api-server
    target: server
    source: ../Server/Api/MasterData/Generated
    namespace: Sample.MasterData
  - name: battle-server
    target: server
    source: ../Server/Battle/MasterData/Generated
    namespace: Sample.MasterData
environments:
  dev:
    outputs:
      unity:
        source: ../Client/Assets/MasterData/GeneratedDev
```

```
make code-gen                                   # CONFIG を省くと samples/masterdata.yaml
make code-gen ENV=dev
make code-gen CONFIG=path/to/masterdata.yaml
```

Makefile を使わない場合は、Tools を直接実行します。

```
dotnet run --project src/MasterMemory.Addons.Tools -- code-gen --config samples/masterdata.yaml [--env dev]
```

自分のプロジェクトでは、このリポジトリの `Makefile` をコピーし、`CONFIG` と `TOOLS` を書き換えて使えます。

- パスは設定ファイルのあるディレクトリからの相対パス。
- `ENV`(`--env`)を付けると、`environments` のその環境に書いた項目だけが上書きされる。出力先は `name` で指定する。
- **`source` のディレクトリは生成のたびに中身を丸ごと入れ替える**(生成するファイルとフォルダの Unity の `.meta` だけは残す)。
  生成専用のディレクトリを指定し、手書きのファイルを置かないこと。設定ファイルや定義のディレクトリを含む場所などを指定すると、何も消さずにエラーになる。
- 定義にエラー(構文エラー、テーブルの型が `class` でない)があると、どの出力先にも書き込まずに終了コード 1 になる。
- 出力先には `namespace` が必須(client / server とも)。その名前空間で、スキーマのハッシュ `MasterDataSchema.Hash` を持つ `MasterDataSchema.cs` も置かれる。値は定義を変えると変わり、client では暗号化の鍵も兼ねる。バイナリを読むときに使う(下の「3. バイナリを作る」の「アプリで読み込む」)。
  **鍵なので、ログやサーバーへの送信、CDN のパスなどでアプリの外に出さない。** 外に出すときは `MasterDataBinary.ComputeSchemaId(MasterDataSchema.Hash)`(スキーマの ID)を使う。

### 引数だけで試す

```
dotnet run --project src/MasterMemory.Addons.Tools -- code-gen --input samples/Definitions --target client --output out/client
dotnet run --project src/MasterMemory.Addons.Tools -- code-gen --input samples/Definitions --target server --output out/server
```

- 入力のサブディレクトリ構造がそのまま出力にも使われる。
- 定義にエラー(構文エラー、テーブルの型が `class` でない)があると、そのファイルを報告して終了コード 1 になる。
- 出力先は生成物なので、手で編集しない。

## 2. 出力を取り込む

設定ファイルを使う場合、ソースは各プロジェクトに直接置かれる。引数だけで試した場合は、次のように取り込む。

- **サーバー**: `out/server` のソースを、MasterMemory の NuGet パッケージを参照するプロジェクトに含める。
- **Unity**: `out/client` のソースを `Assets` 以下に置く。MasterMemory は NuGetForUnity で導入する (Unity 2022.3.12f1
  以上)。

MasterMemory の Source Generator が、`[MemoryTable]` の付いた型からデータベースのコードを生成する。

## 3. バイナリを作る

CSV から、MasterMemory が読むバイナリを作ります(マスタ生成。設計は [master-gen](../design/master-gen.md))。
生成ソースが定義と合っていなければ(1. のコード生成を忘れた、定義を変えたなど)、先にコード生成をやり直してからバイナリを作ります。

設定 YAML に、CSV のディレクトリと、出力先ごとのバイナリのパスを足します。

```yaml
csv: csv
outputs:
  - name: unity
    target: client
    source: ../Client/Assets/MasterData/Generated
    namespace: Sample.MasterData
    binary: ../Client/Assets/MasterData/masterdata.bytes
    encrypt: true                  # client のときだけ書ける
  - name: api-server
    target: server
    source: ../Server/Api/MasterData/Generated
    namespace: Sample.MasterData
    binary: ../Server/Api/MasterData/masterdata.bytes
```

```
make master-gen            # CSV を検証してバイナリを作る
make master-gen ENV=dev
make master-gen-check      # 検証だけを行い、何も書き込まない(生成ソースも、バイナリも、マニフェストも)
```

Makefile を使わない場合は `dotnet run --project src/MasterMemory.Addons.Tools -- master-gen --config <file> [--env <name>] [--check]`。

- 生成ソースを書き直したときは「コード生成をやり直しました」と表示されます。`--check` のときは書き直さずに警告を出し、
  CSV にエラーがなくても終了コード 1 になります(CI でコード生成のし忘れに気づけるように)。
- 生成するファイル・フォルダの Unity の `.meta` があっても、書き直しの理由にはなりません(書き直すときも残ります)。
  生成しなくなったファイルの `.meta` や、手で置いたファイルが `source` に残っていると、生成ソースが古いとみなし、書き直して消します。
- バイナリとマニフェストは、すべてを一時ファイル(`.tmp`)に書いてから置き換えます。書き込みに失敗したときは終了コード 1 で、元のファイルはそのまま残ります。

- バイナリの隣に、マニフェスト(`masterdata.bytes` なら `masterdata.manifest.json`)も書かれる。スキーマの ID・データのハッシュ・
  作った日時などが入っていて、配信のときに、どのアプリのビルドが読めるバイナリかを見分けるのに使える(形式は [master-gen](../design/master-gen.md) の「マニフェスト」)。

### CSV の書き方

- ファイル名はテーブル名(`[MemoryTable("item")]` なら `item.csv`)。スプレッドシートのタブ名をテーブル名にして、タブ名どおりに書き出せばよい。
- **テーブル名に `_` は使えない**(`dropTable` のように付ける)。`_` は水平分割の区切りに使う。
- **水平分割**: 行を複数のファイル(タブ)に分けるときは `item_weapon.csv`、`item_armor.csv` のように `_` で区切る。
  `item.csv` と合わせて、ファイル名の昇順に結合される。数字で分けるときは桁を揃える(`item_01`、`item_02`、… `item_10`)。
  分割したファイルのヘッダは、列の順番まで同じにする。
- `csv` のディレクトリの下のフォルダも探す(スプレッドシートごとにフォルダを分けてよい)。同じテーブルの CSV は1つのフォルダにまとめる。
- 1〜2行目はコメント、3行目は型(参考)、4行目がヘッダ、5行目以降がデータ。ヘッダより右の空の列(コメントの幅で埋められた列)は、データの行に付いていなくてもよく、無視される。
- ヘッダの列名は C# のプロパティ名そのまま(大文字小文字も一致させる)。`[ServerOnly]` / `[ClientOnly]` の列も含め、定義の全カラムを書く。
- 空欄は型の既定値(`0` / `false` / `""`)になる。主キーの列と、値 `0` のメンバーがない enum の列は空欄にできない。
- enum の列には `[InputName]` の名前を書く。
- 例は `samples/csv/`。

### エラーのとき

- CSV のエラーは「ファイル 行 列: 内容」で表示され、**どの出力先にもバイナリを書かない**(終了コード 1)。
- 主キーの重複もエラーになる。
- 定義の `[Unique]` / `[Exists]` に違反する値、MasterMemory の `db.Validate()` の失敗もエラーになる(書き方は [defining-master-data](defining-master-data.md) の「データの検証」)。
- client 用の生成ソースは、Unity と同じ制約(`netstandard2.1` の API、C# 9)でコンパイルする。定義で使ったメソッドなどが Unity で使えないものなら、ここでエラーになる。
  server 用も、.NET の標準のライブラリと MasterMemory / MessagePack しか参照できない。

### アプリで読み込む

- アプリが参照する MasterMemory / MessagePack は、Tools と同じバージョンにする(今は MasterMemory 3.0.4、MessagePack 3.1.12)。
  バイナリは Tools が持つバージョンで作られるため。
- `MemoryDatabase` は、生成ソースを含むプロジェクトの `RootNamespace` に生成される。

- バイナリには、平文のヘッダ(スキーマの ID など)が付いている。`MasterMemory.Addons.Core` の `MasterDataBinary.Read` に、
  バイナリとアプリの `MasterDataSchema.Hash` を渡すと、ヘッダを確かめ、暗号化されていれば復号して、`MemoryDatabase` に渡すバイト列を返す。
  client も server も同じ書き方でよい。

```csharp
using MasterMemory.Addons;

var bytes = /* Addressables やファイルから読む */;
var database = new MemoryDatabase(MasterDataBinary.Read(bytes, MasterDataSchema.Hash));
```

- 読めないときは `MasterDataFormatException` になる。`Reason` で理由が分かる:
  `SchemaMismatch`(アプリとバイナリの定義が違う。アプリかデータを作り直す)、`Corrupted`(壊れている)、
  `NotMasterData` / `UnsupportedVersion`(本アドオンのバイナリでない、形式が違う)。
- 独自の暗号化を使うときは、`MasterDataBinary.Read(bytes, MasterDataSchema.Hash, new MyCipher())` のように実装を渡す。

## 4. 配信

本パッケージには配信の仕組みはない。CDN への配置とダウンロードは、Addressables などを使って利用者のプロジェクトで行う
([distribution](../design/distribution.md))。

- 取得した `byte[]` を、上の「アプリで読み込む」のとおり `MasterDataBinary.Read` に渡す。
- CDN の置き場所は、スキーマの ID(マニフェストの `schemaId`、または `MasterDataBinary.ComputeSchemaId(MasterDataSchema.Hash)`)ごとに分ける。
  古いアプリが残る間は、古いスキーマのバイナリも置いておく。
- パスやログに、スキーマのハッシュ(`MasterDataSchema.Hash`。暗号化の鍵)を使わない。
