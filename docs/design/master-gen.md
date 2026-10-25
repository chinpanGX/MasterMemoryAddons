---
title: マスタ生成(CSV からバイナリを作る)
status: implemented
updated: 2026-10-25
---

# マスタ生成(CSV からバイナリを作る)

> 実装あり(`src/MasterMemory.Addons.Tools/MasterGen/`、CSV の読み込みは `Common/`)。ビルド・テスト(.NET SDK 10.0.401)と、
> `samples/masterdata.yaml` での `make code-gen` / `make master-gen` / `make master-gen-check` の実行を確認済み(2026-10-24)。
> データ段の検証([Unique] / [Exists] と db.Validate())も実装済み(2026-10-25。下の「データ段の検証」)。

CSV を検証し、MasterMemory が実行時に読むバイナリ(`new MemoryDatabase(bytes)` に渡すもの)に変換する。
実行形態は C(Tools の中でメモリ上にコンパイルする)に決定済み([decisions](decisions.md)。試作の結果は
[master-gen-prototype](master-gen-prototype.md))。以前の名前は Build(サブコマンド `build`、make の `build-data`)。

## 流れ

```
make master-gen    1. 定義からメモリ上でコード生成し、各出力先の source にある生成ソースと比べる。
                      違えば(古ければ)source を書き直す(下の「生成ソースが古いとき」)
                   2. 定義から全カラムと enum を読み、CSV を読んでヘッダと enum の入力名を確かめる
                   binary を書いた出力先ごとに:
                   3. 生成ソースを Roslyn でコンパイルする(MasterMemory / MessagePack の Source Generator もその場で動かす)
                   4. CSV の行を型に変換し、DatabaseBuilder に追加して Build() する
                   5. 平文のヘッダを付ける。encrypt: true なら MasterDataSchema.Hash を鍵にして中身を暗号化する(client のみ)
                   6. outputs[].binary と、その隣にマニフェスト(masterdata.manifest.json)を書く
                      (エラーが1つでもあれば、どの出力先にも書かない。書くときは、すべてを一時ファイルに書いてから置き換える)
```

- サブコマンドと Makefile のターゲットは `master-gen`。検証だけなら `master-gen --check`(`make master-gen-check`)。
- `make build` / `make test` は、このリポジトリのビルド・テスト(マスタ生成とは関係ない)。
- `binary` を書いていない出力先は、バイナリを作らない(生成ソースの比較と書き直しは行う)。
- 先にコード生成(`make code-gen`)を実行しておく必要はない。

### 生成ソースが古いとき

定義を変えてコード生成を忘れると、CSV のヘッダは新しい定義で確かめ、バイナリは古い生成ソースの型で作ってしまう(エラーにならない)。
これを防ぐため、次のようにする(D1)。

1. マスタ生成は、最初に定義からメモリ上でコード生成する(ファイルには書かない)。
2. その結果と、各出力先(`binary` のない出力先も含む)の `source` にある `*.cs` の中身を比べる。ファイルがない・余分なファイルがある・
   手で書き換えた、も含めて、少しでも違えば「生成ソースが古い」とする。
   - 作り直すと消えるファイル(生成しなくなったファイル・フォルダの `.meta`、手で置いたファイル)が残っていても「古い」とする
     (`--check` とコード生成の動きをそろえるため。2026-10-25 決定。review の E6)。
   - 生成するファイル・フォルダの Unity の `.meta` は比べない(作り直しても残るため)。改行コードの違い(CRLF / LF)は無視する。
3. 古ければ、コード生成と同じく `source` の中身を入れ替え(生成するファイルとフォルダの Unity の `.meta` は残す)、そのことを表示してから続ける。
   定義のエラー (構文エラー、テーブルの型が `class` でない)があれば、何も書かずに終了コード 1。
4. コンパイルには、メモリ上で作った生成ソース(書き直した後のファイルと同じ内容)を使う。

- これは「1回の実行で1つの機能」([structure](../dev/structure.md))の例外とする。
- 出力先を丸ごと入れ替えることがあるので、コード生成と同じ安全確認([config](config.md) の「出力先の扱い」)を行う。
  安全でなければ、何もせずに終了コード 2。
- Unity などが取り込む生成ソースと、バイナリが常に同じ定義から作られる。

## 設定 YAML の項目(決定)

