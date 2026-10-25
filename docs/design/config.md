---
title: 設定ファイル(YAML)
status: implemented
updated: 2026-10-25
---

# 設定ファイル (YAML)

> 実装あり (`src/MasterMemory.Addons.Tools/Common` の `ConfigLoader.cs` / `OutputDirectory.cs`)
> 。今の項目はコード生成(`code-gen`)とマスタ生成(`master-gen`)に使うもの。スプレッドシートのダウンロードは本リポジトリでは行わないので、取得元の項目は持たない ([pipeline](pipeline.md))。

旧 `config.yaml` に相当する。CLI の引数で毎回指定する代わりに、 **定義の場所と出力先を1つの YAML にまとめる**。
これは設定であり、旧の「YAML の中間データ (テーブル定義)」とは別物。定義の正は引き続き C# のソース
([definition-model](definition-model.md))。

## 目的

- 生成したソース (将来はバイナリも)を、 **各プロジェクトの出力先へ直接置く**(手でコピーしない)。
- 出力先を **複数**持てる (Unity クライアント、API サーバー、バトルサーバーなど)。
- **環境**(開発・本番など)ごとに、一部の項目を切り替えられる。

## 形式

```yaml
# masterdata.yaml
definitions: Definitions          # 定義ソースのディレクトリ
csv: csv                          # CSV のディレクトリ(マスタ生成で必須)

outputs:                          # 出力先の一覧(1つ以上)
  - name: unity
    target: client                # client | server
    source: ../Client/Assets/MasterData/Generated
    namespace: Sample.MasterData  # MasterDataSchema を出力する名前空間(必須)
    binary: ../Client/Assets/MasterData/masterdata.bytes   # マスタ生成で作るバイナリ
    encrypt: true                 # client のときだけ書ける。省略時は false
  - name: api-server
    target: server
    source: ../Server/Api/MasterData/Generated
    namespace: Sample.MasterData
    binary: ../Server/Api/MasterData/masterdata.bytes
  - name: battle-server
    target: server
    source: ../Server/Battle/MasterData/Generated
    namespace: Sample.MasterData

environments:                     # 環境ごとの上書き(書いた項目だけ変わる)
  dev:
    outputs:
      unity:
        source: ../Client/Assets/MasterData/GeneratedDev
        binary: ../Client/Assets/MasterData/masterdata-dev.bytes   # 暗号化しないバイナリは別のパスにする
        encrypt: false
  production: {}
```

| 項目               | 必須 | 内容                                                             |
|--------------------|------|------------------------------------------------------------------|
| `definitions`      | ○    | 定義ソース(`*.cs`)のディレクトリ                                 |
| `outputs`          | ○    | 出力先のリスト。1つ以上                                          |
| `outputs[].name`   | ○    | 出力先の名前。`outputs` の中で重複不可                           |
| `outputs[].target` | ○    | `client` または `server`。同じ `target` の出力先が複数あってよい |
| `outputs[].source` | ○    | 生成ソースを置くディレクトリ                                     |
| `outputs[].namespace` | ○ | `MasterDataSchema`(スキーマのハッシュ。client では暗号化の鍵も兼ねる)を出力する名前空間。client / server とも必須([pipeline](pipeline.md)) |
| `csv`              | マスタ生成で○ | CSV のディレクトリ([master-gen](master-gen.md)) |
| `outputs[].binary` |      | マスタ生成で作るバイナリのパス。書いた出力先だけがバイナリを作る対象になる。`source`・`definitions`・`csv` の中には置けない |
| `outputs[].encrypt` |     | `true` / `false`(省略時は `false`)。`true` ならマスタ生成でバイナリを暗号化する。`target: client` のときだけ `true` にできる |
| `environments`     |      | 環境名 → 上書きの内容                                            |

- パスは、 **設定ファイルのあるディレクトリからの相対パス**(絶対パスも可)。
- 上の表にない項目を書くとエラーにする (打ち間違いを黙って無視しないため)。

## 環境の上書き (決定)

- 基本の設定に、`--env <名前>` で選んだ `environments.<名前>` を重ねる。 **書いた項目だけ**が変わる。
- `--env` を省略すると、基本の設定だけを使う。`environments` にない名前を指定したらエラー。
- 上書きできる項目:
    - `definitions` / `csv`
    - `outputs.<name>.source` / `outputs.<name>.namespace` / `outputs.<name>.binary` / `outputs.<name>.encrypt`
- **`outputs.<name>.target` は上書きできない**。出力先が client か server かは、環境によらず同じとする
  (変えると `namespace` や `encrypt` の意味が変わるため)。書いたらエラー(同じ値でも)。
- 出力先は **`name` をキーにしたマップ**で指定する (基本の設定ではリスト、上書きではマップ)。基本の設定にない `name`
  を書いたらエラー。
- 環境で出力先を増やしたり減らしたりはできない (出力先は全環境で同じ、という前提)。

## 出力先の扱い (決定)

- 出力先の `source` ディレクトリは **生成専用**とみなし、生成のたびに **中身を丸ごと削除してから**出力する。手書きのファイルを置かないこと。
    - ただし、生成するファイルとフォルダの Unity の `.meta` (`Item.cs.meta`、`Tables.meta` など)は残す。作り直すたびに GUID が変わり、
      `.meta` の差分が出ないようにするため (2026-10-25 決定。review の R3)。生成しなくなったファイル・フォルダの `.meta` は消す。
- 誤設定で手書きのファイルを消さないよう、次の場合は **何も消さずにエラー**にする。
    - `source` が、設定ファイルのディレクトリと同じか、その上位にある。
    - `source` が、`definitions` と同じか、その上位・下位にある。
    - `source` がドライブのルート。
    - 2つの出力先の `source` が、同じか、一方がもう一方の中にある。
    - `source` が、`csv` と同じか、その上位・下位にある(`csv` を書いたときだけ)。
- 先にすべての出力先の生成をメモリ上で行い、 **定義のエラー (構文エラー、テーブルの型が `class` でない)が1つでもあれば、どの出力先にも触れない**(終了コード 1)。

## CLI

```
MasterMemory.Addons.Tools code-gen --config masterdata.yaml [--env dev]
MasterMemory.Addons.Tools build --config masterdata.yaml [--env dev]
```

- `--config` と `--input / --target / --output` は同時に使えない。
- `--input / --target / --output` は、設定ファイルなしで試すために残す。こちらは出力先を丸ごと消さない
  (従来どおり、生成したファイルの上書きと、型が残らなかったファイルの削除だけ)。

## 後で足す項目

それぞれの機能を実装するときに、項目名と内容を決める (今は書くとエラー)。

| 機能   | 想定する項目                                                                                    |
|--------|-------------------------------------------------------------------------------------------------|
| 暗号化 | 独自の暗号化 (`IMasterDataCipher` の独自の実装)の指定。必要になったら足す |

→ [pipeline](pipeline.md) / [code-generation](code-generation.md) / [decisions](decisions.md)
