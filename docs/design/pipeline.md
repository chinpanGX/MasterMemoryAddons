---
title: データパイプライン(検証・ビルド)
status: draft
updated: 2026-10-25
---

# データパイプライン (検証・ビルド)

基本要件は旧 `master-data-pipeline`(Python + YAML)と同じ。本リポジトリでは、それを C# 側に作り直す。 **YAML
の中間データは使わない。** 定義は C# の POCO が唯一の正とする ([definition-model](definition-model.md))。

## 旧 master-data-pipeline から引き継ぐもの

| 項目                        | 旧の仕様                                                                                                              | 本リポジトリ                                                                                                                        |
|-----------------------------|-----------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------|
| データの元                  | Google スプレッドシート(`download_sheets.py`、設定は `config.yaml`、認証ファイルは `credentials/`)。CSV の手置きも可  | **CSV を入力として受け取るだけ。** ダウンロードは本リポジトリでは行わない(下記)                                                   |
| ダウンロード                | タブ名 = `csv/{タブ名}.csv`。タブ名は大文字小文字を区別して照合                                                       | 扱わない(下記)                                                                                                                     |
| CSV の形式                  | 1〜2行目: コメント(無視)/ 3行目: 型(参考のみ。型の正は定義側)/ **4行目: ヘッダ**(列名は定義と一致)/ 5行目以降: データ | 引き継ぐ。ヘッダの列名は C# のプロパティ名(旧は snake_case の `fields[].name`)                                                     |
| 分割テーブル                | `input_csv` の末尾 `*`(例: `sample_item_master_*`)で複数ファイルをファイル名昇順に結合。ヘッダは列順まで一致          | 考え方は引き継ぐ。ファイル名は `item_<任意>.csv`。結合は同じフォルダの中だけ([master-gen](master-gen.md))                                   |
| クライアント/サーバーの区別 | CSV には印を付けず、YAML の `targets`(列単位・テーブル単位、既定は両方)                                               | **C# の `[ServerOnly]` / `[ClientOnly]` に置き換える**                                                                              |
| 出力                        | `masterdata.bytes`(MasterMemory、暗号化)。サーバー(MagicOnion)も同じ                                                  | クライアント用/サーバー用のバイナリ                                                                                                 |
| 範囲外                      | Rust 向けの `*.rs` / `*.json`(API サーバー用)                                                                         | 扱わない                                                                                                                            |
| サーバーでの利用            | MagicOnion は `masterdata.bytes` を読み込む                                                                           | 提供するのは**読み込み(`MemoryDatabase` を作る)まで**。RDB に入れるなど、その後の扱いはサーバー側の判断で、本リポジトリでは扱わない |

## 流れ

```
C# 定義(全カラム) ─コード生成─▶ client / server 用のソース(Generated/)
                                         │
(範囲外: スプレッドシート ─ダウンロード─▶) csv/*.csv ─マスタ生成(検証 → バイナリ)─▶ client / server のバイナリ ─配信─▶ CDN
                                         (MasterMemory の DatabaseBuilder を使う)
```

機能の名前は「コード生成」と「マスタ生成」(2026-10-24 に `generate` / `build` から変更)。

| 段 | 内容 | 状態 |
|---|---|---|
| コード生成(`code-gen`) | 定義を client / server に振り分けたソースを出す([code-generation](code-generation.md)) | 実装あり(ビルド・テスト確認済み) |
| マスタ生成(`master-gen`) | 生成ソースが古ければコード生成をやり直し、CSV を検証して、行を型に変換してバイナリを作る([master-gen](master-gen.md)) | 実装あり(ビルド・テスト確認済み。データ段の検証を含む) |
| 検証だけ(`master-gen --check`) | マスタ生成と同じ検証を、何も書き込まずに行う。独立した Validate のサブコマンドは作らない | 実装あり(ビルド・テスト確認済み。データ段の検証を含む) |
| 配信 | CDN への配置とダウンロード([distribution](distribution.md)) | 範囲外(Addressables と利用者のプロジェクトが受け持つ) |
| 暗号化・ヘッダ | スキーマのハッシュの定数の出力(コード生成)と、ヘッダの読み書き・暗号化・復号のランタイム(下記)。マスタ生成で使う | 実装あり(ビルド・テスト確認済み) |