```yaml
definitions: Definitions
csv: csv                              # CSV のディレクトリ。マスタ生成では必須
outputs:
  - name: unity
    target: client
    source: Client/Generated
    namespace: Sample.MasterData
    binary: Client/masterdata.bytes   # バイナリのパス。書いた出力先だけバイナリを作る
    encrypt: true                     # client のときだけ書ける。省略時は false
  - name: api-server
    target: server
    source: Server/Generated
    namespace: Sample.MasterData      # server でも必須
    binary: Server/masterdata.bytes
```

- `csv`、`outputs.<name>.binary`、`outputs.<name>.encrypt` は環境で上書きできる。`target` は上書きできない([config](config.md))。
- `encrypt` は `target: client` のときだけ書ける(サーバーのバイナリは利用者に配らないので、暗号化しない想定)。
- 暗号化の方式は既定の AES(`AesMasterDataCipher`)だけ。独自の実装を指定する項目は、必要になったら足す。
- `binary` が、いずれかの出力先の `source`、`definitions`、`csv` のディレクトリの中にあるとエラー
  (コード生成が `source` の中身を入れ替えるため。また入力と出力を混ぜないため)。

## コンパイル

- 試作 (`spike/InMemoryBuildSpike`、削除済み) と同じ方式。参照と C# のバージョンは target ごとに分ける(下の「参照」)。
- Source Generator の DLL(MasterMemory、MessagePackAnalyzer)は、パッケージから Tools の出力先にコピーして持つ。
- `build_property.RootNamespace` は Tools の内部の名前にする(生成される型は Tools がリフレクションで探すだけで、
  バイナリの中身には影響しない)。
- コンパイルエラーは、生成ソースのファイル名と行で報告して終了コード 1。
- 同じ中身の生成ソース (同じ target の出力先など)は、1回だけコンパイルして結果を使い回す。生成ソースは target・定義・namespace だけで決まるので、
  使い回すキーは target・スキーマのハッシュ・namespace にする(生成ソース全体のハッシュは取らない)。
- MasterMemory / MessagePack のバージョンは Tools に固定される。利用者のアプリは同じバージョンを使う(利用ガイドに書く)。
- 生成ソースから使えるのは、MasterMemory・MessagePack の型だけ(利用者のライブラリの型は参照できない)。
  本アドオンの属性と `using MasterMemory.Addons;` は、コード生成で取り除かれるので、コンパイルには関係しない。
- テーブルの型は `class` で、引数なしで作れること。`record`・`struct` は使えない([definition-model](definition-model.md))。

### 参照

| target | 参照するもの | C# のバージョン |
|---|---|---|
| client | `NETStandard.Library.Ref` 2.1.0 の参照アセンブリ、MasterMemory / MasterMemory.Annotations / MessagePack.Annotations の `netstandard2.0` 版、MessagePack の `netstandard2.1` 版 | 9 に固定(Unity と同じ) |
| server | 実行環境の .NET の共有フレームワーク(`Microsoft.NETCore.App` のディレクトリにあるもの)、Tools が持つ MasterMemory / MessagePack | 固定しない(最新) |

