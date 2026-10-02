# SDRSharp DCR Decoder 1.0.0

本プラグインは、ChatGPTで作成した設計・解析プランをもとに、AIコーディング支援ツールのCodexを使用して実装しました。

SDRSharp revision 1921 **.NET 9 x64版**向けのDCRデコーダです。受信したIQデータから通話情報を表示し、復号音声をSDRSharpの音声出力で再生します。既知コードによる標準秘話の復号にも対応します。

Python 3.14.8 x64の組み込み版とNumPy 2.5.3を同梱します。利用者がPythonをインストールしたり、パスを設定したりする必要はありません。音声エンジン `blip25-vocoder==1.0.0` 本体は同梱せず、利用者が配布元から取得したWindows x64版wheelを取り込む構成です。エンジン未配置でもIQと制御情報の復号表示は継続します。

## インストールと音声の使い方

1. **SDRSharpを終了**します。
2. 配布ZIPの中身を `sdrsharp-x64/Plugins/` にコピーします。DLL全3本、helper、依存情報だけでなく、**DcrRuntimeフォルダー全体**とライセンス文書も必要です。
3. [配布元のファイル一覧](https://pypi.org/project/blip25-vocoder/1.0.0/#files)から **blip25_vocoder-1.0.0-cp39-abi3-win_amd64.whl** を取得します。ソースのtar.gz、Linux版、macOS版は使用できません。開発元のライセンスと特許に関する説明も確認してください。
4. `SDRSharp.dotnet9.exe`を起動し、DCR Decoderの **Import voice engine (.whl)…** を押して取得したファイルを選びます。**Download supported voice engine**リンクから配布元を開くこともできます。
5. **Voice engine: ready**になったら **Enable DCR audio through SDR#**をチェックします。標準秘話の場合だけ **Enable standard privacy decode** をチェックし、既知コードを入力します。コードは数字をそのまま表示します。
6. **Apply audio settings**を押します。出力先と音量はSDRSharp側で選び、SDRSharpのミュートを解除してください。

取り込みはオフラインで行い、自動ダウンロードやpipの実行は行いません。既知のSHA-256に一致する上記ファイルだけを受け付け、展開後に実際の音声復号を試験してから有効にします。エンジンは `Plugins/DcrRuntime/engines/blip25-vocoder-1.0.0/` に保存され、SDRSharp再起動後も再取り込みは不要です。更新時はこのフォルダーを保持してください。取り込み先への書き込み権限が必要です。

対応wheelのSHA-256：
`292f4ea9ec1821211e07761bd01f0db5ec1382a32b9ff72ac969253462eb2ef4`

DCR音声を有効にすると通常の受信音を復号音に置き換えます。復号音がない間は無音です。チェックを外して **Apply audio settings** を押すと通常の受信音に戻ります。出力先・音量・ミュートはSDRSharp側で操作してください。

## 音が出ない場合

SDRSharpのミュート、音量、出力先、スケルチを確認してください。パネルで Voice engine: ready と音声有効のチェックを確認し、Apply audio settings を押してください。SDR# audio が0 Hz、または受信中に Audio callbacks が増えない場合は、SDRSharpから音声処理が呼び出されていません。他の音声処理プラグインの設定も確認してください。

## 音声設定

コードは1～32767のみ受け付け、ディスクには保存しません。音声は初期状態で無効、コード欄も空です。設定は現段階ではセッション限りです。

配布版は同梱環境だけを使い、PCの既存PythonやPYTHONPATHからモジュールを読み込みません。

## 音声と状態の扱い

- コード未入力の秘話は `Code required`。音声を出力しません。
- 不明な秘話状態は `Unsupported privacy` とし、音声を抑止します。
- コードの正誤は自動判定しません。正しい既知コードを入力してください。
- 受信状態が悪く、誤り訂正できない音声データは再生されません。
- PTT終了時は末尾を排出し、周波数変更・停止・リセット・コード変更時は古い音声を破棄します。
- 復号バックエンドの障害はパネルに表示します。SDRSharp側の音声デバイス障害はSDRSharp側で確認してください。設定を直してApplyで再試行できます。制御情報の復号は継続します。
- ライブIQ・音声・秘話コードのログ保存や自動録音は行いません。

## ビルド

ソースからのビルド方法は [BUILD.md](BUILD.md) を参照してください。

```powershell
./build.ps1
./tools/Package-Release.ps1
```

## 対応範囲

SDRSharpから渡されるチャネル抽出済みIQの12～192 kSPSに対応します。標準秘話は既知コードを指定した復号に対応し、コードの探索は行いません。

## ライセンス

本プロジェクトは [MIT License](LICENSE) です。第三者の著作権・ライセンスと出典は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) に記載しています。同梱Python・NumPyと別途取得する音声エンジンには、それぞれのライセンスが適用されます。

SDRSharp SDKは独自の **SDRSHARP REFERENCE LICENSE** です。SDKはリポジトリ・配布ZIPへ同梱しません。
