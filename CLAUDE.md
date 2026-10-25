# CLAUDE.md

このファイルは、このリポジトリで作業する Claude Code 向けのガイドです。詳しいルールは、作業に応じて下のファイルを読む。

## プロジェクト概要

MasterMemory.Addons は、Cysharp の MasterMemory の **非公式アドオン (ヘルパー)**。C# のクライアント (Unity) とサーバーの両方で、
同じ定義からマスターデータを扱えるようにする。MasterMemory の API を包んだり置き換えたりはしない。
OSS として公開する前提 (公開してよくない情報・社内固有の情報は含めない)。

## 作業に応じて読むファイル

| 作業                                          | 読むファイル                                                                       |
|-----------------------------------------------|------------------------------------------------------------------------------------|
| 設計・仕様を確認する、未決定事項に触れる      | `docs/README.md`(索引)→ 関連ページ。決定・未決定は `docs/design/decisions.md` が正 |
| C# のコード・テストを書く                     | `docs/dev/coding.md`                                                               |
| プロジェクト・フォルダ・機能を足す            | `docs/dev/structure.md`                                                            |
| wiki (`docs/`) を変更する、コードを変更した後 | `docs/workflow.md`                                                                 |
| コードレビューの指摘に対応する                | `docs/review.md`(未対応の指摘。対応したら `[x]` にしてコミットを書く)              |

## 常に守ること

- 返答・ドキュメント・コミットメッセージは日本語で書く。
- 勝手に決めない。未決定事項や、決定済みの方針に反する変更・大きな設計変更は、ユーザーに確認する。
- コードを変更したら、関連する設計書・利用ガイドを同じ変更で更新する (手順は `docs/workflow.md`)。
- 存在しないビルドコマンドやファイルを、あるものとして書かない。
- **.NET SDK がない環境では、ビルド・テストの結果を推測で「通った」と書かない。** 確認できていないと明記し、ユーザーに実行を依頼する。

## ディレクトリ

- 直下: `README.md`(リポジトリの紹介。日本語)、`LICENSE`(MIT)
- `src/MasterMemory.Addons.Attributes/` : 定義に付ける属性 (`netstandard2.1`)
- `src/MasterMemory.Addons.Core/` : Tools / Unity / Server が共通で使う処理。暗号化など (`netstandard2.1`)
- `src/MasterMemory.Addons.Tools/` : 機能ごとのサブコマンドを持つ CLI (`net10.0`)。今あるのは `code-gen`(コード生成)と `master-gen`(マスタ生成)
- `tests/<アセンブリ>.Tests/` : テスト (xunit)
- `samples/` : サンプル定義 (`Definitions/`)、CSV (`csv/`)、設定ファイル (`masterdata.yaml`)、生成ソースを取り込むプロジェクト (`Client/` は Unity の代わり、`Server/`。slnx には入れていない)
- `docs/` : wiki (`design/` 設計書、`guide/` 利用ガイド、`dev/` 開発ルール)
- `Makefile` : 機能ごとのコマンドをつなぐ

## ビルド・テスト・実行

```
make build                     # dotnet build MasterMemory.Addons.slnx
make test                      # dotnet test MasterMemory.Addons.slnx
make code-gen [ENV=dev]        # samples/masterdata.yaml でコード生成する
make master-gen [ENV=dev]      # samples/csv を検証してバイナリを作る(生成ソースが古ければコード生成もやり直す)
make master-gen-check [ENV=dev] # 検証だけを行い、何も書き込まない
dotnet run --project src/MasterMemory.Addons.Tools -- code-gen --input samples/Definitions --target client --output out/client
```

make は別途入れる必要がある (Windows では scoop / choco、または WSL)。
