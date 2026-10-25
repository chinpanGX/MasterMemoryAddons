---
title: 全体概要
status: draft
updated: 2026-10-25
---

# 全体概要

## 位置づけ

Cysharp の MasterMemory の **非公式アドオン (ヘルパー)**。MasterMemory の API を包んだり置き換えたりするフレームワークではない。
利用者は MasterMemory をそのまま使い、本リポジトリは「データを MasterMemory に渡すまで」
(検証・クライアント/サーバーの振り分け・バイナリのビルド)を受け持つ。スプレッドシートからの CSV のダウンロードは扱わない。

## 目的

モバイルゲーム向けのマスターデータ基盤。 **C# のクライアント (Unity)とサーバーの両方**で、同じ定義からマスターデータを扱えるようにする。

## 背景

- 既存の `master-data-pipeline`(chinpanGX/master-data-pipeline)は YAML で定義しており、MasterMemory の機能
  (セカンダリキーの登録など)を十分に使えていなかった。
- そのため YAML をやめ、新規リポジトリとして作り直す (本リポジトリ)。
- OSS として公開する前提で設計する。

## スコープ

含むもの:

- CSV の検証と、CSV からのバイナリの作成
- C# POCO によるマスターデータ定義 (クライアント/サーバー専用カラムの宣言を含む)
- 定義からクライアント用/サーバー用クラスを生成するツール
- マスターデータの配信 (DLC 方式)に使う材料 (バイナリのヘッダ、マニフェスト、スキーマの ID)

含まないもの:

- Rust 向けの出力 (旧リポジトリにあったが、本リポジトリでは扱わない)
- 配信の仕組み (CDN への配置とダウンロード)。Addressables と利用者のプロジェクトが受け持つ ([distribution](distribution.md))
- 読み込んだマスターデータを RDB などに入れる処理。本リポジトリが提供するのは、バイナリを読み込んで `MemoryDatabase`
  を作るところまで。その後の使い方 (DB に入れるなど)はサーバー側で決める

## 全体の流れ

```
C# 定義(MasterMemory 属性 + ServerOnly/ClientOnly)
        │ コード生成(Roslyn)
        ▼
クライアント用ソース / サーバー用ソース(MasterMemory の Source Generator が DB コードを生成)
        │
(範囲外: スプレッドシート ──ダウンロード──▶) CSV ──マスタ生成(検証 → バイナリ)──▶ client / server バイナリ ──▶ CDN 配信(DLC)またはビルド埋め込み
```

実装済みなのはコード生成 (`code-gen`。定義の振り分け)とマスタ生成 (`master-gen`。CSV の検証とバイナリ。検証だけなら `master-gen --check`。[master-gen](master-gen.md))。
配信の仕組みは範囲外 (Addressables が受け持つ。[distribution](distribution.md))。スプレッドシートのダウンロードは範囲外。

詳細は各ページを参照:
[pipeline](pipeline.md) / [definition-model](definition-model.md) / [code-generation](code-generation.md) / [distribution](distribution.md)

## 関連

- [decisions.md](decisions.md)
