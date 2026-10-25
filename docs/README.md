---
title: MasterMemory.Addons Wiki 索引
status: draft
updated: 2026-10-24
---

# MasterMemory.Addons Wiki

このディレクトリは、設計書と利用ドキュメントをまとめた **wiki** です。
人間と LLM (Claude Code など)の両方が読み書きすることを前提にしています。
開発の進め方は [workflow.md](workflow.md) を参照してください。

## ページ一覧

### 進め方

- [workflow.md](workflow.md) — コード駆動開発のルール (設計書→コード、コード→設計書)
- [log.md](log.md) — wiki の変更履歴 (新しいものを上に追記)
- [review.md](review.md) — コードレビューの指摘事項と、対応の状況

### 設計 (`design/`)

- [design/overview.md](design/overview.md) — 目的・スコープ・全体像
- [design/pipeline.md](design/pipeline.md) — 検証・ビルドのパイプライン
- [design/definition-model.md](design/definition-model.md) — マスターデータ定義 (C# POCO と属性)
- [design/code-generation.md](design/code-generation.md) — Roslyn による解析とクラス生成
- [design/config.md](design/config.md) — 設定ファイル (YAML。出力先・環境)
- [design/master-gen.md](design/master-gen.md) — マスタ生成 (CSV を検証してバイナリを作る。`master-gen`)
- [design/master-gen-prototype.md](design/master-gen-prototype.md) — マスタ生成の実行形態を決めた試作の記録
- [design/distribution.md](design/distribution.md) — DLC 配信と Addressables
- [design/decisions.md](design/decisions.md) — 決定事項と未決定事項

### 開発ルール (`dev/`)

- [dev/coding.md](dev/coding.md) — コードスタイル・Unity に入るコードの制約・テストの書き方
- [dev/structure.md](dev/structure.md) — アセンブリの分け方、フォルダ・名前空間、機能のルール

### 利用ガイド (`guide/`)

- [guide/getting-started.md](guide/getting-started.md) — 導入と基本的な使い方
- [guide/defining-master-data.md](guide/defining-master-data.md) — マスターデータの定義方法
- [guide/build-and-distribute.md](guide/build-and-distribute.md) — 生成・ビルド・配信の手順

## ページの状態 (`status`)

| status        | 意味                                     |
|---------------|------------------------------------------|
| `draft`       | 方針のみ。実装はまだない                 |
| `implemented` | 実装済みで、コードと内容が一致している   |
| `outdated`    | コードとずれている疑いがある。更新が必要 |

各ページ冒頭の frontmatter に `status` と `updated` を書きます。
