---
title: コードレビューの指摘事項
status: implemented
updated: 2026-10-25
---

# コードレビューの指摘事項

コードレビューで出た指摘のうち、まだ対応していないものを残す。対応したら `[x]` にして、対応したコミットを書く。
新しいレビューは上に追記する。

## 2026-10-25 R1〜R10 への対応と、テーブルの型を class だけにした変更のレビュー

- 対象: R1〜R10 への対応と、テーブルの型を `class` だけにした変更
- 方法: `/code-review`(Claude Code)。指摘はコードと照合した(ビルド・テストはレビューでは実行していない)
- 指摘: 8 件(直す 3 件、方針を決めてから直す 3 件、小さな点 2 件)

### 直す

- [x] **E1. `struct` のテーブルがエラーにならない**(対応: `class` 以外のテーブルの型(`record`・`record struct`・`struct`・`interface`)を、コード生成とマスタ生成で定義のエラーにする。種類をエラーに表示する)(`src/MasterMemory.Addons.Tools/CodeGen/DefinitionTransformer.cs:33`、`src/MasterMemory.Addons.Tools/Common/DefinitionSchemaReader.cs:82`)
  - 「テーブルの型は `class` だけ」(2026-10-25 決定)の確認が `record` だけを見ている。`[MemoryTable] public struct Item` は、
    コード生成もマスタ生成も通り、バイナリもできる。テストもない。
  - 対応案: `class` 以外(`struct`・`record`・`interface`)のテーブルの型をエラーにする。
- [x] **E2. decisions.md の 2026-10-23 の行に「テーブルの型は `class` / `record`」が残っている**(対応: 2026-10-23 の行に「2026-10-25 に、テーブルの型は `class` だけにした」と書き添えた)(`docs/design/decisions.md:21`)
  - 2026-10-25 の決定(`class` だけ)と食い違う。ほかの行のような「後に変更」の書き添えがない。テーブルの型を `class` だけにした変更の直し漏れ。
- [x] **E3. 定義に `DatabaseBuilder` という名前の型があると、マスタ生成が必ず失敗する**(対応: `InMemoryCompiler.RootNamespace` を含めた名前で探す)(`src/MasterMemory.Addons.Tools/MasterGen/MasterDataBinaryBuilder.cs:41`)
  - 生成された `DatabaseBuilder` を型の名前だけで `SingleOrDefault` しているので、同じ名前の型が2つあると例外になり、
    「バイナリを作れない」と表示される。指摘 4(`MasterDataKey`)と同じ種類の問題。
  - 対応案: コンパイルの `RootNamespace` を含めた名前で探す。

### 方針を決めてから直す

- [x] **E4. 出力先ごとのバイナリとマニフェストを順に書き、書き込みの失敗を扱っていない**(対応: すべての出力先のバイナリとマニフェストを `<パス>.tmp` に書いてから置き換える。失敗したら一時ファイルを消して終了コード 1)(`src/MasterMemory.Addons.Tools/MasterGen/MasterGenCommand.cs:218`)
  - **方針(2026-10-25 決定)**: すべての出力先のバイナリとマニフェストを、先に一時ファイルに書き、すべて書けてから本来の名前に置き換える。一時ファイルの段階で失敗したら、一時ファイルを消して終了コード 1(元のファイルは変えない)。書き込みの失敗はメッセージで表示する
  - 2つ目の出力先の書き込みが失敗する(ファイルのロックなど)と、1つ目だけが新しいデータになり、client と server のデータが食い違う。
    例外はとらえておらず、スタックトレースで落ちる(終了コードも決まっていない)。
  - 対応案: 書き込みの例外をとらえて、エラーの表示と終了コードにする。すべての出力先をまとめて入れ替える(一時ファイルに書いてから移す)かは決める。
- [x] **E5. バイナリを書いてからマニフェストを書くので、マニフェストだけが古いまま残りうる**(対応: E4 と同じ仕組みで、マニフェストも一時ファイルから置き換える)(`src/MasterMemory.Addons.Tools/MasterGen/MasterGenCommand.cs:222`)
  - **方針(2026-10-25 決定)**: E4 と同じ仕組みで対応する(マニフェストも一時ファイルに書いてから置き換える)
  - マニフェストの書き込みが失敗すると、新しいバイナリの隣に古いマニフェスト(古い `schemaId` / `dataHash` / `size`)が残り、配信で誤った判断をする。
  - E4 と一緒に決める。