## スプレッドシートのダウンロード (範囲外)

**本リポジトリでは、スプレッドシートのダウンロードは行わない** ([decisions](decisions.md))。入力は、設定 YAML の `csv` のディレクトリにある CSV だけ。
CSV をどう用意するか (ダウンロードのツールを使う、手で置くなど)は、利用者が決める。Tools のサブコマンド・Makefile の段・設定 YAML の項目は持たない。

- 先頭3行の除去と水平分割の結合は、本リポジトリのマスタ生成がメモリ上で行う (今の `CsvTableReader`)。エラーを元のファイル・行で出すため。
- CSV の形式は [master-gen](master-gen.md) の「CSV とテーブルの対応」「CSV の読み込み」。シートのタブ名を `item`、`item_weapon` のようにして
  タブ名どおりのファイル名で保存すれば、そのまま読める。

## 検証の項目

二段に分ける。どちらもマスタ生成が行い、`master-gen --check` は書き込まずに同じ検証を行う ([master-gen](master-gen.md))。

**1. CSV の段 (マスタ生成で実装済み)**

- ヘッダ (4行目)が、定義のカラム (`ServerOnly` / `ClientOnly` を含む全カラム)と一致する。分割ファイルはヘッダが完全に一致する。
- 型に変換できる (`int`, `long`, `string`, `bool`, `enum`)。`bool` は `TRUE`/`FALSE`、`True`/`False`、`1`/`0` を受け付ける。
- **空欄のセルは、型の既定値として扱う**(`int` / `long` は `0`、`bool` は `false`、`string` は `""`、enum は値 `0`)。エラーにはしない。
  - 例外: 主キー(`[PrimaryKey]`)の列の空欄はエラーにする。
  - 例外: enum に値 `0` のメンバーがない場合、その enum の列の空欄はエラーにする (定義にない値を作らないため)。
- enum の入力名が、enum の定義に存在する (下記)。
- CSV の列で使う enum は、すべてのメンバーに `[InputName]` が付いている。付いていないメンバーがあればエラー (下記)。

**2. データの段 (マスタ生成で実装済み。2026-10-25)**

- 一意性 (キー以外の列)・参照の整合性など、行をまたぐ検証。旧の `validate.unique` / `check_relation` に相当する。
- **client / server に分ける前の、全カラム (CSV)に対して1回だけ行う。** `[ServerOnly]` の列やテーブルを参照する検証も書ける。
- **検証は定義に属性で宣言し、マスタ生成が検証する。** 検証の属性は、`[InputName]` と同じくコード生成の出力から取り除く
  (Unity やサーバーには入らない)。属性は `MasterMemory.Addons.Attributes` にある。

| 属性 | 意味 |
|---|---|
| `[Unique]` | 列の値が、テーブルの中で重ならない。空欄は型の既定値として比べる (空欄が2つあれば重複) |
| `[Exists(typeof(Item))]` | 列の値が、Item の主キー (1列)にある。主キーが複合なら列を指定する |
| `[Exists(typeof(Item), nameof(Item.Code))]` | 列の値が、Item の Code 列にある (2つ目の引数は文字列リテラルでもよい) |
| `AllowEmpty = true`(`[Exists]` の名前付き引数) | 空欄のセルは「参照なし」として確かめない |