- どちらも、Roslyn・YamlDotNet・Tools・Core は参照しない。
- client 用は、Unity で通らない書き方(`netstandard2.1` にない API、C# 10 以降の構文)をマスタ生成のコンパイルエラーにする。
- client 用の参照は、Tools のビルドで出力先の `references/client/` にコピーする。`NETStandard.Library.Ref` は参照用のパッケージ
  (`DotnetPlatform`)で `PackageReference` にできないので、`PackageDownload` でダウンロードだけ行う。
- client 用のコンパイルには、`IsExternalInit` の polyfill を Tools が足す(`init` に必要。生成ソースに同じ名前のクラスがあれば足さない)。
  Unity では利用者が polyfill を置く必要がある(マスタ生成が通っても、置いていなければ Unity ではコンパイルできない)。
- コンパイルした型を読み込むときは、client 用も MasterMemory / MessagePack は Tools のもの(既定のコンテキスト)を使う。

## CSV とテーブルの対応(決定)

- ファイル名は `[MemoryTable("item")]` の名前: `item.csv`。
- `csv` のディレクトリの下を、**フォルダの中まで**探す。スプレッドシートのダウンロードのツール(例: SheetsDownloader)が
  スプレッドシートごとのフォルダ(`<csv>/<name>/<タブ名>.csv`)に書き出すため。`csv` の直下も1つのフォルダとして扱う。
- **水平分割**: 行を複数のファイルに分けられる。`item_<任意>.csv`(例: `item_weapon.csv`、`item_armor.csv`、`item_01.csv`)を、
  `item.csv` と合わせて**ファイル名の昇順**に結合する(旧 `normalize_csv.py` と同じ。`item.csv` が先になる)。
  - **`_` は水平分割の区切り。** ファイル名の最初の `_` より前がテーブル名になる(`item_weapon_01.csv` も item)。
  - そのため、**テーブル名に `_` は使えない**(`dropTable` のように付ける)。定義にあればマスタ生成のエラー(終了コード 1)。
  - 数字で分けるときは、桁を揃える(`_01`、`_02`、… `_10`)。文字列の昇順なので、揃えないと `_10` が `_2` より先になる。
  - `_` で始まる・終わる名前(`_item.csv`、`item_.csv`)は、どのテーブルにも一致しない。
  - 結合するのは**同じフォルダの中のファイルだけ**。同じテーブルの CSV が2つ以上のフォルダにあればエラー。
  - 分割したファイル同士は、ヘッダが列の順番まで完全に一致すること。一致しなければエラー。
  - テーブルに一致するファイルが1つもなければエラー。
- どのテーブルにも一致しない CSV は、警告を出して読まない。
- エラーなどに出すファイル名は、`csv` からの相対パス(区切りは `/`。例: `master/item_weapon.csv`)。

## CSV の読み込み(決定)

[pipeline](pipeline.md) の「CSV の形式」と「検証の項目」に従う。

- 文字コードは UTF-8(BOM はあってもなくてもよい)。区切りは `,`、`"` で囲んだ値の中の `,`・改行・`""` を扱う。
- 1〜2行目: コメント(読まない)/ 3行目: 型(参考。読まない)/ 4行目: ヘッダ / 5行目以降: データ。すべてのセルが空の行は読み飛ばす。
- **ヘッダの末尾の空の列は、列として扱わない。** スプレッドシートの書き出しでは、全行がいちばん長い行(コメントなど)の列数まで
  空のセルで埋められるため。その列のデータのセルに値があればエラー。ヘッダの途中の空の列は、定義にない列としてエラー。
  分割したファイル同士のヘッダは、末尾の空の列を除いて比べる。
  データの行は、名前のある列がすべてあれば、末尾の空の列の分はあってもなくてもよい(テキストエディタで、空の列を付けずに行を足した場合。review の R4)。
  名前のある列より少ない、またはヘッダの列より多い行はエラー。
- **ヘッダの列名は、C# のプロパティ名そのまま**(`ItemId`。大文字小文字を区別)。
- ヘッダの列は、定義の**全カラム**(`[ServerOnly]` / `[ClientOnly]` を含む)と過不足なく一致すること(順番は問わない)。
  対象 (client / server) にない列は読み飛ばす。全カラムは、定義ディレクトリのソースを構文として読んで得る。
- 変換できる型: `int`、`long`、`string`、`bool`、enum。`bool` は `TRUE` / `FALSE`、`True` / `False`、`1` / `0`。
  数値は前後の空白を許さない。ほかの型は扱わない(入力をわかりやすくするため。[definition-model](definition-model.md))。
- 空欄は型の既定値(`0` / `false` / `""` / enum の値 `0`)。ただし主キーの列と、値 `0` のメンバーがない enum の列はエラー。
- enum は `[InputName]` の名前だけを受け付ける。CSV の列で使う enum に `[InputName]` のないメンバーがあればエラー。
- エラーは「ファイル・行・列」で報告し(行はファイルの実際の行。コメントの行に引用符で囲んだ改行があっても、ずれない)、**エラーが1つでもあれば、どの出力先にも書き込まない**(終了コード 1)。

## 書き込み

- すべての出力先のバイナリとマニフェストを、先に一時ファイル(`<パス>.tmp`)に書き、すべて書けてから本来の名前に置き換える
  (2026-10-25 決定。review の E4・E5)。
- 一時ファイルの段階で失敗したら(ファイルのロック、権限など)、書いた一時ファイルを消して終了コード 1。元のバイナリとマニフェストは変わらない。
- 置き換えの段階で失敗したら、終了コード 1。新しくなったファイルを表示し、残りの一時ファイルは消す(置き換えはファイルの名前の変更なので、
  ここで失敗することはまれ)。
- **主キーの重複は Tools が検出する**(MasterMemory の `Build()` は検出しないことを確認した)。複合主キーは、主キーの列の値の組で比べる。

## 検証だけ行う `master-gen --check`

- 独立した Validate のサブコマンドは作らない。データ段の検証にもコンパイルと行の変換が要り、マスタ生成の前半と同じ処理になるため。
- `master-gen --check` は、マスタ生成と同じ検証(CSV の段・データの段・コンパイル)を行い、**何も書き込まない**(生成ソースも、バイナリも、マニフェストも)。
- 生成ソースが古いときは、ファイルを書き換えずに、メモリ上でコード生成した結果で検証を続け、「生成ソースが古い」と警告する(標準エラー)。
- 終了コードはマスタ生成と同じ。ただし、生成ソースが古ければ、CSV などにエラーがなくても **終了コード 1** にし、古い出力先の名前を表示する
  (2026-10-25 決定。CI でコード生成のし忘れを見逃さないため。review の R2)。
- エラーがなく、生成ソースも古くなければ「検証のみ(--check): エラーはありません」と表示する(終了コード 0)。
- これは「1つの機能に1つのサブコマンド」([structure](../dev/structure.md))の例外とする。

## データ段の検証

- 分ける前の全カラム(CSV)に対して、マスタ生成が1回だけ行う。定義の `[Unique]` / `[Exists]` を、CSV の段にエラーがなければ確かめる。
  詳細は [pipeline](pipeline.md) の「検証の項目」。
- 出力先ごとにバイナリを作った後、MasterMemory の `db.Validate()` も呼ぶ(`IValidatable<T>` を実装した型がなければ何もしない)。
- どちらかのエラーがあれば、どの出力先にもバイナリを書かない(終了コード 1)。`--check` でも同じ検証を行う。

## バイナリのヘッダ

- バイナリの先頭に平文のヘッダ(72 バイト。マジック、形式のバージョン、フラグ、スキーマの ID、データのハッシュ)を付ける。
  スキーマの ID は `MasterDataSchema.Hash`(鍵)から計算した、外に出してよい値([pipeline](pipeline.md) の「スキーマの ID」)。
  暗号化するのはヘッダより後ろだけ。並びは [pipeline](pipeline.md) の「バイナリのヘッダ」。書き込みは Core の `MasterDataBinary.Write`。
- スキーマのハッシュは、メモリ上のコード生成で計算した値をそのまま使う(コンパイルした型から `MasterDataSchema` を探さない。
  定義に同じ名前の型があっても取り違えない)。

## マニフェスト

- バイナリごとに **マニフェスト** を出す。配信で、どのアプリのビルドが読めるバイナリか、client と server が同じデータを使っているかを
  見分けるために使う([distribution](distribution.md))。
- バイナリの隣に、拡張子を `.manifest.json` に変えた JSON(`masterdata.bytes` → `masterdata.manifest.json`)。UTF-8(BOM なし)。
- `formatVersion`・`schemaId`・`dataHash`・`encrypted` は、書いたバイナリのヘッダを Core の `MasterDataBinary.ReadHeader` で読んで書く
  (ヘッダの並びを Tools で持たないため。review の R8)。

```json
{
  "formatVersion": 2,
  "schemaId": "8c2f17a0...",
  "dataHash": "f554d45c...",
  "encrypted": true,
  "size": 296,
  "createdAt": "2026-10-24T10:07:48Z"
}
```

| 項目 | 内容 |
|---|---|
| `formatVersion` | バイナリの形式のバージョン(ヘッダと同じ) |
| `schemaId` | スキーマの ID(その出力先の `MasterDataSchema.Hash` から計算した値。ヘッダと同じ)。鍵のハッシュそのものは書かない |
| `dataHash` | データのハッシュ(暗号化する前の中身の SHA-256。ヘッダと同じ) |
| `encrypted` | 暗号化したか |
| `size` | バイナリの長さ(バイト。ヘッダを含む) |
| `createdAt` | 作った日時(UTC の ISO 8601、秒まで)。1回のマスタ生成で作ったものは、どの出力先も同じ値 |

- `dataHash` は暗号化の乱数に関係しないので、client と server で、同じ CSV から作ったかどうかは `createdAt` で、
  データの中身が変わったかどうかは同じ出力先の `dataHash` の比較で見分けられる(client と server は列が違うので、`dataHash` は違う)。
- 2つの出力先のマニフェストのパスが重なる(`Out/data.bytes` と `Out/data.dat` など)、またはほかの出力先の `binary` と同じパスになる場合は、
  設定の誤り(終了コード 2)。
- 実装: `src/MasterMemory.Addons.Tools/MasterGen/MasterDataManifest.cs`。

## 終了コード

| 終了コード | 意味 |
|---|---|
| 0 | 成功 |
| 1 | 定義のエラー(構文エラー、テーブルの型が `class` でない)、コンパイルエラー、または CSV のエラー(どの出力先にもバイナリを書き込まない)。バイナリ・マニフェストを書き込めない(上の「書き込み」)。`--check` で生成ソースが古い |
| 2 | 引数・設定ファイルが不正(出力先が安全でない場合を含む) |

## 構成

- `src/MasterMemory.Addons.Tools/MasterGen/MasterGenCommand.cs` — 引数処理と、全体の流れ(設定の確認、生成ソースの比較と書き直し、CSV の読み込みと検証、出力先ごとのコンパイルとバイナリの作成、書き込み)
- `src/MasterMemory.Addons.Tools/MasterGen/InMemoryCompiler.cs` — 生成ソースのメモリ上のコンパイルと、Source Generator の実行(参照は target ごと)
- `src/MasterMemory.Addons.Tools/MasterGen/TableRowConverter.cs` — CSV の行を、コンパイルした型のインスタンスに変換する。主キーの重複の検出
- `src/MasterMemory.Addons.Tools/MasterGen/MasterDataBinaryBuilder.cs` — `DatabaseBuilder` をリフレクションで使う
- `src/MasterMemory.Addons.Tools/MasterGen/MasterDataManifest.cs` — マニフェストのパスと内容
- `src/MasterMemory.Addons.Tools/CodeGen/ConfigCodeGenerator.cs` — 設定の出力先ごとのコード生成(code-gen と共有する)
- `src/MasterMemory.Addons.Tools/CodeGen/GeneratedSourceComparer.cs` — 出力先の生成ソースが古いかの判定
- `src/MasterMemory.Addons.Tools/Common/CsvParser.cs` / `CsvTableReader.cs` — CSV の読み込みと水平分割の結合
- `src/MasterMemory.Addons.Tools/Common/DefinitionSchemaReader.cs` — 定義から全カラムと enum を読む(構文だけ。コード生成でパースした定義を使い回す)
- `src/MasterMemory.Addons.Tools/Common/CellConverter.cs` — セルの値の変換(空欄の規則、enum の入力名)

## 検証

`tests/MasterMemory.Addons.Tools.Tests` で、次を確認する。

- CSV の読み込み: 引用符・改行・BOM、コメント行と型の行の読み飛ばし、水平分割の結合の順番、ヘッダの不一致、ファイル名とテーブル名の対応(最初の `_` より前)、フォルダの中の CSV、複数のフォルダにあるときのエラー、ヘッダの末尾の空の列、コメントに改行があるときのヘッダの行の番号(`CsvParserTests` / `CsvTableReaderTests`)。
- セルの変換: 各型、bool の表記、空欄の既定値、主キーと値 0 のない enum の空欄、enum は入力名だけを受け付けること(`CellConverterTests`)。
- 定義の読み込み: 全カラム、enum の値と入力名、型の中に入れ子にした enum の名前、long に収まらない enum の値のエラー、テーブルの型が `class` でない(`record`・位置指定 record・`struct`)・テーブル名の重複・式で書いた enum の値のエラー(`DefinitionSchemaReaderTests`)。
- 設定: `csv` / `binary` / `encrypt`、環境での上書き、server の `encrypt` のエラー(`ConfigLoaderTests`)。
- 通しの実行: client 用のコンパイルで netstandard2.1 にない API がエラーになること、server 用でも Tools の依存(YamlDotNet)を参照できないこと。code-gen → master-gen で、client(暗号化)と server のバイナリを、コンパイルした型の `MemoryDatabase` で読み直して中身を確かめる。セルのエラー・ヘッダの不一致・`[InputName]` のない enum・主キーの重複で何も書かないこと。同じ target の2つの出力先に、同じバイナリを書くこと。テーブルの型の中に入れ子にした enum の列を変換できること(名前空間の直下の同名の enum と取り違えないこと)。スプレッドシートごとのフォルダの CSV を読めること、テーブル名に `_` があるときのエラー。設定の誤り(`binary` が `source` の中、`csv` なし、`source` が `csv` と重なる)。code-gen を実行していない・定義を変えた・生成ソースを手で書き換えたときに生成ソースを作り直すこと、`.meta` と改行コードの違いでは作り直さないこと。`--check` は何も書かず、古い生成ソースを警告して 1 を返し、CSV のエラーでも 1 を返すこと。作り直すときに `.meta` を残すこと。バイナリにヘッダが付き、server のバイナリを client のスキーマで読むと `SchemaMismatch` になること、定義に `MasterDataSchema` という名前の型があっても動くこと。マニフェストの中身(ヘッダと同じハッシュ、長さ、日時)と、マニフェストが重なるときのエラー(`MasterGenCommandTests`)。

→ [pipeline](pipeline.md) / [config](config.md) / [decisions](decisions.md)
