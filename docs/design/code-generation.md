---
title: コード生成(Roslyn)
status: implemented
updated: 2026-10-25
---

# コード生成 (Roslyn)

> 実装あり (`src/MasterMemory.Addons.Tools/CodeGen`、テストは `tests/MasterMemory.Addons.Tools.Tests/CodeGen`)。ビルド・テスト
> (.NET SDK 10.0.401)と、`samples/Definitions` での Client / Server 出力を確認済み (2026-10-23)。設定ファイル (`--config`)
> による出力も、ビルド・テストと `samples/masterdata.yaml` での実行を確認済み (2026-10-24)。
>
> 実装メモ: 位置指定 record の引数は `SeparatedSyntaxList` なので、`VisitParameter` で `null` を返すと Roslyn
> が例外を投げる。引数の削除は `VisitParameterList` でリストから取り除いて行う。

## 二段構成

| 段 | 担当                                            | 内容                                                           |
|----|-------------------------------------------------|----------------------------------------------------------------|
| 1  | `MasterMemory.Addons.Tools code-gen`(このリポジトリ) | 定義ソースから、クライアント用/サーバー用のソースを出力する    |
| 2  | MasterMemory の Source Generator                | `[MemoryTable]` が付いた型から、データベースのコードを生成する |

MasterMemory の README によると、以前の MSBuild タスクと CLI ツールは削除され、現在のコード生成は Roslyn の Incremental
Source Generator である。
そのため本リポジトリは、MasterMemory のコード生成を置き換えず、 **その手前で定義を Client / Server に振り分ける**役割に絞る。

```
定義ソース(*.cs)  ──code-gen (target: client)──▶  client/*.cs ──▶ Unity(MasterMemory の Source Generator が DB コードを生成)
                  └─code-gen (target: server)──▶  server/*.cs ──▶ サーバー(同上)
```

## 方針 (決定済み)

- 変換には **Roslyn** を使う。
- 初期の実行形態は **CLI**。MSBuild への統合などは必要になってから検討する。

## code-gen の動作 (実装)

構文木だけを扱う (意味解析・コンパイルはしない)。そのため、MasterMemory や MessagePack を参照せずに動く。

1. 入力ディレクトリ以下の `*.cs` を読み、1回だけパースする (`bin` / `obj` と出力先ディレクトリは除外)。パースした結果は、target ごとの変換と、
   マスタ生成の定義の読み込み (`DefinitionSchemaReader`)で共有する (review の E8)。
2. 定義のエラー (構文エラー、テーブルの型が `class` でない)があれば、そのファイルは失敗として報告する (他のファイルの処理は続ける)。
3. [definition-model.md](definition-model.md) の規則に従って、反対側の Only の列・型を削除し、Only 属性、`[InputName]`、
   データ段の検証の属性 (`[Unique]` / `[Exists]`)、`using MasterMemory.Addons;` を取り除く。
4. 整形 (`NormalizeWhitespace`)して、ヘッダコメント付きで出力する。
5. 型が1つも残らないファイルは出力しない (以前の出力があれば削除する)。

整形するため、元のソースのインデントやコメントの位置は保たれない (生成物なので編集しない)。

## CLI

```
MasterMemory.Addons.Tools code-gen --config <file> [--env <name>]
MasterMemory.Addons.Tools code-gen --input <dir> --target <client|server> --output <dir>
```

通常は Makefile から呼ぶ (`make code-gen [CONFIG=<file>] [ENV=<name>]`)。以前の名前は `generate`。Tools のサブコマンドとしての位置づけは
[dev/structure](../dev/structure.md) の「機能のルール」を参照。

- `--config`: 設定ファイル (YAML)の `outputs` ごとに、その `target` で生成し、`source` のディレクトリへ置く。`source`
  の中身は丸ごと入れ替える (生成するファイルとフォルダの Unity の `.meta` は残す)。形式と安全のための確認は [config](config.md)。
    - 先にすべての対象を生成し、定義のエラーが1つでもあれば、どの出力先にも書き込まない。
    - 同じ `target` の出力先が複数あれば、生成は1回だけ行い、同じ内容を各出力先に置く。
    - すべての出力先に、スキーマのハッシュ `MasterDataSchema.cs` も置く(名前空間は `outputs[].namespace`。値は target ごと。計算方法は [pipeline](pipeline.md) の「スキーマのハッシュ」)。定義の直下に同名のファイルがある、または `outputs[].namespace` の名前空間の直下に `MasterDataSchema` という型を宣言した定義があれば、終了コード 2(コンパイルで型が重なるため。review の E7)。
