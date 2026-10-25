---
title: コーディング・テストのルール
status: implemented
updated: 2026-10-24
---

# コーディング・テストのルール

C# のコード・テストを書くときに読む。詳細な設定は `.editorconfig`。

## コードスタイル

- `.editorconfig` に従う (Unity プロジェクトの設定を参考にしている)。
- Allman。**`var` を常に使う**。
- private / internal のフィールドは **接頭辞なしの camelCase**(`_` は付けない)。
- public / protected のフィールド・プロパティと、型・メソッドは PascalCase。定数と static readonly も PascalCase。
- `.cs` は BOM 付き UTF-8、改行は LF。
- コード中の識別子は英語、コメントは日本語。

## Unity に入るコード

対象: Attributes / Core / Unity と、コード生成の出力・定義 (ソースのまま Unity に入る)。

- ブロック形式の `namespace` を使い、C# 10 以降の構文は避ける (Unity は C# 9)。
- `required` は使えない。`init` は `IsExternalInit` の polyfill が必要。
- クライアントで実行されるコードでは、実行時の GC アロケーションを避ける (モバイル)。

## テスト

- xunit を使う。
- テストメソッドの先頭に、何を確かめるテストかと、期待する結果をコメントで書く。`[Description]` や `DisplayName`
  は使わない (xunit はこれを表示しないため)。

  ```csharp
  [Fact]
  public void Decrypt_ReadsLegacyFormat()
  {
      // 検証: 旧 master-data-pipeline の手順で暗号化したデータを復号する
      // 期待: 元のデータと一致する
      ...
  }
  ```

- メソッド名は `対象_条件_結果` の形 (`.editorconfig` でアンダースコアを許可している)。
- 置き場所とファイル名は [structure.md](structure.md) の「フォルダ・名前空間」。

## パッケージ

- NuGet パッケージのバージョン (Roslyn、xunit など) は未確認で書いている。restore に失敗したら利用可能なバージョンに直す。
