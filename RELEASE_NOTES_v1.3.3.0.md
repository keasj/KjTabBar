# KjTabBar v1.3.3.0

## 変更内容
- コントロールパネルのタブを閉じた後、通常フォルダーをコントロールパネル用ウィンドウに直接表示していた処理を修正。移動先のエクスプローラーを準備してからタブを閉じるよう変更。
- 閉じるボタン、右クリックの「タブを閉じる」「右側のタブを閉じる」「左側のタブを閉じる」に適用。
- 最後のタブを閉じる場合もホーム用のウィンドウを準備。切り替えに失敗した場合はタブと閉じたタブの履歴を保持。
- 非同期処理中にタブ構成が変わった場合は、別のタブを誤って閉じないよう中止。

## 検証結果
- Visual Studio MSBuild による Release 再ビルド成功。
- 日英インストーラーの Release ビルド成功。各初回に WPF 生成ファイル不足が発生したため、アプリの再ビルド後に再実行し、各「成功2・失敗0」を確認。
- 日英 MSI から展開した実行ファイルで vstest.console.exe を実行し、それぞれ全298件成功（今回の回帰テスト7件を含む）。
- MSI ProductVersion=1.3.3、内包 EXE FileVersion=1.3.3.0。
- 日英 MSI の内包 EXE と単体配布 EXE の SHA-256 が一致。
- 日英 ProductCode を更新し、UpgradeCode を維持。
- ビルド後の終了処理パッチ成功。InstallExecuteSequence の終了処理1399が InstallValidate 1400より前であることを確認。
- 既存ファイルの CRLF・UTF-8・BOM の有無を維持。git diff --check と tools/Check-LineEndings.ps1 成功。

## 配布物
保存先: Setup/Release/v1.3.3.0/

- 日本語 MSI PackageCode: {A870BE8A-C882-4B1F-92F6-6802C346CFDB}
- 英語 MSI PackageCode: {FB971B2F-91EC-4A7A-A208-16DC5BDEFFD9}

## 未検証
- 実 Explorer で、コントロールパネルのタブを閉じた後に Windows 11標準タブが表示され続けることの確認。
- インストール済みアプリの更新と旧版からのアップグレード確認。MSI は検証用ディレクトリへ展開したのみ。