- `--env`: `environments` から上書きに使う環境を選ぶ。`--config` と一緒にだけ使える。
- `--input / --target / --output`: 設定ファイルなしで試すための形。出力先は丸ごと消さず、生成したファイルの上書きと、型が残らなかったファイルの削除だけを行う。
  `--config` とは同時に使えない。

| 終了コード | 意味                                                   |
|------------|--------------------------------------------------------|
| 0          | 成功                                                   |
| 1          | 1つ以上のファイルで定義のエラー(構文エラー、テーブルの型が `class` でない) |
| 2          | 引数・設定ファイルが不正(出力先が安全でない場合を含む) |

## 構成

- `src/MasterMemory.Addons.Tools/Cli.cs` — 先頭の引数でサブコマンドを選ぶ
- `src/MasterMemory.Addons.Tools/CodeGen/CodeGenCommand.cs` — `code-gen` の引数処理とファイル入出力
- `src/MasterMemory.Addons.Tools/CodeGen/ConfigCodeGenerator.cs` — 設定の出力先ごとのコード生成 (ファイルには書き込まない。マスタ生成も使う)
- `src/MasterMemory.Addons.Tools/CodeGen/GeneratedSourceComparer.cs` — 出力先の生成ソースが、コード生成の結果と同じかの判定 (マスタ生成が使う)
- `src/MasterMemory.Addons.Tools/CodeGen/TargetFilterRewriter.cs` — 構文木の書き換え
- `src/MasterMemory.Addons.Tools/CodeGen/DefinitionTransformer.cs` — 1ファイル分の変換
- `src/MasterMemory.Addons.Tools/Common/DefinitionSource.cs` — 定義のファイルの読み込みとパース (1回だけ行い、共有する)
- `src/MasterMemory.Addons.Tools/CodeGen/SourceGenerator.cs` — パースした定義全体の変換 (ファイルには書き込まない)
- `src/MasterMemory.Addons.Tools/CodeGen/MasterDataSchemaGenerator.cs` — スキーマのハッシュ(`MasterDataSchema.Hash`)の計算とソースの生成
- `src/MasterMemory.Addons.Tools/Common/ConfigLoader.cs` — 設定ファイルの読み込みと環境の上書き (YamlDotNet)
- `src/MasterMemory.Addons.Tools/Common/OutputDirectory.cs` — 出力先の安全確認と、中身の入れ替え

## 検証

`tests/MasterMemory.Addons.Tools.Tests` で、次を確認する。

- 反対側の Only 列・型が削除されること (`ServerOnly` がクライアント用に残らない)。
- 自側の Only 属性と `using MasterMemory.Addons;` が取り除かれること。
- 位置指定 record の引数でも動くこと (テーブルではない型)。テーブルの型が `class` でない(`record`・`struct` など)ならエラーになること。
- 出力が構文的に正しいこと。型が残らなければ `null` を返すこと。
- 設定ファイルの読み込み (相対パスの解決、必須項目、未知の項目・重複した name・不正な target のエラー)と、環境の上書き
  (`ConfigLoaderTests`)。
- 出力先の安全確認 (`csv` と重なる出力先を含む)と、中身の入れ替え (出力先の外のファイルには触れない)(`OutputDirectoryTests`)。
- `--config` の実行全体 (出力先ごとの target、`--env`、定義のエラー (構文エラー、テーブルの型が `class` でない)の時に出力先へ触れない、安全でない出力先では何も消さない)
  (`CodeGenCommandTests`)。`MasterDataSchema.cs` がすべての出力先に置かれ、値が client と server で違うこと、定義と名前が重なればエラーになること。
- サブコマンドの振り分け (引数なし・未知のコマンドは終了コード 2、`--help` は 0)(`CliTests`)。
- `MasterDataSchema.Hash` の計算(コメント・空白・サーバー専用の列の変更では client の値は変わらない、列・型・属性の変更では変わる、ファイル名・順序に依存しない)と、出力が C# 9 として正しいこと(`MasterDataSchemaGeneratorTests`)。
- `outputs[].namespace`(client / server とも必須、名前空間として不正な値はエラー、環境での上書き)(`ConfigLoaderTests`)。

## 未決定

- MSBuild / `dotnet tool` としての配布。
- セマンティック解析が必要になる場面 (別名や継承など)。

→ [definition-model](definition-model.md) / [decisions](decisions.md)