- 値は定義の型で比べる (`int` と `long` は数値として同じに扱い、enum は入力名を値に直して比べる)。参照元と参照先の列の型が合わなければ定義のエラー。
- 使える型 (`int` / `long` / `string` / `bool` / enum)以外の列に付ける、参照先のテーブル・列がない、引数の書き方が違う、は定義のエラー (終了コード 1)。
- 検証のエラーは「ファイル 行 列: 内容」で表示し、どの出力先にもバイナリを書かない (終了コード 1)。行の変換のエラー (型に変換できないなど)と一緒に表示する。
- **MasterMemory の `db.Validate()` も呼ぶ** (2026-10-25 決定)。出力先ごとに、作ったバイナリから `MemoryDatabase` を作って呼び、失敗すれば
  `FormatFailedResults()` を表示して終了コード 1。`IValidatable<T>` を実装した型がなければ何もしない。
  - 本アドオンが勧めるのは属性での検証。`IValidatable<T>` は定義に書くと検証のコードが client 用の出力にも入り、`[ServerOnly]` の列・テーブルを
    参照すると client でコンパイルできないので、使うかどうかは利用者が判断する。
- 主キーの一意性は、マスタ生成が確かめている (MasterMemory の `Build()` は検出しない)。
- 実装: `src/MasterMemory.Addons.Tools/MasterGen/DataValidator.cs`、`Common/ColumnValue.cs`、`MasterDataBinaryBuilder.Validate`。属性の読み込みは `Common/DefinitionSchemaReader.cs`。

## 旧の制限の扱い

| 旧は未対応       | 本リポジトリ                                                                                                                                               |
|------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 複合主キー       | **対応する。** MasterMemory が `[PrimaryKey(keyOrder: n)]` で複合キーをサポート                                                                            |
| 複合ユニーク制約 | **対応する。** 複合の `[SecondaryKey]` は、`[NonUnique]` を付けなければ一意                                                                                |
| nullable 型      | **使わない。** 列は `int` / `long` / `string` / `bool` / enum とし、CSV の空欄は型の既定値として扱う(検証の項目を参照) |

## enum

- enum は **C# で直接定義**する。ID は enum の値。
- スプレッドシートでの入力しやすさのために、メンバーに `[InputName("武器")]` で入力名を付ける。CSV
  にはこの入力名を書く。旧の YAML の `name` に相当する。
- 入力名の属性は、コード生成の出力から取り除く (クライアント/サーバーのコードには不要)。
- CSV に書けるのは入力名だけ。C# のメンバー名や数値は受け付けない。
- **すべてのメンバーに `[InputName]` を付ける。** CSV の列で使う enum に、付いていないメンバーがあればマスタ生成でエラーにする。
  `None = 0` のような内部用の値にも付ける。CSV の列で使わない enum は検証しない。
- 値 `0` のメンバーがない enum の列で、セルが空欄ならエラーにする。値 `0` のメンバーがあれば、空欄はそのメンバーになる。

## 暗号化 (アドオンの機能として提供)

- MasterMemory 自体には暗号化の機能がない (README に記載なし)。本リポジトリのアドオン機能として提供する。
- マスタ生成が出力するバイナリを暗号化する。クライアントやサーバーは、復号してから `new MemoryDatabase(bytes)` に渡す。
- 注意: 鍵はクライアントに含まれるため、これは **中身を読みにくくするもの**であって、強い秘匿ではない。
  CBC だけなので、**改ざんは検出しない**。壊れたデータは、ヘッダのデータのハッシュで見分ける (下の「バイナリのヘッダ」)。
- **暗号化するかどうかは出力先ごとに決める**(設定 YAML の `outputs[].encrypt`。[config](config.md))。
  想定は「クライアントは暗号化する、サーバーはしない」。サーバーのバイナリは利用者に配らないので、暗号化は必須ではない。

### 既定の方式 (決定: 旧 master-data-pipeline と同じ)

旧 `tools/dotnet/csharp-codegen/Generators/AesCryptoGenerator.cs` と同じ処理にする。

| 項目       | 値                                                                                   |
|------------|--------------------------------------------------------------------------------------|
| 暗号       | AES-128-CBC、PKCS7 パディング                                                        |
| 鍵導出     | PBKDF2(`Rfc2898DeriveBytes`)、SHA-256、1000 回、ソルト 16 バイト(暗号化のたびに乱数) |
| IV         | 16 バイト(暗号化のたびに生成)                                                        |
| 出力の形式 | `[ Salt(16) \| IV(16) \| 暗号文 ]`。バイナリでは、この前に平文のヘッダが付く(下の「バイナリのヘッダ」) |
| パスワード | スキーマのハッシュ `MasterDataSchema.Hash`(旧の `content_hash` に相当。CSV の実データは含めない)。英大文字が渡されても小文字にそろえてから使う |

