# PC Mode Switcher 0.2

PC Mode Switcherは、Windows 11の画面OFF時間、スリープ時間、電源プランを、GAME / WORK / NORMALの3モードとしてまとめて切り替えるWPFアプリです。

## 安全性

- WinBridgeとは独立したプロジェクトです。
- 電源設定の変更にはWindows Power API（`powrprof.dll`）だけを使用し、電源設定のレジストリ値を直接編集しません。
- Windowsログイン時の自動起動を有効にした場合だけ、現在のユーザーの `Run` キーへ起動情報を登録します。無効にすると削除します。
- 管理者権限を要求しません。
- 利用可能な電源プランだけを列挙し、存在しないプランは適用しません。
- 画面OFFまたはスリープのAC/DC書き込みが途中で失敗した場合、その項目を変更前の値へ戻します。

## ビルド

```powershell
dotnet build .\PCModeSwitcher.csproj -c Release
```

実行ファイルは通常 `bin\Release\net8.0-windows10.0.22621.0\win-x64\PCModeSwitcher.exe` に作成されます。

## 配布パッケージ

次のコマンドで、.NETの追加インストールが不要な自己完結・単一EXEのwin-x64版を作成できます。

```powershell
.\scripts\Publish-Release.ps1
```

`artifacts` フォルダーへ次のファイルが作成されます。

- `PCModeSwitcher-v0.2.0-win-x64.zip`
- `SHA256SUMS.txt`

ZIPには実行ファイル、利用者向けREADME、リリースノートが含まれます。コード署名は行っていないため、ダウンロードした環境ではSmartScreenの警告が表示される場合があります。

設定は `%LOCALAPPDATA%\PCModeSwitcher\settings.json` に保存されます。

設定メニューでは、右上の閉じるボタンを押したときにWindowsの通知領域へ格納するか、アプリを終了するかを選べます。通知領域アイコンはダブルクリックで再表示でき、右クリックメニューからGAME / WORK / NORMALの直接適用と完全終了ができます。現在適用中のモードにはチェックが付き、通知領域から切り替えた結果は通知で確認できます。格納時の通知も設定で有効・無効を選べます（既定は無効、有効時も起動中の初回だけ表示）。

同じ設定メニューから、Windowsログイン時の自動起動と、GAME / WORK / NORMALを直接適用するグローバルショートカットを設定できます。Windowsログイン時は画面を開かず通知領域で起動します。ショートカットはアプリが通知領域にある間も使用でき、重複・Windows予約キー・他アプリとの競合を検出します。既定ではすべて未設定です。

アプリはユーザーセッション内で1つだけ起動します。起動済みの状態でもう一度実行すると、2個目は終了し、既存のウィンドウを通知領域から復元して前面に表示します。

アプリアイコンの原本は `Assets\AppIcon.png`、Windows用の複数解像度アイコンは `Assets\AppIcon.ico` です。

## テスト

```powershell
dotnet run --project .\tests\PCModeSwitcher.Tests\PCModeSwitcher.Tests.csproj -c Release
```

既定モード、JSONの保存と再読み込み、スタートアップ起動引数、多重起動の検出と通知、通知領域向けのモード適用、Windows Power APIによる電源プランの読み取りを検証します。実際のWindows設定は変更しません。