- [x] **E6. 生成ソースの比較は `*.cs` だけなので、残った `.meta` やほかのファイルがあっても「古くない」になる**(対応: `GeneratedSourceComparer` が、`OutputDirectory.MetaFilesToKeep` 以外の、生成しないファイルが残っていれば古いとみなす)(`src/MasterMemory.Addons.Tools/CodeGen/GeneratedSourceComparer.cs`)
  - **方針(2026-10-25 決定)**: 作り直すと消えるファイル(生成しなくなったファイル・フォルダの `.meta`、手で置いたファイル)が残っていれば古いとみなす。生成するファイル・フォルダの `.meta` は今までどおり比べない。`--check` とコード生成の動きをそろえる
  - `OutputDirectory.Replace` は、生成しなくなったファイルの `.meta` や手で置いたファイルを消すが、比較ではそれらを見ない。
    `master-gen --check` が 0 を返す一方で、コード生成を実行するとファイルが消える(動きが食い違う)。
  - D1 で「`*.cs` 以外は比べない」と決めた。R3 で `.meta` を残すようにしたので、要らない `.meta` が残っていたら古いとみなすかを決める。

### 小さな点

- [x] **E7. `MasterDataSchema` との名前の重なりを、定義の直下のファイル名でしか確かめていない**(対応: 生成ソースが出力先の `namespace` の直下に `MasterDataSchema` 型を宣言していれば、終了コード 2。別の名前空間や入れ子の型は通す)(`src/MasterMemory.Addons.Tools/CodeGen/ConfigCodeGenerator.cs:38`)
  - `Sub/MasterDataSchema.cs` や、出力先の `namespace` と同じ名前空間に `MasterDataSchema` 型を書いた定義は、ここでは分からず、
    後でコンパイルエラー(CS0101)になる。原因の分かりにくいエラーになるだけで、データは壊れない。
- [x] **E8. 定義を何回もパースし、定義の確認も2か所に分かれている**(対応: 定義は `DefinitionSource.Load` で1回だけパースし、target ごとのコード生成と `DefinitionSchemaReader` で共有する。テーブルの型の確認は `NonClassTableMessage` の1つ(E1)。生成ソースのパース(スキーマのハッシュとコンパイル)は、出力するテキストが元なので残す)(`src/MasterMemory.Addons.Tools/MasterGen/MasterGenCommand.cs:127`)
  - target ごとのコード生成・`DefinitionSchemaReader`・スキーマのハッシュの計算・コンパイルで、同じ定義を 3〜4 回パースする。
    テーブルの型の確認は `DefinitionTransformer` と `DefinitionSchemaReader` の2か所にあり、E1 の漏れも両方にあった。
  - 対応案: パースの結果と定義の確認を1か所にまとめる。

## 2026-10-24 レビューの指摘への対応のレビュー

- 対象: 指摘 1〜10・D1〜D17 への対応(改名、生成ソースの作り直し、`--check`、参照の絞り込み、ヘッダ、マニフェストなど)
- 方法: `/code-review`(Claude Code)。主な指摘はコードと照合した
- 指摘: 10 件(方針を決めてから直す 3 件、直す 4 件、小さな点 3 件)

### 方針を決めてから直す