- AES-GCM を使わない理由 (旧の経緯): IL2CPP の実機でネイティブの AEAD 実装がなく、`PlatformNotSupportedException` になった。
- 定義が変わるとパスワードが変わり、古いバイナリは読めない (旧と同じ。想定どおり)。

### スキーマのハッシュ `MasterDataSchema.Hash`(実装済み)

以前は、暗号化のパスワードの `MasterDataKey.Value` を client だけに出していた。2026-10-24 に `MasterDataSchema.Hash` に変えた ([decisions](decisions.md))。

- コード生成は、**client / server の両方**の出力先に `MasterDataSchema.cs` を出す。名前空間は `outputs[].namespace`(**必須**。[config](config.md))。
- 計算の元は、その出力先の target の **生成ソースから、コメントと空白を除いたトークン列**。SHA-256 の16進文字列 (小文字)。
  **定義が変わり、生成ソースが変わったときに値が変わる。**
  - コメントや改行だけの変更では値が変わらない。列・型・属性 (キーなど)が変わると値が変わる。
  - `[ServerOnly]` の列だけを変えると、server の値だけが変わる (client の生成ソースは変わらないため)。client と server の値は違う。
  - ファイル名やファイルの並び順には依存しない (ファイルごとのトークン列を並べ替えてから計算する)。
- client では、暗号化の鍵 (`IMasterDataCipher` に渡す `key`)も兼ねる。
- **鍵なので、アプリの外に出さない** (2026-10-25 決定。[decisions](decisions.md))。バイナリのヘッダ・マニフェスト・CDN の置き場所・エラーの表示には、
  この値から計算した **スキーマの ID** を使う (下記)。読み込むときは、アプリの `MasterDataSchema.Hash` から ID を計算して、ヘッダと比べる。
- 定義の直下に `MasterDataSchema.cs` があると、生成するファイルと重なるのでエラー (終了コード 2)。
  `outputs[].namespace` の名前空間の直下に `MasterDataSchema` という型を宣言した定義があっても、型が重なるのでエラー (終了コード 2)。
  別の名前空間や、型の中に入れ子にした `MasterDataSchema` は重ならないので使える。
- `--input / --target / --output` の形では出力しない (名前空間を指定できないため)。
- マスタ生成は、コンパイルした型から探さず、メモリ上のコード生成で計算した値をそのまま使う (同じ生成ソースから計算するので同じ値)。

出力:

```csharp
// <auto-generated>
// Generated by MasterMemory.Addons.Tools. Do not edit.
// </auto-generated>
namespace Sample.MasterData
{
    public static class MasterDataSchema
    {
        public const string Hash = "3401e990...";
    }
}
```

実装: `src/MasterMemory.Addons.Tools/CodeGen/MasterDataSchemaGenerator.cs`。

### スキーマの ID(実装済み)

スキーマのハッシュは client の暗号化の鍵なので、外に出すときは代わりにスキーマの ID を使う (review の R1。以前はヘッダとマニフェストにハッシュをそのまま書いていて、
配布したバイナリだけで復号できた)。

- `MasterDataBinary.ComputeSchemaId(schemaHash)` で計算する。`SHA-256("schema:"(ASCII) + スキーマのハッシュの 32 バイト)` の16進文字列 (小文字)。
- ID からスキーマのハッシュには戻せない。ハッシュの英大文字・小文字に関係なく同じ ID になる。
- 使う場所: バイナリのヘッダ・マニフェストの `schemaId`・CDN の置き場所 ([distribution](distribution.md))・`SchemaMismatch` のメッセージ。
- 鍵はアプリの中にあるので、アプリを解析すれば取り出せる。守れるのは「アプリを解析しないと読めない」までで、上の「注意」のとおり。

