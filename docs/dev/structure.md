---
title: アセンブリ・フォルダ・機能の構成ルール
status: implemented
updated: 2026-10-25
---

# アセンブリ・フォルダ・機能の構成ルール

プロジェクト・フォルダ・ファイルを足すとき、新しい機能を実装するときに読む。決定の経緯は
[decisions.md](../design/decisions.md)。

## アセンブリの分け方

**どこで動くかで分け、機能ごとには分けない。**

| アセンブリ                       | TFM・形式                          | 参照先           | 入れるもの                                                                                                            |
|----------------------------------|------------------------------------|------------------|-----------------------------------------------------------------------------------------------------------------------|
| `MasterMemory.Addons.Attributes` | `netstandard2.1`                   | なし             | 定義に付ける属性だけ                                                                                                  |
| `MasterMemory.Addons.Core`       | `netstandard2.1`                   | なし             | Tools / Unity / Server のどこでも使う処理 (暗号化など)                                                                |
| `MasterMemory.Addons.Server`     | 未定                               | Core             | サーバー専用の処理 (ファイルからの読み込み、差し替えなど)。**必要になったら作る**                                     |
| `MasterMemory.Addons.Tools`      | `net10.0`                          | Core, Attributes | コード生成 / マスタ生成(検証を含む)                                                                                   |

- 参照の向きは「Core ← Server / Tools」だけ。Unity のプロジェクトは Core と Attributes を直接参照する。
- Unity 専用のアセンブリ (`MasterMemory.Addons.Unity`)は作らない (2026-10-25 決定)。配信は Addressables と利用者のプロジェクトが受け持ち、
  読み込みは Core の `MasterDataBinary.Read` で足りるため。
- 新しいアセンブリを足すのは、依存先か動く場所が違うときだけ。
- Core と Attributes は、Unity にも外部サービスにも依存しない。ツール用の依存 (Roslyn、YamlDotNet など) は
  Tools にだけ入れる。
- Unity に依存しないクライアントの処理 (計算など) は Core に置く。
- `MemoryDatabase` は利用者のプロジェクトで生成される型なので、Core / Server からは参照できない。補助の処理は
  `byte[]` を返すか、`Func<byte[], T>` を受け取る形にする。

## フォルダ・名前空間

- リポジトリの直下には、`README.md`(リポジトリの紹介)・`LICENSE`(MIT)・`CLAUDE.md`・`Makefile`・ソリューション(`MasterMemory.Addons.slnx`)と、
  ビルドの共通設定(`Directory.Build.props`・`.editorconfig` など)だけを置く。コードは `src/`・`tests/`・`samples/`、文書は `docs/` に置く。
- `src/<アセンブリ>/<機能>/` に置く。機能にまたがって使うものは `Common/` に置く。
- Tools の名前空間は「アセンブリ名 + フォルダ名」(例: `MasterMemory.Addons.Tools.CodeGen`)。ファイルスコープの
  `namespace` でよい。
- Unity と共有するアセンブリ (Attributes / Core) の名前空間は、フォルダによらず `MasterMemory.Addons` だけにする
  (利用者の `using` を1つにするため)。ブロック形式の `namespace` で書く。
- 1ファイルに1つの型を置き、ファイル名は型名と同じにする。
- テストは `tests/<アセンブリ>.Tests/` に置き、フォルダと名前空間を `src` と同じ構成にする。ファイル名は `<型名>Tests.cs`。

## 機能のルール

機能はパイプラインの段階 (コード生成 / マスタ生成 / 配信) を指す。スプレッドシートのダウンロードは範囲外 ([pipeline](../design/pipeline.md))。
今の機能は、コード生成 (`CodeGen/`、サブコマンド `code-gen`)とマスタ生成 (`MasterGen/`、サブコマンド `master-gen`)。

- 1つの機能は、次のものを1つずつ持つ: 設計書のページ、Tools のサブコマンド、設定 YAML のセクション、`src` のフォルダ、
  テストのフォルダ。
  - 例外: 検証は独立した機能にせず、マスタ生成のオプション `master-gen --check` とする。
- 1回の実行で行うのは1つの機能だけ。機能から別の機能を直接呼ばず、ファイル (CSV・ソース・バイナリ) で受け渡す。
  - 例外: マスタ生成は、生成ソースが古ければコード生成をやり直す ([master-gen](../design/master-gen.md))。処理は `CodeGen/` のものを使う。
- 機能を順につなぐのは Makefile。「どの順で何を実行するか」は Tools に持たせない。
- サブコマンドの処理は `<機能>/<機能>Command.cs` に置き、`Cli.cs` はコマンドの振り分けだけを行う。
- 機能を実装したら、Makefile にターゲットを足す。まだ実装していない機能のターゲットは書かない。