- [x] **R1. 暗号化の鍵が、バイナリのヘッダに平文で入っている**(対応: ヘッダ・マニフェストの `schemaId`・`SchemaMismatch` の表示に、`MasterDataBinary.ComputeSchemaId` で計算したスキーマの ID を使う。形式のバージョンを 2 にした)(`src/MasterMemory.Addons.Core/MasterDataBinary.cs:39`)【重大】
  - 鍵は `MasterDataSchema.Hash` で、同じ値をヘッダのスキーマのハッシュ(8〜39 バイト)にそのまま書いている。配布した client のバイナリだけで、
    アプリがなくても復号できる(ヘッダのデータのハッシュで、復号が正しいことも確かめられる)。
  - decisions.md の「鍵はクライアントにある」という前提が、ファイルの形式で崩れている。D3 の実装のときの見落とし。
  - 対応案 A: ヘッダには、スキーマのハッシュをもう一度ハッシュした値(例: `SHA-256("schema:" + Hash)`)を書き、読むときも同じ計算をして比べる。鍵は今のまま。
  - 対応案 B: 鍵を、スキーマのハッシュから別の計算で作る(例: `SHA-256("key:" + Hash)`)。ヘッダは今のまま。
  - **方針(2026-10-25 決定)**: 案 A を広げる。鍵(`MasterDataSchema.Hash`)はアプリの外に出さない。外に出すのは、Hash から戻せない公開用の値(例: `SHA-256("schema:" + Hash)`)だけとし、バイナリのヘッダ・マニフェストの `schemaHash`・CDN の置き場所・`SchemaMismatch` などの表示に使う。読み込むときは、アプリが Hash から公開用の値を計算してヘッダと比べる。案 B は、鍵の計算が OSS で公開されているため、ヘッダの Hash から鍵を計算できてしまい直らない
- [x] **R2. `--check` は、生成ソースが古くても終了コード 0 になる**(対応: `--check` で生成ソースが古ければ、出力先の名前を表示して終了コード 1)(`src/MasterMemory.Addons.Tools/MasterGen/MasterGenCommand.cs:262`)
  - **方針(2026-10-25 決定)**: `--check` で生成ソースが古ければ終了コード 1 にする(D6 の「警告だけ」を変える)。CSV の検証は今のとおり最後まで行い、結果も表示する
  - D6 で決めたとおり警告だけを出すが、CI で `make master-gen-check` を使うと、コード生成のし忘れ(古い `Generated/` のコミット)を見逃す。
    そのまま Unity に入ると、次のバイナリで `SchemaMismatch` になる。
  - 対応案: `--check` で生成ソースが古ければ終了コード 1 にする(または、そうするオプションを足す)。
- [x] **R3. マスタ生成が生成ソースを作り直すと、Unity の `.meta` も消える**(対応: `OutputDirectory.Replace` が、生成するファイルとフォルダの `.meta` を残す(コード生成も同じ))(`src/MasterMemory.Addons.Tools/MasterGen/MasterGenCommand.cs:269`)
  - **方針(2026-10-25 決定)**: コード生成も含めて、作り直す `*.cs` の `.meta` は残し、なくなる `*.cs` の `.meta` は消す(`OutputDirectory.Replace` を変える)
  - `OutputDirectory.Replace` は出力先の中身を丸ごと消す。比べるときは `.meta` を無視するが、作り直すときは消える。
    バイナリを作るつもりでマスタ生成を実行しただけで、生成ソースの GUID が変わり、`.meta` の差分が大量に出る。
  - コード生成と同じ動きで、利用ガイドにも書いてある。生成物は MonoBehaviour ではないので、参照は壊れない想定(config.md)。
  - 対応案: 作り直すときは `*.cs` だけを入れ替え、対応する `.meta` は残す(コード生成も同じにするかを含めて決める)。

### 直す

- [x] **R4. CSV の列数の確認が、ヘッダの末尾の空の列を含んだ数で比べている**(対応: データの行の列数を、名前のある列の数からヘッダの列の数までなら受け付ける)(`src/MasterMemory.Addons.Tools/Common/CsvTableReader.cs:110`)
  - ヘッダの末尾の空の列は無視する(`width`)が、データの行の列数は空の列を含むヘッダの列数(`rawHeader.Count`)と比べている。
  - 例: コメントの行が長く、ヘッダが `ItemId,Name,,` になったファイルに、テキストエディタで `9,x` の行を足すと、
    値はすべて名前のある列に入っているのに「列の数がヘッダと違います(ヘッダ 4 列、この行 2 列)」になる。指摘 3 の対応からある。
  - 対応案: `width <= 列数 <= rawHeader.Count` の行を受け付ける。
