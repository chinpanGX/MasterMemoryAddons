---
title: 配信(DLC 方式)
status: implemented
updated: 2026-10-25
---

# 配信 (DLC 方式)

## 方針 (決定済み)

- マスターデータは **DLC 方式**で配信する。クライアントが CDN からダウンロードする。
- クライアントでの取り扱いは **Addressables**。
- **CDN 配信は必須ではない。** アプリビルドへの埋め込みも許容する。
- **配信の仕組みは、本パッケージでは作らない** (2026-10-25 決定)。CDN への配置とダウンロードは Addressables が受け持ち、
  その設定は利用者のプロジェクトで行う。本パッケージが提供するのは、配信に使う材料までとする。
  - バイナリ (平文のヘッダ付き)と、その隣のマニフェスト (`masterdata.manifest.json`。[master-gen](master-gen.md))
  - スキーマの ID (`MasterDataBinary.ComputeSchemaId`)と、鍵なしでヘッダを読む `MasterDataBinary.ReadHeader` ([pipeline](pipeline.md))
  - 読み込み (`MasterDataBinary.Read`)。Addressables などで取得した `byte[]` を渡す

## 構成 (利用者のプロジェクトで選ぶ)

| 方式           | 内容                                                             |
|----------------|------------------------------------------------------------------|
| CDN 配信       | 生成したマスターデータを CDN に置き、Addressables 経由で取得する |
| ビルド埋め込み | アプリに同梱して、そのまま読み込む                               |

## 定義とバイナリの対応 (前提)

- 定義が変わると、スキーマのハッシュ (`MasterDataSchema.Hash`)が変わり、client では暗号化の鍵も変わる ([pipeline](pipeline.md))。
  そのため、**CDN 上のバイナリは、それを読めるアプリのビルドと1対1に対応する。** 古いアプリが残る間は、古いスキーマのバイナリも置いておく必要がある。
- CDN の置き場所は、**スキーマごとに分ける**前提とする (分けるのは利用者の Addressables の設定)。パスには、スキーマのハッシュ (鍵)ではなく **スキーマの ID** を使う
  (`MasterDataBinary.ComputeSchemaId(MasterDataSchema.Hash)`。マニフェストの `schemaId` と同じ値。[pipeline](pipeline.md) の「スキーマの ID」)。
  CDN の URL・マニフェスト・ログなど、アプリの外に出るものには、スキーマのハッシュを書かない。
- バイナリの先頭のヘッダで、アプリとデータのスキーマの食い違いを、読み込む前に見分けられる (`MasterDataBinary.Read` が `SchemaMismatch` を投げる)。

## マニフェスト (実装済み)

- マスタ生成は、バイナリごとにマニフェストを出す。中身は、スキーマの ID、データのハッシュ、作った日時など。
- 利用者の配信の仕組みは、これを使って「どのアプリのビルドが読めるバイナリか」「client と server が同じデータを使っているか」を確かめられる。
- 形式とファイル名は [master-gen](master-gen.md) の「マニフェスト」(`masterdata.manifest.json`)。

## 範囲外 (利用者のプロジェクトで決める)

2026-10-25 に、未決定から範囲外に移した。

- クライアントが CDN のどのバイナリを読むかの決め方 (マニフェストの使い方)
- 差分更新の要否
- サーバー側のマスターの読み込み・更新方法

→ [decisions.md](decisions.md)
