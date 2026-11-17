# 実Explorer用の境界試験フック

これは受入試験用のビルドにだけ組み込む機能です。製品設定・レジストリ・コントロールパネルの判定には変更を加えません。通常ビルドでは、環境変数や指示ファイルが存在しても注入・追加ログは無効です。

## 起動

1. 通常起動中のKjTabBarを終了し、必要な保存タブデータを退避します。A・Bという試験用フォルダーと、プログラムと機能・電源オプションの保存タブを用意してください。実ファイルやOS設定を削除・変更する必要はありません。
2. リポジトリのルートで、専用の出力先にビルドします。通常の配布物にこのビルドを混ぜないでください。

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' KjTabBar.build.sln /t:Build /p:Configuration=Release /p:KjtbAcceptanceHooks=true /p:OutDir="$PWD\artifacts\acceptance-enabled\" /m:1 /v:minimal /nologo
```

3. **管理者ではないPowerShell**から起動します。管理者から起動すると、通常ユーザーへの再起動経路で環境変数が引き継がれない場合があります。起動済みの別インスタンスがある場合も、そのインスタンスに変数は反映されません。

```powershell
$testRoot = Join-Path $PWD 'artifacts\acceptance-commands'
$savedDiagnostics = $env:KJTB_DIAGNOSTICS
$savedAcceptance = $env:KJTB_ACCEPTANCE_DIR
try {
    $env:KJTB_DIAGNOSTICS = '1'
    $env:KJTB_ACCEPTANCE_DIR = $testRoot
    $testProcess = Start-Process -FilePath "$PWD\artifacts\acceptance-enabled\KjTabBar.exe" -WindowStyle Hidden -PassThru
} finally {
    $env:KJTB_DIAGNOSTICS = $savedDiagnostics
    $env:KJTB_ACCEPTANCE_DIR = $savedAcceptance
}
$testProcess.Id
```

環境変数は起動するプロセスにだけ渡し、ユーザー／システムの環境変数として保存しません。指示先は `$testRoot/<メインプロセスID>/` です。`--kjtb-shell-worker` の子プロセスIDではありません。

## 指示の投入

```powershell
.\tools\Set-AcceptanceFault.ps1 -Root $testRoot -MainProcessId $testProcess.Id -Action reject
```

移動の指示は**次のタブ選択による実移動要求**で一回だけ消費します。現在地が既に対象パスなら要求が発生せず、指示は残ります。別ウィンドウの操作や起動時の保存復元も対象になるため、他の操作が止まった状態で投入してください。投入から60秒を超えた指示、不正形式、128バイト超の指示は消費して無視します。指示は同名ファイルを上書きしません。取り消す場合は表示された指示ファイルだけを削除してください。

| Action | 動作 |
| --- | --- |
| `reject` | 要求を送らずfalseを返す |
| `throw` | 要求を送らずTimeoutExceptionを返す |
| `drop` | 要求を送らずtrueを返す。実際の5秒タイムアウト経路を観測する |
| `delay -DelayMs 3000` | 指定時間待って実Explorerへの要求を実行し、その結果を返す |
| `late-throw -DelayMs 3000` | 即座に例外を返した後、指定時間後に実Explorerへの要求を実行する |
| `rollback-reject` | 次のホスト復帰を、元ウィンドウを壊さず拒否する |
| `pause-sync` / `resume-sync` | 同期処理を停止／再開する。停止は30秒で自動失効 |
| `grace` / `end-grace` | 猶予判定を強制的に有効／解除する。30秒で自動失効 |

遅延は1〜30000ms。遅延中にホストが切り替わる（元のホストへ戻る場合も含む）、対象タブが消える・パスが変わる、ViewModelが破棄される場合は実要求を破棄します。`late-throw`は指示消費後にファイルを消しても取り消せません。試験アプリを終了すれば停止します。実要求の受理は到着の証明ではなく、到着は別途同期ログで確認します。

`grace`は**試験のために猶予を人工的に作るもの**で、実際にホスト切替後750ms以内だったことの証明ではありません。正確な750ms境界を確認するときは使用せず、ログの `hostAgeMs` と `grace` で判定します。`pause-sync`も通常の1秒周期を意図的に止める試験です。

## ログ

既存の `%APPDATA%\KjTabBar\Logs\KjTabBar.log` に `[DIAG] AcceptanceTest:` が出ます。新しいログは生のパスや選択ファイル名を記録せず、PID、単調時計tick、ホスト、同期世代、選択番号、保護状態、保留状態、観測パスとの一致、キャンセル追跡との一致、猶予判定、ホスト切替後の経過msを記録します。既存の診断ログと同じローテーション・秘匿処理を使います。

主なイベント:

- `rollback-rejected` → `rollback-protected`: 復帰失敗による保護成立。
- `navigate-rejected` / `navigate-thrown` / `navigate-accepted-without-request`: 指示が消費された証拠。
- `delayed-real-request accepted=True`: 遅延した実要求を受理。到着済みという意味ではありません。
- `sync-observed` → `sync-arrival-confirmed`: 観測と選択確定。選択番号・保護解除を比較。
- `click-read` → `click-arrival-confirmed`: クリック処理による到着確認と保護解除。
- `sync-protected` / `sync-paused` / `navigation-timeout`: 保存パス保護・試験停止・実タイムアウト。
- `delayed-request-discarded`: 古いホスト等への遅延要求を破棄。

## 再現手順

### 共通: 復帰失敗状態を作る

1. 通常フォルダーAを選択し、移動が完了した状態で止める。
2. `reject`と`rollback-reject`をそれぞれ投入する。
3. プログラムと機能の**保存タブをクリック**し、CPホスト切替を起こす。
4. ログで `navigate-rejected`、`rollback-rejected`、`rollback-protected protected=True` を確認する。元ホストの終了は不要。

移動要求やホスト切替が起きなければ指示は消費されません。ログがそろわない場合、以降を合格にせず前提未成立とします。

### 拒否・例外・未到着からの再開

共通状態から、同じCPホスト内の電源オプションを、`reject`・`throw`・`drop`を一つずつ投入してクリックします。各試行後、保存パス保持を確認します。`drop`は同期を停止せず6秒以上待ち、`navigation-timeout`と保護の維持を確認します。指示なしで再度クリックし、到着後に保護が解除され通常同期へ戻ることを確認します。

### 猶予中の遅延到着

1. 共通の復帰失敗状態を作る。
2. `grace`、`late-throw -DelayMs 3000`を投入する。
3. 同じCPホストの電源オプションのタブをクリックする。
4. 一度失敗扱いで選択が戻り、その後実Explorerが電源オプションへ到着することを観察する。
5. `sync-observed cancelledMatch=True syntheticGrace=True` の後、`sync-arrival-confirmed`で到着先の選択番号と`protected=False`を確認する。
6. `end-grace`を投入し、Explorer側で別ページへ移動する。到着先タブだけが追従し、元の保存タブが変わらないことを確認する。

自然の750ms境界を確認する別試験では、`grace`を使わず短い遅延値で試行します。`sync-observed`の`hostAgeMs`が0以上750未満かつ`syntheticGrace=False`にならなければ、該当条件は未再現です。

### クリックで到着確認後、次の同期前に別の場所へ移動

1. 共通の復帰失敗状態を作る。
2. `pause-sync`、`late-throw -DelayMs 2000`を投入し、電源オプションのタブをクリックする。
3. 実Explorerが電源オプションへ到着したことを画面で確認する。同期停止中なので、タブの選択確定はまだ行われない。
4. 電源オプションのタブを再度クリックする。`click-arrival-confirmed protected=False`を確認する。
5. 同期停止の30秒以内に、Explorerのアドレス欄で試験用フォルダーBへ移動する。
6. `resume-sync`を投入する。選択タブにBが反映され、他の保存タブが保持されることを確認する。

`click-arrival-confirmed`が出ない場合は、保留中の移動を共有した、現在地が違う、または別ホストへの切替が起きた可能性があります。目的の条件が成立していないため、合格とは判定しません。

## 終了・通常版への復帰

試験アプリを通常終了し、退避した保存タブを必要に応じて復元します。通常版は `/p:KjtbAcceptanceHooks=false` で**別の出力先**へビルドしてください。試験用フラグを外した通常版では注入コードの環境変数読取り部分がコンパイルされず、試験フォルダーにアクセスしません。指示フォルダーはPIDごとに分離され、古い指示は期限切れで無視されます。

テストは専用ビルドの `AcceptanceTestHooksTests` を実行します。通常ビルドでは無効化のテストだけを実行し、注入を必要とするテストはInconclusive（スキップ）になります。実Explorerを使う試験は、これらのモック境界テストとは別に判定してください。