- [x] **R5. 英大文字のスキーマのハッシュを渡すと、「壊れている」と表示される**(対応: 鍵に使う前に、スキーマのハッシュを小文字にそろえる)(`src/MasterMemory.Addons.Core/MasterDataBinary.cs:91`)
  - **方針(2026-10-25 決定)**: 大文字も受け付ける。鍵は、16 進をバイトにした値(または小文字にそろえた文字列)から作り、照合と扱いをそろえる
  - スキーマの照合(16 進をバイトにして比べる)は大文字小文字を区別しないが、鍵(PBKDF2 のパスワード)には文字列をそのまま使う。
    `MasterDataSchema.Hash.ToUpperInvariant()` などを渡すと照合は通り、復号に失敗して `Corrupted` になる(本当の原因が分からない)。
  - 対応案: 小文字の 16 進だけを受け付ける(大文字は `ArgumentException`)。または、鍵に使う前に小文字にそろえる。
- [x] **R6. `master-gen.md` の設定 YAML の例で、server の出力先に `namespace` がない**(対応: 例の server の出力先に `namespace` を足した)(`docs/design/master-gen.md:66`)
  - `namespace` は server でも必須になった(D3)ので、例をそのまま使うと設定の読み込みでエラー(終了コード 2)になる。直し漏れ。
- [x] **R7. 古いコマンド名が残っている**(対応: csproj と `masterdata.yaml` のコメントを直した。config.md に残っていた「build」も直した)(`samples/Client/Sample.Client.csproj:6`、`samples/Server/Sample.Server.csproj`、`samples/masterdata.yaml`)
  - csproj のコメントに `make generate`、`masterdata.yaml` のコメントに「build の対象」が残っている。改名の直し漏れ。
    CLAUDE.md の「存在しないビルドコマンドを、あるものとして書かない」に反する。

### 小さな点

- [x] **R8. マニフェストが、ヘッダのデータのハッシュの位置を自分で書いている**(対応: Core に `MasterDataBinary.ReadHeader` と `MasterDataHeader` を足し、マニフェストはヘッダを読んで作る)(`src/MasterMemory.Addons.Tools/MasterGen/MasterDataManifest.cs:33`)
  - `Convert.ToHexString(binary, 40, 32)` と、Core の `MasterDataBinary` の非公開の定数を写している。ヘッダの並びを変えると、
    マニフェストの `dataHash` だけが黙ってずれる。
  - 対応案: Core に、ヘッダを読む関数(またはデータのハッシュの位置の定数)を公開して使う。
- [x] **R9. 本番の処理では使わず、テストからだけ呼ばれるコードが残っている**(対応: `CsvTableReader.Read(dir, table, errors)`・`FindFiles`・`InMemoryCompiler.Compile(string, target)`・`ReadSources` を消し、テストは本番と同じ `EnumerateFiles` → `GroupFilesByTable` → `Read` の経路で読む)(`src/MasterMemory.Addons.Tools/Common/CsvTableReader.cs:23` など)
  - `CsvTableReader.Read(dir, table, errors)` と `FindFiles`、`InMemoryCompiler.Compile(string, target)` と `ReadSources`。
    本番(マスタ生成)は別の経路でファイルを探す・読むので、テストが本番と違う経路を確かめている(例: 複数のフォルダにあるときのまとめ方)。
  - 対応案: テストを本番と同じ経路に寄せ、使わない入口を消す。
- [x] **R10. コンパイルの結果を使い回すキーを、生成ソース全体のハッシュで作っている**(対応: キーを `target:スキーマのハッシュ:namespace` にした)(`src/MasterMemory.Addons.Tools/MasterGen/MasterGenCommand.cs:169`)
  - コード生成の結果は target・定義・namespace だけで決まるので、`target + SchemaHash + namespace` で足りる。出力先ごとに全ソースを
    エンコードしてハッシュを取り直していて無駄(定義が多いと遅くなる)。

## 2026-10-24 設計書のレビュー

- 対象: `docs/` の設計書・利用ガイド・開発ルール(指摘 3 に対応した時点)。一部はコードと照合した
- 方法: Claude Code による設計書の通読
- 指摘: 17 件(方針を決めてから直す 6 件、設計書の食い違い 9 件、小さな点 2 件)。上の Build の実装のレビュー(1〜10)と重なるものは除いた

### 方針を決めてから直す

