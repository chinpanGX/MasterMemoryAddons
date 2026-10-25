---
title: マスタ生成(Build)の試作の記録
status: implemented
updated: 2026-10-24
---

# マスタ生成 (Build)の試作の記録

マスタ生成 (当時の名前は Build)の実行形態を決めるための試作の記録。結果から C に決定した (2026-10-24。[decisions](decisions.md))。
試作のコード (`spike/`) は Build の実装後に削除した。今の仕様は [master-gen](master-gen.md)。

## 確かめたこと

| 確かめること | 方法 |
|---|---|
| generate の出力が、Unity と同じ制約 (`netstandard2.1`、C# 9) で MasterMemory と一緒にコンパイルできるか | `samples/Client`(`netstandard2.1`、`LangVersion 9`)に client の出力を置いてビルドする |
| サーバー用の出力も同様にコンパイルできるか | `samples/Server`(`net10.0`)に server の出力を置いてビルドする |
| `DatabaseBuilder` / `MemoryDatabase` がどの名前空間に生成されるか | 上の2つのビルド結果を見る |
| 別のプロセス (Tools) から、コンパイル済みのアセンブリを読み込み、リフレクションでバイナリを作れるか | 試作用のコンソール (`spike/BuildSpike`、試作後に削除) から `samples/Client` の DLL を読み、行を追加して `Build()` し、暗号化・復号して `MemoryDatabase` で読み直す |

## 結果

2026-10-24、MasterMemory 3.0.4 / MessagePack 3.1.12 / .NET SDK 10.0.401。

- **client の出力は `netstandard2.1` + `LangVersion 9` で、server の出力は `net10.0` で、どちらもコンパイルできた。**
  MasterMemory の Source Generator も動いた。init には `IsExternalInit` の polyfill を置いた (`samples/Client/IsExternalInit.cs`)。
  実際の Unity では未確認。
- **`DatabaseBuilder` / `MemoryDatabase` / `ImmutableBuilder` / `MasterMemoryResolver` は、プロジェクトの `RootNamespace`
  (`Sample.MasterData`)に生成された。** テーブルの型は定義の名前空間 (`Sample.Definitions`)のまま。
- **別のプロセスから DLL を読み、リフレクションだけで「行の追加 → `Build()` → 暗号化 → 復号 → `MemoryDatabase` で読み直し」
  ができた。** `init` のプロパティも `PropertyInfo.SetValue` で設定できる。client 用には `DropTable`(`[ServerOnly]`)が入らず、
  `MasterDataKey.Value` も DLL からリフレクションで読めた。
- 読み込みの注意点が2つあった。
  - DLL の依存 (MasterMemory、MessagePack) を読むには、そのプロジェクトで `CopyLocalLockFileAssemblies` を `true` にして、
    依存を bin に出す必要がある。
  - `netstandard2.1` の bin には `System.Memory.dll` などの互換 DLL が入る。これを別に読み込むと `ReadOnlySpan` などが
    別の型になり、`MemoryDatabase` の型が読み込めない (`Method 'Init' ... does not have an implementation`)。
    **実行環境 (.NET 10) にあるアセンブリはそちらを優先して読む**必要がある。
- MasterMemory 3.0.4 が依存する MessagePack 3.1.3 には、既知の脆弱性の警告 (高 3件を含む)が出る。**MessagePack は 3.1.12
  を明示して参照する。**

## C (Tools の中でメモリ上にコンパイルする) の試作

`spike/InMemoryBuildSpike`(試作後に削除)。generate の出力を Roslyn の `CSharpCompilation`(C# 9)に入れ、
MasterMemory と MessagePack の Source Generator をその場で動かし、メモリ上に出力した DLL からバイナリを作った。

- **client 用・server 用とも、通常のビルド (`samples/Client` / `samples/Server` の DLL) と、同じ行からバイト単位で同じバイナリができた**
  (client 109 bytes、server 198 bytes)。通常のビルドの `MemoryDatabase` でも読み直せた。
- Source Generator は、パッケージの DLL を Tools の出力先にコピーして実行時に読み込む
  (`MasterMemory` の `analyzers/dotnet/cs/MasterMemory.SourceGenerator.dll`、`MessagePackAnalyzer` の
  `analyzers/roslyn4.3/cs/MessagePack.SourceGenerator.dll`)。どちらも Roslyn 4.3 向けで、Tools の Roslyn 4.12 で動いた。
- `DatabaseBuilder` などの名前空間は、通常のビルドと同じく `build_property.RootNamespace` を generator に渡して指定できた。
- 参照は、実行環境 (.NET 10) のアセンブリと、Tools が持つ MasterMemory / MessagePack。.NET 10 の上でコンパイルするので、
  A で出た読み込みの問題 (`System.Memory` など、`CopyLocalLockFileAssemblies`) は起きない。
  (2026-10-24 に、参照を絞ることにした。[master-gen](master-gen.md) の「コンパイル」)
- 時間は、コンパイル (generator を含む) が約 1.2〜1.5 秒。
- 制約: バイナリは **Tools が持つ MasterMemory / MessagePack のバージョン**で作られる。定義が、MasterMemory・MessagePack
  以外の型 (利用者のライブラリなど) を使うとコンパイルできない。独自の暗号化は、実装を含む DLL を別に指定する必要がある。

## 比べた候補

C の欄は試作の結果。**C に決定した**。

| | A. 利用者の .NET プロジェクトの DLL を読む | B. Tools が一時的なプロジェクトを作ってビルドする | C. Tools の中でメモリ上にコンパイルする |
|---|---|---|---|
| 流れ | generate → 利用者が `dotnet build` → Tools が DLL を読んでバイナリを作る (Makefile でつなぐ) | generate → Tools が生成ソースを含む一時プロジェクトを作り、`dotnet build` してから読む (旧 `csharp_converter` に近い) | generate → Tools が生成ソースを読み、Roslyn でコンパイルしてバイナリを作る |
| 利用者が用意するもの | target ごとに、生成ソースを含む .NET プロジェクト。Unity はそのまま使えないので、client 用は `samples/Client` のようなプロジェクトが別に要る | なし (設定 YAML に MasterMemory のバージョンなどを書く) | なし |
| MasterMemory のバージョン | 利用者のプロジェクトのバージョン | 設定 YAML で指定できる | Tools が持つバージョンに固定 |
| 定義が別のアセンブリの型やパッケージを使う場合 | 利用者のプロジェクトで参照すれば動く | 設定 YAML で追加の参照を指定する仕組みが要る | 同左 (または、使えないという制約にする) |
| 独自の暗号化 (`IMasterDataCipher`) | 同じ DLL の中から実装を探せる | 実装を含む DLL を設定 YAML で指定する仕組みが要る | 同左 |
| 読み込みの注意点 | `CopyLocalLockFileAssemblies` が必要。実行環境のアセンブリを優先して読む必要がある | A と同じ | なし |
| 速さ | 利用者のビルド + 読み込み | `dotnet build` の分だけ遅い (数秒〜) | 約 1.5 秒 (試作) |
| 構成ルール ([structure](../dev/structure.md)) との相性 | 機能をファイルで受け渡し、Makefile でつなぐ形にそのまま合う | Build の中で `dotnet build` を呼ぶ | 生成ソースをファイルで受け取る形で合う |

→ [master-gen](master-gen.md) / [pipeline](pipeline.md) / [decisions](decisions.md)
