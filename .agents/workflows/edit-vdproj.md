---
description: .vdproj ファイル（Visual Studio セットアッププロジェクト）を安全に編集する手順
---

# .vdproj ファイルの編集ルール

## 重要: エンコーディング

- `.vdproj` ファイルは **Shift-JIS（コードページ 932）** でエンコードされている。
- 絶対に UTF-8 で読み書きしてはならない。日本語が不可逆に破壊される。

## 読み書きの手順

### 読み込み
```powershell
$sjis = [System.Text.Encoding]::GetEncoding(932)
$allLines = [System.IO.File]::ReadAllLines("path\to\Setup.vdproj", $sjis)
```

### 書き出し
```powershell
$sjis = [System.Text.Encoding]::GetEncoding(932)
$content = [String]::Join("`r`n", $allLines)
[System.IO.File]::WriteAllText("path\to\Setup.vdproj", $content, $sjis)
```

### 禁止事項
- `Get-Content -Encoding UTF8` で読まない
- `[System.Text.Encoding]::UTF8` で読み書きしない
- `write_to_file` / `replace_file_content` ツールで直接編集しない（UTF-8で書き出してしまうため）
- BOM を付けない（元ファイルにBOMは無い）

## CustomActionData の書式

- `.vdproj` 内での文字列値は `"8:値"` の形式
- 値の中にダブルクォートを含めたい場合は `""` でエスケープするが、末尾に `\"` が来るとパーサーが壊れる
- **安全なパターン**: クォートを含めない形式を使う
  ```
  "CustomActionData" = "8:/targetdir=[TARGETDIR]"
  ```
- C# の `Context.Parameters` 側でパスのトリムや正規化を行う

## カスタムアクションの追加

- **必ず Visual Studio の GUI から追加する。手動で .vdproj にエントリを追加してはならない。**
- GUI での手順:
  1. Setup プロジェクトを右クリック → 「表示」→「カスタム動作」
  2. 対象ノード（インストール / コミット / ロールバック / アンインストール）を右クリック → 「カスタム動作の追加」
  3. 「アプリケーション フォルダー」→ 「プライマリ出力」を選択
- GUI が生成する正しい GUID は `{4AA51A2D-7D85-4A59-BA75-B0809FC8B380}` で、`InstallerClass = "11:TRUE"` などのプロパティが必要
- 手動で使っていた GUID `{1A39CB38-16CE-484A-B63D-3E522B8E6CB5}` は別の種類のカスタムアクション用であり、Install フェーズでは動作しない