- [x] **D1. Build が古い生成ソースで動いても気づけない**(対応: master-gen が定義からメモリ上でコード生成し、出力先の `*.cs` と比べて違えば書き直す)(`src/MasterMemory.Addons.Tools/Build/BuildCommand.cs:99`、`:390` 付近)
  - **方針(2026-10-24 決定)**: マスタ生成が、定義からメモリ上でコード生成した結果と `source` のファイルを比べ、違えばコード生成をやり直してから続ける。サブコマンドは `code-gen` / `master-gen` に改名し、`make data` はなくす
  - Build は、CSV のヘッダの照合には `definitions` を、コンパイルと型には `outputs[].source` の生成ソースを使う。定義を変えて `make generate` を忘れると、新しい定義で CSV を確かめ、古い型・古い `MasterDataKey` でバイナリを作る。`ENV` を generate と build-data で取り違えたときも同じ。
  - 対応案: generate が定義のハッシュを生成ソースに出し、Build が今の定義と比べて、違えばエラーにする(`MasterDataKey` の計算と仕組みを共有できる)。
- [x] **D2. データ段の検証(未決定)が、client / server に分ける方式と合わない**(対応: 2026-10-25 に属性 `[Unique]` / `[Exists]` と `db.Validate()` の併用を決め、マスタ生成で実装した)(`docs/design/pipeline.md`「Validate の項目」)
  - **方針(2026-10-24 決定。未実装)**: 分ける前の全カラムの定義に対して1回だけ検証し、検証は属性で宣言してマスタ生成が行う(`IValidatable<T>` は使わない)。属性の種類と `db.Validate()` の併用は実装時に決める
  - `IValidatable<T>` を定義に書くと、検証のコードが client 用の出力(Unity)にも入る。`[ServerOnly]` の列・テーブルを参照すると client でコンパイルできない(`Exists` で server 専用のテーブルを参照する場合も同じ)。
  - 対応案: 「検証は、分ける前の全カラムの定義に対して1回だけ行う」を前提に置いてから、宣言の方法を決める。この前提では、属性で宣言してツールが検証する方が自然。
- [x] **D3. 復号の失敗や、定義とデータの食い違いを見分けられない**(対応: 平文のヘッダ(72 バイト)と `MasterDataBinary.Read` / `Write`、`MasterDataSchema.Hash`)(`src/MasterMemory.Addons.Core/AesMasterDataCipher.cs`)
  - **方針(2026-10-24 決定)**: client / server とも平文のヘッダ(マジック・形式のバージョン・スキーマのハッシュ・データのハッシュ)を付ける。スキーマのハッシュは両方に出す `MasterDataSchema.Hash`(client の鍵も兼ね、`MasterDataKey` はなくす。`namespace` は server でも必須)。Core に、ヘッダを確かめて中身の `byte[]` を返す読み込みの関数を足す。設計書の「改ざん」の記述は直した
  - CBC + PKCS7 だけで、MAC もヘッダもない。鍵が違うと多くは `CryptographicException` になるが、パディングの確認がたまたま通ると、壊れたバイト列が `MemoryDatabase` に渡る。
  - 設計書の「改ざんを難しくする」は、CBC だけでは改ざんを検出できないので不正確(鍵はクライアントにあるので、強くする意味は薄い)。
  - server のバイナリは暗号化しないので、スキーマの手がかりがない。
  - 対応案: 両方の target のバイナリに、スキーマのハッシュ(とデータのハッシュ)を持つヘッダを付ける。旧 master-data-pipeline との形式の互換と引き換え。D4 の土台にもなる。
- [x] **D4. 配信と「定義が変わると鍵が変わる」ことの関係が決まっていない**(対応: マスタ生成がバイナリごとに `masterdata.manifest.json` を出す。置き場所の前提は distribution.md に書いた)(`docs/design/distribution.md`)
  - **方針(2026-10-24 決定)**: マスタ生成がバイナリごとにマニフェスト(スキーマのハッシュ、データのハッシュ、作った日時)を出す。CDN の置き場所はスキーマのハッシュごとに分ける前提とし、distribution.md に書いた
  - CDN 上のバイナリは、それを読めるアプリのビルドと 1:1 に対応する。古いアプリが残る間は、スキーマごとに置き場所を分ける必要がある。
  - client と server が同じデータのリリースを使っているかを確かめる ID もない。
  - 対応案: 未決定の「バージョン管理」をこの点から書き直す。Build がマニフェスト(スキーマのハッシュ、データのハッシュ、作った日時)を出す案。