### 独自の暗号化への差し替え (決定・実装済み)

暗号化と復号をインターフェースにして、利用者が独自の実装に差し替えられるようにする。既定の実装は上の AES-CBC。
どちらも `MasterMemory.Addons.Core`(`netstandard2.1`、名前空間 `MasterMemory.Addons`)にある。Tools(マスタ生成での暗号化)・Unity・サーバーが参照する。

```csharp
namespace MasterMemory.Addons
{
    public interface IMasterDataCipher
    {
        byte[] Encrypt(byte[] data, string key);
        byte[] Decrypt(byte[] data, string key);
    }
}
```

- `key` は、ツールが渡すパスワード (`MasterDataSchema.Hash`)。独自の実装は、これを使っても無視してもよい。
  引数で渡すので、ツールは実装を引数なしのコンストラクタで作れる。
- 既定の実装は `AesMasterDataCipher`。旧の `AesCrypto` と同じ形式で、互いに読み書きできる (テストで確認)。
  ただし、ヘッダを付けると、バイナリのファイルとしては旧と互換がなくなる (下記)。
- 復号では、暗号文を別の配列にコピーせず、オフセットを指定して変換する (旧は暗号文もコピーしていた)。ソルトと IV の 16 バイトずつはコピーする。

実装: `src/MasterMemory.Addons.Core/`。テスト: `tests/MasterMemory.Addons.Core.Tests/`。

### バイナリのヘッダ (実装済み)

client / server どちらのバイナリにも、先頭に **平文のヘッダ**を付ける。暗号化するのは、ヘッダより後ろの中身だけ。
リトルエンディアン、固定長 72 バイト。

| 位置 | 長さ | 内容 |
|---|---|---|
| 0 | 4 | マジック `MMAD`(ASCII。本アドオンのバイナリであることの印) |
| 4 | 2 | 形式のバージョン(uint16)。今は `2`(`1` はスキーマのハッシュをそのまま書いていた形式で、読めない) |
| 6 | 2 | フラグ(uint16)。bit0 = 暗号化あり。ほかのビットは 0 |
| 8 | 32 | スキーマの ID(上の「スキーマの ID」。16 進をバイトにしたもの。鍵の `MasterDataSchema.Hash` は書かない) |
| 40 | 32 | データのハッシュ(暗号化する前の中身、つまり `DatabaseBuilder.Build()` が返したバイト列の SHA-256) |
| 72 | - | 中身(暗号化ありなら `[ Salt(16) \| IV(16) \| 暗号文 ]`) |

- データのハッシュは暗号化する前の中身から計算するので、中身が同じなら、暗号化の有無や乱数 (ソルト・IV)に関係なく同じ値になる。
  鍵が違うのに復号できてしまった (パディングの確認がたまたま通った)場合も、この値が合わないことで見分けられる。
- 読み込む側は、復号や `MemoryDatabase` の前に、次を見分けられる (下の「読み込みの関数」)。
  - 本アドオンのバイナリでない (短い、マジックが違う)。形式のバージョンやフラグに対応していない。
  - スキーマの ID が、アプリの `MasterDataSchema.Hash` から計算した ID と違う (アプリとデータの定義が食い違っている)。
  - 復号できない、またはデータのハッシュが合わない (データが壊れている)。
- server のバイナリは暗号化しないが、ヘッダでスキーマの食い違いを見分けられる。
- 旧 master-data-pipeline とのファイルの形式の互換はなくなる (旧の形式はヘッダがないので、本アドオンのバイナリではないと判断する)。

### 読み込みの関数 (実装済み)

