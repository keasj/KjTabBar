# KjTabBar v1.3.2.0

## 変更内容
- コントロールパネルのルートが別の GUID 形式で返される場合も、新しいウィンドウを正しく認識するよう変更。
- Explorer の切替時、タブバーを再接続する前に切替先の位置を揃えるよう変更。
- 環境変数 KJTB_DIAGNOSTICS=1 で切替処理の診断ログを有効にできるよう追加。通常の Release 実行時は診断ログを出力しない。

## 検証結果
- dotnet build KjTabBar.build.sln -c Release: 成功、警告0件・エラー0件。
- dotnet test UnitTestProject/UnitTestProject.csproj -c Release --no-restore: 全291件成功。
- 回帰テスト2件を追加し、既存2件に再接続前の位置調整の検証を追加。
- devenv.com による日英インストーラーの Release ビルド: 両方成功。起動時に NuGet 復元メッセージが出たが、最終ビルド結果は成功2・失敗0。
- 日英 MSI から展開した EXE を対象に vstest.console.exe を実行: それぞれ全291件成功。
- 日英 MSI の ProductVersion=1.3.2、内包 EXE のバージョン=1.3.2.0。日英内包 EXE と単体配布 EXE の SHA-256 一致。
- 日英 ProductCode を更新、UpgradeCode を維持。LICENSE 同梱を確認。
- 日英 MSI の終了処理は InstallExecuteSequence 1399、InstallValidate は1400。
- git diff --check と tools/Check-LineEndings.ps1 成功。既存ファイルへの追加編集なし。変更対象の CRLF と BOM を維持。
- C# 7.3 / .NET Framework 4.8.1 を維持。新規 NuGet パッケージなし。

## 配布物の識別情報
- 日本語 MSI PackageCode: {DC04CEFB-B75C-4617-8516-5ECFAD287586}
- 英語 MSI PackageCode: {E171C061-46DA-487D-8083-10172C6FBA96}

| 配布物 | SHA-256 |
|---|---|
| KjTabBar-v1.3.2.0-setup-en.exe | F53241723E5DDC051AE31D77610F4A989E3A6601404CA2F6B2AA9BD767AC8905 |
| KjTabBar-v1.3.2.0-setup-en.msi | 071DF1A0A9DA889BBFFBC78FC5B2307011BAE9446F1E7D0F0595435AD3B7CE3D |
| KjTabBar-v1.3.2.0-setup.exe | F53241723E5DDC051AE31D77610F4A989E3A6601404CA2F6B2AA9BD767AC8905 |
| KjTabBar-v1.3.2.0-setup.msi | 74C267B01F4BCBF7292337081C4B242C92A3DBBC4966CD0025A9F95A7453AEA9 |
| KjTabBar-v1.3.2.0.exe | 3F154F5AFFF08D3723AD901C2454B9AE859A952625962E937009124FFDFBB547 |

## 未検証
- 実 Explorer でのタブ切替、表示位置、コントロールパネル操作。
- 旧版からの実インストール／更新。検証は MSI の展開のみで、インストール済みアプリは更新していない。