- [x] **D5. 使える型が少なく、範囲も決まっていない**(`src/MasterMemory.Addons.Tools/Common/CellConverter.cs`)
  - **方針(2026-10-24 決定)**: 今の `int` / `long` / `string` / `bool` / enum だけとする(入力をわかりやすくするため)。設計書に書いた
  - 使えるのは `int` / `long` / `string` / `bool` / enum だけ。`float` / `double`、`DateTime`(イベントの期間など)、配列がない。範囲外とも未決定とも書かれていない。
  - `definition-model.md` に「行の中の値型」とあるが、CSV で入れ子の型を表す方法は設計されていない。
  - 対応案: 対応する型と対応しない型を決定事項にする。
- [x] **D6. Validate を独立した機能にする必要があるか**(対応: `master-gen --check` と `make master-gen-check`)(`docs/design/pipeline.md`、`docs/dev/structure.md`)
  - **方針(2026-10-24 決定)**: 案 A。`master-gen --check`(何も書き込まない。生成ソースが古ければメモリ上の生成結果で検証し、警告する)
  - CSV の段の検証は Build に入っている。データ段の検証(`db.Validate()`)もコンパイルと行の変換が必要で、Build の前半と同じ処理になる。Validate はほぼ「書き込まない Build」。
  - 案 A: `build --check` のような Build のオプションにする(「1機能 = 1サブコマンド」の例外になる)。
  - 案 B: 別の機能のまま、処理を `Common/` で共有する。

### 設計書の食い違い・古い記述

- [x] **D7.**(対応: 項目名 `encrypt` に直し、未決定から決定済みの内容を除いた) `docs/design/pipeline.md`「暗号化」: 「項目名は暗号化を実装するときに決める」が古い(`encrypt` に決定済み)。決定済みの内容が「未決定 (暗号化)」の中にもある。
- [x] **D8.**(対応: 検証を `master-gen --check` とし、CSV の段は実装済みと分けて書いた) `docs/design/pipeline.md`: Validate を「未着手」としているが、「CSV の段 (本ツールで実装)」の項目は Build で実装済み。サブコマンドがないだけだと分けて書く。
- [x] **D9.**(対応: 試作の記録を build-prototype.md に移した) `docs/design/pipeline.md`: `status: draft` だが、実装済みの内容と試作の経緯が混ざって長い。試作の記録を別のページか `decisions.md` に移す。
- [x] **D10.**(対応: 位置指定 record は使えないと書いた。decisions.md にも追記) `docs/design/definition-model.md`: 位置指定 record を「generate では扱えるが推奨しない」としているが、`build.md` では Build で扱えない。実際には使えないと書く。
- [x] **D11.**(対応: 未決定から削除した) `docs/design/definition-model.md` の未決定「列単位だけで十分か」は、型単位の Only が実装済みで解決している。`decisions.md` の未決定にも載っていない。
- [x] **D12.**(対応: 「仮名」を削除した) `docs/guide/defining-master-data.md`: enum の説明に「(仮名。…)」が残っている(`InputName` に決定済み)。
- [x] **D13.**(対応: 指摘 7 の方針に合わせ、`target` を上書きできない項目にした) `docs/design/config.md`: `outputs.<name>.target` を上書きできる項目にしている。指摘 7 の結論に合わせて直す。
- [x] **D14.**(対応: 属性はコード生成で取り除かれるので、コンパイルには関係しないと書いた) `docs/design/build.md`: 「定義から使えるのは…本アドオンの属性の型」とあるが、生成ソースからは属性と `using MasterMemory.Addons;` が取り除かれるので、Build のコンパイルには関係しない。
- [ ] **D15.**(一部対応: 古い名前の行に「後に変更」を書き添えた。`status` は、README の定義に決定事項のページに合うものがないため、そのまま) `docs/design/decisions.md`: `status: implemented` は決定事項のページに合わない。古い名前(`.Generator`、`Runtime`)の行に「後に変更」と書き添える。

### 小さな点

