# KjTabBar v1.3.1.0

## 変更内容
- コントロールパネルから電源オプションなどへ移動した際、移動元のタブを引き継ぎ、以前のページへの不要な再移動を防ぐ。
- 同じ項目の別タブがある場合も、操作中のタブを優先して引き継ぐ。
- ウィンドウ表示時に起動元を記録し、前面ウィンドウの変化や処理の遅延後も引き継ぎ判定に使用する。
- 保存したコントロールパネル項目の復元では、専用ウィンドウの準備後に移動し、二重起動と復元途中のタブ情報の上書きを防ぐ。
- 電源オプションや記憶域などの復元時は、親のコントロールパネルを先に開いて「戻る」の履歴を作る。

## 検証結果
- Release ビルド成功（警告0件、エラー0件）。
- 回帰テスト8件を追加。全289件成功。
- 日英インストーラーの Release ビルド成功。英語版は手順書に従い WPF 生成物を再生成してから作成。
- 日英 MSI から展開した EXE をそれぞれ対象として、全289件成功。
- 日英 MSI の ProductVersion=1.3.1、内包 EXE のバージョン=1.3.1.0 を確認。
- ProductCode と PackageCode を更新し、既存の UpgradeCode を維持。LICENSE 同梱を確認。
- 実行中アプリの終了処理は InstallExecuteSequence 1399、InstallValidate は1400。
- git diff --check と tools/Check-LineEndings.ps1 成功。既存ファイルへの追加編集なし。変更対象は UTF-8、CRLF。
- C# 7.3 / .NET Framework 4.8.1 を維持。新規 NuGet パッケージなし。

## 配布物の識別情報
- 日本語 MSI PackageCode: {6A6A2D6A-5019-46F2-B0CD-D56DAA925BAF}
- 英語 MSI PackageCode: {FB2191E2-1535-41ED-ABE3-A922B3E1DD22}

| 配布物 | SHA-256 |
|---|---|
| 日本語 MSI | 4D800217660CC97FB202F9D8CA694BC46268E3334D90B03C5DFD2EB1AA3724C9 |
| 日本語 MSI 内 EXE | 124A59D9D19816802BD329A6A4AE8847E5BE29EFC9A92D022A5BB21AF0444ACB |
| 英語 MSI | BA5C5884C9210591CA3772C71259B73D7E8A0519534AE1FC395B98A9767A95C9 |
| 英語 MSI 内 EXE / 単体 EXE | 42C94088C6DAC73474F89D25A0ADAD74094FF6C99A4C67EAC961FC0F33AFE2D0 |

## 未検証
- 実 Explorer でのタブ操作、コントロールパネル項目の復元と「戻る」操作。
- 旧版からの実インストール／更新。インストール済みアプリは更新していない。
