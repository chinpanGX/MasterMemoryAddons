---
title: はじめに
status: draft
updated: 2026-10-25
---

# はじめに

> `dotnet build` / `dotnet test` と、サンプル定義・サンプル設定 (`samples/masterdata.yaml`)・サンプル CSV (`samples/csv/`)での
> `make code-gen` / `make master-gen` / `make master-gen-check` の実行は確認済みです (2026-10-24)。作ったバイナリを `MemoryDatabase` で読めることもテストで確認しています。
> 実際の Unity での読み込みは未確認です。

## 想定する利用者

Unity クライアントとサーバー (C#)の両方でマスターデータを使う開発者。

## 前提

- .NET 10 SDK (`dotnet --version` が `10.x`)
- Rider など、.NET 10 に対応した IDE
- Unity 2022.3.12f1 以上 (MasterMemory の要件)
- make (Windows では scoop / choco で入れるか、WSL を使う)

## このリポジトリのビルドとテスト

```
make build    # dotnet build MasterMemory.Addons.slnx
make test     # dotnet test MasterMemory.Addons.slnx
```

## 基本の流れ

1. マスターデータを C# で定義する → [defining-master-data](defining-master-data.md)
2. 設定ファイル (`masterdata.yaml`)に、定義の場所と出力先 (Unity、API サーバーなど)
   を書く → [build-and-distribute](build-and-distribute.md)
3. `make code-gen` を実行し、クライアント用/サーバー用のソースを各プロジェクトへ直接置く (各プロジェクトで
   MasterMemory の Source Generator が DB コードを生成する)
4. CSV を用意し、`make master-gen` で検証してバイナリを作る(暗号化は client だけ。定義を変えたときは、生成ソースも作り直す)→ [build-and-distribute](build-and-distribute.md)
5. バイナリを配信する (Addressables などで、利用者のプロジェクトが行う。本パッケージには配信の仕組みはない)→ [build-and-distribute](build-and-distribute.md)

## 導入方法

未定 (OSS 公開時の配布形態が決まり次第、記載する)。