- [x] **D16.** `MasterDataKey` は定義だけから決まるので、同じ定義(サンプルなど)を使う利用者は全員同じ鍵になる。設定 YAML に任意のソルトを足せる余地がある(D3 と一緒に考える)。
  - **方針(2026-10-24 決定)**: ソルトは足さない
- [x] **D17.**(対応: 指摘が誤りだった。サンプルには最初から enum と `[InputName]` の例がある。`Category` が `int` だったのは `definition-model.md` の例なので、そちらをサンプルに合わせた) `samples/Definitions/Item.cs` は `Category` が `int` で、enum と `[InputName]` の例がない。規則が多いので、サンプルに1つ入れる。
  - **方針**: 対応案のとおり

## 2026-10-24 Build の実装のレビュー

- 対象: Build の実装(generate・暗号化・設定ファイルを整えた変更)
- 方法: `/code-review high`(Claude Code)
- 指摘: 10 件(うち 3 件はレビューの中で再現を確認)

### 早めに直す(データを壊す、または正しい入力が通らない)

- [x] **1. generate が CSV を消しうる**(対応: `source` と `csv` が重なっていればエラー)(`src/MasterMemory.Addons.Tools/Common/OutputDirectory.cs:36`、再現済み)
  - **方針**: 対応案のとおり。config.md に規則を書いた
  - `OutputDirectory.Validate` は `source` を `definitions`・設定ファイルのディレクトリ・ほかの `source` とは比べるが、`csv` とは比べない。
  - 例: `csv: gen2/csv`、`source: gen2` で generate すると、終了コード 0 で CSV のディレクトリが丸ごと消える。
  - 対応案: `source` と `csv` が重なっていたら、何も消さずにエラーにする。
