# KjTabBar v1.3.5.0

## 原因
- 2026年9月19日18:10の Explorer クラッシュでは、ダンプの例外情報が NVIDIA の nvui.dll（8.17.16.1692）の CResourceException を指していた。Firefox 終了との因果関係や、失敗したリソースの種類は未特定。
- クラッシュ後も残ったフォルダー画面は、Shell.Application.Windows の操作対象一覧に登録されていなかった。実機で、その画面は登録なし・現在のパス取得不可、新しい画面は登録あり・パス取得可能であることを確認した。
- KjTabBar はウィンドウの存在を確認しても Shell 登録の消失を検出せず、操作できない画面を使い続けていた。対象を見つけられない Navigate は失敗を返すため、タブを押してもフォルダー表示が変わらなかった。

## 変更内容
- タブ操作前に Explorer の Shell 登録を確認する。対象が見つからない場合は COM キャッシュを破棄して再確認する。
- 登録が失われた場合は、既存のホスト切り替え処理を使い、タブ一覧を保持して新しい Explorer 画面へ接続し直す。
- 退避中の画面と新しい接続先の登録も確認し、操作できない画面への切り替えを防ぐ。
- 復旧要求の重複実行を抑制する。登録確認や新しい画面の準備が失敗した場合は、元のタブ状態を保持する。
- 本体の AssemblyVersion / AssemblyFileVersion を 1.3.5.0、日英インストーラーの ProductVersion を 1.3.5 に更新。ProductCode / PackageCode を更新し、UpgradeCode は維持する。

## 検証と限界
- v1.3.5.0 の Release ビルド成功（警告0・エラー0）。全376件のテスト成功。本体 EXE の FileVersion / ProductVersion が 1.3.5.0 であることを確認。
- 本体出力: artifacts/v1.3.5.0/KjTabBar.exe（実行中の修正版とは別フォルダーへ生成）。
- 修正前の再現テストで復旧処理が実行されないことを確認し、修正後に成功を確認した。
- バージョン更新前の修正版で、ユーザーによる dev / KjTabBar のタブ切り替え成功と、両操作のナビゲーション開始ログを確認した。
- NVIDIA 側のクラッシュ自体を修正する変更ではない。実際の再クラッシュ後の自動復旧、長時間動作、インストーラーの更新動作は未検証。
- 日英インストーラーの Release ビルド成功（各2プロジェクト成功・失敗0）。Visual Studio が途中で出した復元エラー表示の後、復元完了とビルド成功を確認。
- 日英 MSI の ProductVersion=1.3.5、内包 EXE の FileVersion=1.3.5.0 を確認。
- 日英 MSI から展開した実行ファイルで、それぞれ全376件のテスト成功。
- 日英 MSI の終了処理パッチを確認（KjTabBarCloseRunningProcess=1399、InstallValidate=1400）。

## 配布物
保存先: Setup/Release/v1.3.5.0/

- KjTabBar-v1.3.5.0.exe
- KjTabBar-v1.3.5.0-setup.exe / KjTabBar-v1.3.5.0-setup.msi（日本語）
- KjTabBar-v1.3.5.0-setup-en.exe / KjTabBar-v1.3.5.0-setup-en.msi（英語）
- 日本語 MSI 内包 EXE SHA-256: FAB22EACA7A9E0D0638E7F477AB31B1E1730F10EE94959331B38933A4B8D07DB
- 英語 MSI 内包 EXE・単体配布 EXE SHA-256: E360EC7F82842B3F7877706E8DC77C1D0BDCE728CC967D057AE379DE7A5F2730
