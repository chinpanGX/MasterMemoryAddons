---
title: Wiki 変更履歴
status: implemented
updated: 2026-10-25
---

# 変更履歴

新しいものを上に追記します。形式: `- YYYY-MM-DD [ページ] 変更内容`

- 2026-10-25 [review / log] 履歴を1つのコミットにまとめるため、review.md と log.md に残っていたコミット番号を消した
- 2026-10-25 [dev/structure] リポジトリの直下に README.md(日本語)と LICENSE(MIT)を置いた。直下に置くものの決まりを書き、名前空間の例(Tools.CodeGen)と Unity アセンブリの記述を直した
- 2026-10-25 [review] main へのマージで履歴を1つのコミットにまとめるため、review.md から csv-input-rules ブランチのコミット番号を消した(main にある以前のコミットの番号は残した)
- 2026-10-25 [pipeline / definition-model / master-gen / code-generation / decisions / guide / review] データ段の検証を実装(review の D2)。属性 [Unique] / [Exists] をマスタ生成が CSV で確かめ、MasterMemory の db.Validate() も呼ぶ。属性はコード生成の出力から取り除く。サンプルの DropTable.ItemId に [Exists] を付けた。ビルド・テスト・make master-gen を確認
- 2026-10-25 [code-generation / pipeline / master-gen / review] review の E7・E8 に対応。出力先の namespace に MasterDataSchema 型を宣言した定義は終了コード 2、定義は1回だけパースしてコード生成と定義の読み込みで共有する。ビルド・テスト・make master-gen を確認
- 2026-10-25 [distribution / overview / pipeline / dev/structure / decisions / guide] 配信の仕組みは本パッケージでは作らない(Addressables と利用者のプロジェクトが受け持つ)ことにし、配信まわりの未決定を範囲外に移した。Unity 専用のアセンブリは作らない
- 2026-10-25 [review / decisions / master-gen / guide] review の E4〜E6 の方針を決定し、対応(マスタ生成は一時ファイルに書いてから置き換える、作り直すと消えるファイルが残っていれば生成ソースは古い)。master-gen / guide も更新。ビルド・テスト・make master-gen を確認
- 2026-10-25 [review / definition-model / code-generation / config / master-gen / decisions / guide] R1〜R10 への対応と class だけにした変更のコードレビューの指摘(8 件。E1〜E8)を review.md に記録。E1〜E3 に対応(テーブルの型が struct・interface でもエラー、decisions.md の 2026-10-23 の行の書き添え、DatabaseBuilder を名前空間まで含めて探す)。ビルド・テスト・make master-gen を確認
- 2026-10-25 [definition-model / code-generation / config / master-gen / decisions / guide] テーブルの型を `class` だけにし、`record` はコード生成とマスタ生成で定義のエラーにする。エラーの表示を「構文エラー」から「定義のエラー」に変えた。サンプルの定義も `class` にした。ビルド・テスト・make master-gen を確認
- 2026-10-25 [master-gen / pipeline / config / decisions / guide / review] review の R4・R6〜R10 に対応。データの行はヘッダの末尾の空の列がなくてもよい、master-gen.md の設定の例に server の namespace、サンプルと config.md の古いコマンド名、Core の MasterDataBinary.ReadHeader でマニフェストを作る、テスト専用の入口(CsvTableReader.Read / FindFiles、InMemoryCompiler.Compile(string) / ReadSources)を消す、コンパイルを使い回すキーを target・スキーマのハッシュ・namespace にする。review の R1・R2・R3・R5 に対応したコミットを書いた。ビルド・テスト・make master-gen を確認
- 2026-10-25 [pipeline / master-gen / distribution / config / code-generation / decisions / guide / review] review の R1・R2・R3・R5 に対応。ヘッダ・マニフェスト(schemaId)・CDN のパス・エラー表示には鍵のハッシュではなくスキーマの ID を使う(形式のバージョン 2)、--check は生成ソースが古ければ終了コード 1、作り直すときに生成するファイルとフォルダの .meta を残す、スキーマのハッシュの大文字も受け付ける。ビルド・テスト・make master-gen を確認
- 2026-10-25 [review / decisions] review の R1・R2・R3・R5 の方針を決定(鍵の Hash は外に出さず、ヘッダ・マニフェスト・CDN のパスには戻せない公開用の値を使う、--check は生成ソースが古ければ終了コード 1、作り直すときに .meta を残す、スキーマのハッシュの大文字も受け付ける。未実装)
- 2026-10-24 [review] レビューの指摘への対応(0230411〜0e26f9e)のコードレビューの指摘事項 (10 件。R1〜R10)を review.md に記録
- 2026-10-24 [master-gen / distribution / pipeline / decisions / guide / review] review の D4 に対応。マスタ生成がバイナリの隣にマニフェスト(masterdata.manifest.json)を出す(--check では出さない。マニフェストのパスが重なれば設定の誤り)。サンプルのマニフェストを .gitignore に足した。ビルド・テスト・make master-gen を確認
- 2026-10-24 [pipeline / master-gen / code-generation / config / decisions / distribution / guide / review] review の D3・指摘 4 に対応。MasterDataKey を MasterDataSchema.Hash に変え、client / server の両方に出す(namespace は server でも必須)。バイナリに平文のヘッダ(72 バイト)を付け、Core に MasterDataBinary.Read / Write と MasterDataFormatException を足した。ヘッダの並び・読み込みの関数・マニフェストの形式を決定。サンプルの設定に server の namespace を足した。ビルド・テスト・make master-gen・samples/Client と samples/Server のビルドを確認
- 2026-10-24 [master-gen / master-gen-prototype / code-generation / config / pipeline / overview / decisions / definition-model / README / dev / guide / review] 機能の名前をコード生成(code-gen)・マスタ生成(master-gen)に変更(build.md → master-gen.md、build-prototype.md → master-gen-prototype.md、Tools のフォルダ Generate → CodeGen・Build → MasterGen、make の generate / build-data / data → code-gen / master-gen / master-gen-check)。review の D1(マスタ生成は生成ソースが古ければコード生成をやり直す)と D6(master-gen --check)に対応。CLAUDE.md・Makefile も変更。ビルド・テスト・make の各ターゲットを確認
- 2026-10-24 [build / decisions / guide / review] review の指摘 8 に対応。マスタ生成のコンパイルの参照を target ごとに絞った(client は NETStandard.Library.Ref 2.1.0 と netstandard 版の MasterMemory / MessagePack、C# 9。server は .NET の共有フレームワークと MasterMemory / MessagePack)。ビルド・テスト・サンプルでの generate / build を確認
- 2026-10-24 [config / build / review] review の指摘 7・9 に対応。環境の上書きで target を書いたらエラーにした。サンプルの dev 環境でも暗号化する。config.md の例で、暗号化しない dev のバイナリを別のパスにした。ビルド・テスト・サンプルの dev 環境での generate / build を確認
- 2026-10-24 [build / definition-model / review] review の指摘 10・D17 に対応。同じ中身の生成ソースのコンパイルを1回にした。definition-model の例を、サンプルと同じ enum(ItemCategory)の列にした(D17 の指摘はサンプルではなく設計書の例のことだった)。ビルド・テストを確認
- 2026-10-24 [definition-model / build / review] review の指摘 5・6 に対応。long に収まらない enum の値を定義のエラーにした(例外で落ちない)。ヘッダのエラーの行番号を、コメントに改行があってもファイルの実際の行で出す。ビルド・テストを確認
- 2026-10-24 [config / build / code-generation / review] review の指摘 1・2 に対応。出力先の source が csv と重なればエラー。型の中に入れ子にした enum の列を変換できるようにし、[InputName] の確認も C# と同じ順で enum を探す。ビルド・テスト・サンプルでの generate / build を確認
- 2026-10-24 [decisions / pipeline / build / build-prototype / config / code-generation / definition-model / distribution / overview / README / dev/structure / guide / review] review の指摘の方針を決定(未実装)。機能名をコード生成(code-gen)・マスタ生成(master-gen)にし、マスタ生成は生成ソースが古ければコード生成をやり直す。検証は master-gen --check。データ段の検証は分ける前の定義に属性で宣言。バイナリに平文のヘッダ、マニフェストを出す。鍵とスキーマのハッシュは MasterDataSchema.Hash(server にも出し、namespace は server でも必須)。target は環境で上書き不可。コンパイルの参照を絞る(client は netstandard2.1 + C# 9)。CSV の型は今のまま。試作の記録を build-prototype.md に移した。D7〜D14 の設計書の食い違いを修正
- 2026-10-24 [review] 設計書のレビューの指摘事項 (17 件。D1〜D17)を review.md に記録
- 2026-10-24 [build / decisions / pipeline / guide / review] 水平分割の区切りを `_` に変更し、テーブル名に `_` を使えなくした。CSV をフォルダの中まで探し、同じテーブルが複数のフォルダにあればエラー。ヘッダの末尾の空の列を無視する(review の指摘 3)。サンプルの CSV とテーブル名(drop_table → dropTable)を変更。ビルド・テスト・make data を確認
- 2026-10-24 [decisions / pipeline / config / overview / dev/structure] 本リポジトリではスプレッドシートのダウンロードを行わないことを決定。Fetch の段(SheetsDownloader のサブモジュール参照、Makefile の段、設定 YAML の項目)を取り除き、入力は CSV だけとした
- 2026-10-24 [pipeline / decisions / overview / dev/structure] ダウンロードの仕組み(認証・API・空のセルの補い方)の記述を削り、SheetsDownloader の設計書を正とした。本リポジトリには使い方だけを残した
- 2026-10-24 [decisions / pipeline / config / dev/structure] 入力規則は入力する人から見て master-data-pipeline と揃え、細部は今のままとすることを決定。pipeline は残す。ダウンロードは新しい公開リポジトリ SheetsDownloader(Python)に切り出し、両方からサブモジュールで参照する(結合はそれぞれに残す)。pipeline.md の旧との対応表を、決定済みの列名・分割ファイル名に合わせて直した
- 2026-10-24 [README / dev / decisions / code-generation] CLAUDE.md のルールを dev/coding.md・dev/structure.md に移し、CLAUDE.md は作業ごとに読むファイルへのリンクにした
- 2026-10-24 [review / README] Build の実装のコードレビューの指摘事項 (10 件)を review.md に記録
- 2026-10-24 [build / config / pipeline / overview / decisions / guide] Build を実装 (Tools の build コマンド、make build-data / make data)。CSV のファイル名・水平分割・ヘッダの列名・設定 YAML の csv / binary / encrypt を決定。主キーの重複は Tools で検出 (MasterMemory は検出しない)。試作のコード (spike/) を削除。ビルド・テスト・make data を確認
- 2026-10-24 [decisions / build / README] Build の実行形態を C に決定。build.md (案)を追加
- 2026-10-24 [pipeline] Build の実行形態 C (メモリ上でコンパイル)の試作を実施 (spike/InMemoryBuildSpike を追加)。通常のビルドとバイナリが一致することを確認し、候補の比較表に C を追加
- 2026-10-24 [pipeline] Build の試作を実施 (samples/Client・samples/Server・spike/BuildSpike を追加)。結果と実行形態の候補 (A / B)を記載
- 2026-10-24 [decisions / definition-model / pipeline / guide] enum の全メンバーに InputName を必須とし、値 0 のない enum の空欄をエラーにすることを決定
- 2026-10-24 [decisions / definition-model / pipeline / code-generation / guide] enum の入力名の属性を SheetName から InputName に変更し、CSV には入力名だけを書けることを決定。コード・テスト・サンプルも変更
- 2026-10-24 [decisions / pipeline / guide] CSV の空欄を型の既定値として扱うことを決定(主キーの空欄はエラー)
- 2026-10-24 [decisions / definition-model / pipeline / guide] nullable 型と record struct を使わないことを決定。CSV の空欄の扱いを未決定に追加
- 2026-10-24 [overview / pipeline / definition-model / code-generation / config / decisions / guide] Generator を Tools、Runtime を Core にリネーム。Tools をサブコマンド形式 (`generate`) にし、Makefile でつなぐ形にした。アセンブリ・フォルダ・機能・テストのルールを CLAUDE.md に追加。ビルド・テスト・`make generate` を確認
- 2026-10-24 [pipeline / config / code-generation / decisions / guide] MasterDataKey の出力(名前空間は outputs[].namespace)と、MasterMemory.Addons.Runtime(IMasterDataCipher / AesMasterDataCipher)を実装。ビルド・テスト・CLI 実行を確認
- 2026-10-24 [pipeline / decisions] 暗号化のパスワードをクライアント用だけに作り、定義が変わると出し直すことを決定 (名前は
  ContentHash をやめる)
- 2026-10-24 [overview / pipeline / decisions] 提供範囲を「バイナリを読み込んで MemoryDatabase を作るまで」と明記 (RDB
  への投入などはサーバー側の判断で範囲外)
- 2026-10-24 [config / code-generation / guide] 設定ファイル (`--config` / `--env`)を実装し、samples/masterdata.yaml
  を追加。ビルド・テスト・CLI 実行を確認
- 2026-10-24 [config / decisions / pipeline] 設定ファイルの細部を決定 (environments で項目ごとに上書き、出力先は name
  で指定、出力先は丸ごと削除して出力、binary / encrypt は機能の実装時に追加)
- 2026-10-24 [pipeline / config / decisions] 暗号化の有無を出力先ごとに設定することを決定 (サーバーは暗号化しない想定)
- 2026-10-24 [pipeline / decisions] 暗号化の既定の方式を旧 master-data-pipeline と同じにし、インターフェースで差し替えられるようにすることを決定
- 2026-10-23 [decisions / definition-model / pipeline / config / README] Roslyn の継続、定義の書き方、出力先への直接配置、設定ファイルを
  YAML にすることを決定。config.md (案)を追加
- 2026-10-23 [code-generation / pipeline / guide] 位置指定 record の引数削除で例外になる不具合を修正。ビルド・テスト確認済みに更新
- 2026-10-23 [decisions] コードスタイルの方針を決め、.editorconfig を更新 (参考の .editorconfig に合わせ、`var` 強制・接頭辞なしの
  camelCase に変更)
- 2026-10-23 [全体] 名前空間・プロジェクト名を MasterDataTools から MasterMemory.Addons に変更
  (非公式アドオンとして明記)
- 2026-10-23 [pipeline / definition-model / decisions / guide] enum・暗号化・複合キー・nullable・認証の方針を反映
  (MasterMemory の機能を確認)。`SheetName` 属性を追加
- 2026-10-23 [pipeline / decisions] 旧 master-data-pipeline の仕様 (Google スプレッドシート、CSV の形式、検証項目、出力)
  を調査して引き継ぎ内容を明記
- 2026-10-23 [overview / decisions] 位置づけを「MasterMemory のアドオン (ヘルパー)」と明記
- 2026-10-23 [code-generation / guide] Generator・Attributes・テスト・サンプルを追加 (未ビルド)
- 2026-10-23 [pipeline / decisions / overview / README] 要件 (スプレッドシート取得・CSV 検証・専用カラム宣言、YAML 不要)
  を反映し、pipeline.md を追加
- 2026-10-23 [definition-model / code-generation / decisions / overview / guide] MasterMemory の README に合わせて更新
  (MessagePack 前提、`record struct` 不使用、二段構成、CLI、netstandard2.1 方針)
- 2026-10-23 [全体] wiki を新規作成 (overview / definition-model / code-generation / distribution / decisions / guide
  3本 / workflow)