- [x] **2. 型の中に入れ子にした enum の列が、必ずエラーになる**(対応: 定義の enum に外側の型名を持たせて CLR の名前で照合し、`ValidateEnums` も C# と同じ順で探す)(`src/MasterMemory.Addons.Tools/Build/TableRowConverter.cs:44`、再現済み)
  - **方針**: 対応案のとおり
  - 定義から読んだ enum の名前(`N.T.Kind`)と、CLR の `Type.FullName`(`N.T+Kind`)を比べているため一致しない。
    `BuildCommand.ValidateEnums` は名前だけでも探すので、2つの確認の結果が食い違う。
  - 対応案: 入れ子の型を含めて名前の表し方をそろえて照合する(定義の読み込みで入れ子の型名も持つ)。
- [x] **3. ヘッダの末尾の空セルが「定義にない列」になる**(対応: ヘッダの末尾の空の列を無視し、その列に値があればエラー)(`src/MasterMemory.Addons.Tools/Build/BuildCommand.cs:276`)
  - スプレッドシートの書き出しで、1〜2行目のコメントがヘッダより右の列まであると、全行が空セルで埋められ、ヘッダが `...,SellPrice,` になる。
  - 対応案: ヘッダの末尾の空の列(とデータの同じ列)を無視する。空でない値が入っていればエラー。

### 直す(落ち方や表示の問題)

- [x] **4. 同じ名前の `MasterDataKey` 型が2つあると、例外で落ちる**(対応: マスタ生成はコンパイルした型から探さず、メモリ上のコード生成で計算したスキーマのハッシュを使う)(`src/MasterMemory.Addons.Tools/Build/MasterDataBinaryBuilder.cs:58`)
  - **方針**: `MasterDataSchema` への変更と一緒に直す(型を探すこと自体をやめた)
  - `SingleOrDefault(t => t.Name == "MasterDataKey")` が try/catch の外にあり、定義に同名の型があるとスタックトレースで落ちる。
  - 対応案: 生成した `MasterDataKey` を名前空間まで含めて探す(設定の `namespace` を使う)。見つからない・複数ある場合はエラーの表示と終了コード 1。
- [x] **5. `ulong` の大きな enum の値で、例外で落ちる**(対応: 値を decimal で数え、long に収まらなければ定義のエラー。小数のリテラルも整数として扱わない)(`src/MasterMemory.Addons.Tools/Common/DefinitionSchemaReader.cs:143`)
  - **方針**: `long` に収まらない値は定義のエラーにする(D5 で型を増やさないため)
  - `Convert.ToInt64` が `long.MaxValue` を超える値で `OverflowException` を投げる。CSV で使わない enum でも起きる。
  - 対応案: 値を `decimal` などで持つか、範囲外は定義のエラーとして表示する。
- [x] **6. ヘッダのエラーの行番号が、常に「4行」になる**(対応: `CsvTable` にヘッダの行の番号を持たせる。水平分割したファイルのヘッダの不一致も実際の行で出す)(`src/MasterMemory.Addons.Tools/Build/BuildCommand.cs:272`)
  - **方針**: 対応案のとおり
  - ヘッダは4つ目の「レコード」で、1〜3行目のコメントに引用符で囲んだ改行があると、実際の行とずれる。
  - 対応案: `CsvTable` にヘッダの行の番号(`records[3].Line`)を持たせて使う。

### 方針を決めてから直す

- [x] **7. 環境で `target` を client → server に変えると、必ずエラーになる**(対応: 環境の上書きに `target` を書いたらエラー)(`src/MasterMemory.Addons.Tools/Common/ConfigLoader.cs:105`、再現済み)
  - **方針(2026-10-24 決定)**: 案 A。環境では `target` を変えられない(書いたらエラー)。config.md に書いた
  - 基本の設定の `namespace`(と `encrypt: true`)を引き継ぎ、上書きで消す方法がない。`config.md` では `target` を上書きできる項目にしている。
  - 案 A(おすすめ): 環境では `target` を変えられないことにする(出力先の性質が環境で変わるのは不自然)。
  - 案 B: `target` が server になったら `namespace` / `encrypt` を引き継がない。
- [x] **8. コンパイル時の参照が広すぎて、Unity で通らない定義も build は通る**(対応: client は netstandard2.1 の参照アセンブリと netstandard 版の MasterMemory / MessagePack、C# 9。server は .NET の共有フレームワークと MasterMemory / MessagePack)(`src/MasterMemory.Addons.Tools/Build/InMemoryCompiler.cs:89`)
  - **方針(2026-10-24 決定)**: 案 B。client は `NETStandard.Library.Ref` 2.1.0(同梱)+ MasterMemory / MessagePack の netstandard 用 DLL、C# 9 に固定。server は .NET 10 の共有フレームワーク + MasterMemory / MessagePack。build.md に書いた
  - 参照が .NET 10 の全体(TPA)と Roslyn・YamlDotNet・Tools / Core まで含む。`build.md` の「定義から使えるのは MasterMemory・MessagePack・本アドオンの属性の型だけ」が守られない。
  - 案 A(おすすめ): 今は設計書に「守られていない」と明記して、後で対応する。
  - 案 B: 参照を netstandard2.1 の参照アセンブリと MasterMemory / MessagePack だけに絞る(参照アセンブリの入手方法を決める必要がある)。
- [x] **9. サンプルの dev 環境で、暗号化前提の Client のバイナリを平文で上書きする**(`samples/masterdata.yaml:34`)
  - **方針(2026-10-24 決定)**: dev でも暗号化する(サンプルの dev の `encrypt: false` を消す)。config.md の例は、暗号化しないバイナリを別のパスにする形に直した
  - dev は unity の `source` を `../out/sample/unity-dev` に変え、暗号化を切るが、`binary: Client/masterdata.bytes` はそのまま。
  - 対応案: dev では `binary` も別のパス(`../out/sample/unity-dev.bytes` など)にする。

### 性能

- [x] **10. 同じ target の出力先ごとに、コンパイルし直している**(対応: 生成ソースの中身のハッシュごとにコンパイルの結果を使い回す。`ReadCsvTables` の2回の列挙は 指摘 3 の対応で解消済み)(`src/MasterMemory.Addons.Tools/Build/BuildCommand.cs:331`)
  - **方針**: 対応案のとおり
  - 1回あたり約 1.2〜1.5 秒。server の出力先が2つあれば、同じソースを2回コンパイルする。`ReadCsvTables` も、テーブルごとに CSV のディレクトリを2回列挙している。
  - 対応案: `CompilationResult` を `source` の内容(または target)ごとにキャッシュする。

→ [log](log.md)
