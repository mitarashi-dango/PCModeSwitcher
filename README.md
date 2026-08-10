# PC Mode Switcher 0.1

PC Mode Switcherは、Windows 11の画面OFF時間、スリープ時間、電源プランを、GAME / WORK / NORMALの3モードとしてまとめて切り替えるWPFアプリです。

## 安全性

- WinBridgeとは独立したプロジェクトです。
- Windows Power API（`powrprof.dll`）だけを使用し、レジストリは変更しません。
- 管理者権限を要求しません。
- 利用可能な電源プランだけを列挙し、存在しないプランは適用しません。
- 画面OFFまたはスリープのAC/DC書き込みが途中で失敗した場合、その項目を変更前の値へ戻します。

## ビルド

```powershell
dotnet build .\PCModeSwitcher.csproj -c Release
```

実行ファイルは通常 `bin\Release\net8.0-windows10.0.22621.0\win-x64\PCModeSwitcher.exe` に作成されます。

設定は `%LOCALAPPDATA%\PCModeSwitcher\settings.json` に保存されます。

設定メニューでは、右上の閉じるボタンを押したときにWindowsの通知領域へ格納するか、アプリを終了するかを選べます。通知領域アイコンはダブルクリックで再表示し、右クリックメニューの「終了」で完全終了します。格納時の通知も設定で有効・無効を選べます（既定は無効、有効時も起動中の初回だけ表示）。

アプリアイコンの原本は `Assets\AppIcon.png`、Windows用の複数解像度アイコンは `Assets\AppIcon.ico` です。

## テスト

```powershell
dotnet run --project .\tests\PCModeSwitcher.Tests\PCModeSwitcher.Tests.csproj -c Release
```

既定モード、JSONの保存と再読み込み、Windows Power APIによる電源プランの読み取りを検証します。実際のWindows設定は変更しません。