```csharp
namespace MasterMemory.Addons
{
    public static class MasterDataBinary
    {
        // ヘッダを確かめ、暗号化されていれば復号して、MemoryDatabase に渡すバイト列を返す
        public static byte[] Read(byte[] binary, string schemaHash, IMasterDataCipher? cipher = null);

        // ヘッダを付ける。cipher を渡すと、schemaHash を鍵にして中身を暗号化する(マスタ生成が使う)
        public static byte[] Write(byte[] data, string schemaHash, IMasterDataCipher? cipher);

        // 外に出してよいスキーマの ID を計算する(CDN の置き場所を決めるときなどに使う)
        public static string ComputeSchemaId(string schemaHash);

        // ヘッダだけを読む。鍵は要らず、復号もしない(マニフェストや配信の確認に使う)
        public static MasterDataHeader ReadHeader(byte[] binary);
    }
}

// 使い方
var database = new MemoryDatabase(MasterDataBinary.Read(bytes, MasterDataSchema.Hash));
```

- `schemaHash` はアプリの `MasterDataSchema.Hash`。暗号化の鍵も兼ねる。英大文字・小文字は区別しない。
- 例外のメッセージには、スキーマのハッシュ (鍵)ではなく ID だけを書く。ログに出してもよい。
- `ReadHeader` は `MasterDataHeader`(`FormatVersion`・`Encrypted`・`SchemaId`・`DataHash`。ハッシュは16進の小文字)を返す。
  本アドオンのバイナリでない・形式が違うときは、`Read` と同じ `MasterDataFormatException`(`NotMasterData` / `UnsupportedVersion`)。
- `cipher` を省くと、暗号化されたバイナリは `AesMasterDataCipher` で復号する。独自の暗号化なら、その実装を渡す。
- 読めないときは `MasterDataFormatException` を投げる。`Reason` で理由が分かる。

| `Reason` | 意味 |
|---|---|
| `NotMasterData` | 本アドオンのバイナリでない (ヘッダより短い、マジックが違う) |
| `UnsupportedVersion` | 形式のバージョンやフラグに対応していない |
| `SchemaMismatch` | スキーマ (ID)がアプリと違う |
| `Corrupted` | 復号できない、またはデータのハッシュが合わない |

- `MemoryDatabase` を作るのは利用者のまま (`new MemoryDatabase(bytes)`)。MasterMemory の API は包まない。

実装: `src/MasterMemory.Addons.Core/MasterDataBinary.cs`、`MasterDataHeader.cs`。テスト: `tests/MasterMemory.Addons.Core.Tests/MasterDataBinaryTests.cs`。

### 未決定 (暗号化)

- 独自の実装の指定方法。マスタ生成は C (利用者の DLL を使わない) に決まったので、実装を含む DLL と型名を設定 YAML に書く形になる見込み。必要になったら決める。

## 設計上の制約 (マスタ生成)

- MasterMemory の `DatabaseBuilder` は、`[MemoryTable]` を含むプロジェクトで Source Generator が生成する。そのためマスタ生成
  は、 **Client 用/Server 用の生成ソースをコンパイルしたアセンブリ**に対して動く必要がある。実行形態は C (Tools の中でメモリ上にコンパイルする) に決定 ([master-gen](master-gen.md))。
- バイナリは `builder.Build()` で `byte[]` を得て、`new MemoryDatabase(bytes)` で読み込む (MasterMemory の README より)。
- 旧 `csharp_converter` のコメントより: MasterMemory は `DatabaseBuilder` / `MemoryDatabase` を、型の名前空間ではなく
  **プロジェクトの `RootNamespace`** に生成する (試作で確認済み。[master-gen-prototype](master-gen-prototype.md))。旧は、生成ソースを converter のプロジェクトに `Compile Include`
  で取り込んでビルドし、型名はリフレクションで探していた。
- クライアントのコードは Unity の中でコンパイルされるため、ツールが Unity の出力を読むことはできない。
  そのため、C では Tools が client 用の生成ソースを自分でコンパイルする。

試作の記録 (実行形態の候補 A / B / C の比較と結果)は [master-gen-prototype](master-gen-prototype.md) に移した。

## 未決定

- データの段の検証の属性の種類と、`db.Validate()` を併用するか (方針は決定済み)。
- 暗号化の細部 (上の「未決定 (暗号化)」)。

→ [decisions](decisions.md)
