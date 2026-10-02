# Architecture

```text
SDRSharp (.NET 9, x64)
  IIQProcessor callback
    -> IqBlockQueue (16 preallocated slots, max 65,536 samples/slot)
    -> RealtimeReceiver worker
    -> ChannelResampler -> StreamingDcrDecoder -> DcrProtocol
    -> immutable ReceiverSnapshot -> WinForms timer (100 ms)
    -> AudioEngine bounded queue -> audio worker
       -> AmbeFec -> StandardPrivacy (only when required)
       -> PythonVocoder child process -> HostAudioBuffer (8 kHz mono)
       -> IRealProcessor / FilteredAudioOutput -> SDRSharp audio output
```

## 連続入力

`ReferenceDsp`はライブ復号に必要な受信フィルター生成・同期推定・量子化だけを保持します。

`StreamingDcrDecoder`は録音全体を保持せず、500-tap FIRと固定長のサンプル履歴から同期候補を探します。約4,800サンプル間隔の同期語を2回確認した後、候補を復号します。同期語ごとに位相・周波数オフセット・レベルを推定し、反転スペクトルにも対応します。小数位相の独立timing loopやslow AFCは未実装です。

ライブ復号は`DcrProtocol`でdewhitening、RICH、SACCH、PICH、TCH/AMBE3600 packingを実行します。SACCH/PICHのCRCはPython参照実装に合わせて両ビット順を受理します。この寛容な判定を規格上の厳密性と同一視せず、将来変更する場合は別の既知ベクトルで確認します。

## スレッドと切り替え

キューはsingle producer / single consumer専用です。callbackは領域確保済みスロットを予約し、IQをコピーして公開します。空きがなければブロック全体を破棄し、overflow数と世代番号を更新します。decoder workerは古い世代をスキップします。

周波数・受信条件・サンプルレート・再生状態の変更時には世代を更新し、worker側でresampler、同期、通話状態をリセットします。callbackの最中に世代が変わった場合、そのブロックを公開しません。

停止中はworkerを待機させ、Closeでcallback無効化、hook解除、キャンセル、join、UI timer破棄を行います。現段階でworkerはラジオ停止のたびに破棄せず、プラグインのライフサイクルに合わせて保持します。

## 音声経路

`StandardPrivacy`は49-bit natural-order AMBEに対する既知コード変換です。現在の前段出力はFEC付きAMBE3600であり、そのまま秘話解除へ渡してはいけません。

実装経路：AMBE3600 → FEC解除 → AMBE2450 → 必要に応じ既知コード解除 → vocoder → PCM出力。音声のFEC・秘話解除・vocoder呼び出しは専用audio workerで実行します。

`AudioEngine`は最大8組の有界キューを使います。receiverの世代、音声設定の世代、処理終了時の世代を確認して、切り替え前のPCMが新しい通話へ流れないようにします。秘話コード未入力・不明な秘話モード・訂正不能FECはvocoderへの入力を抑止します。

PTT終話マーカーはキュー内で音声の後ろに並べ、末尾を排出します。停止・選局・reset・キー変更はバッファをクリアします。音声設定とコードはメモリー上にだけ保持します。

Python helperは一度起動して標準入出力の固定長バイナリで通信します。1フレーム7バイト→160 PCMサンプル。音声データはログ出力せず、応答タイムアウト・キャンセル時は子プロセスを終了します。IVocoderを介して将来のnative backendへ交換できます。

ホスト出力はHostAudioBufferを使用します。500 msの有界リングバッファ、160 msの初期蓄積で、終話時は短い末尾も排出します。PCM16をfloatへ正規化し、コールバックのSampleRateへ線形補間します。ホスト側はTryEnterでバッファへアクセスし、競合時は待機せず無音を返します。コールバックでvocoderを呼ばず、配列の動的確保やI/Oを行いません。リセット通知は世代カウンターで伝達し、次のバッファ操作で反映します。音量とミュートはホストに委ねます。

FilteredAudioOutputのバッファを左右交互のfloat列として処理します。lengthは左右合計のfloat数、SampleRateは左右一組のフレームレートです。変換したモノラル値を左右に複製し、左右一組につき一度だけ位相を進めます。奇数長の置換要求はPCMを消費せず無音とします。現在のSDKでコンパイル済みですが、本体での処理順序、音量倍率、スケルチや他プラグインとの相互作用は実機確認が必要です。

## 組み込み実行環境とエンジンの別取得

`EmbeddedVocoderRuntime` がプラグインDLLと同じ場所の `DcrRuntime` を参照します。Python 3.14.8 x64とNumPy 2.5.3は同梱、音声エンジンはユーザーによる取得・取り込みです。Pythonの._pthと-Iにより外部環境を排除し、-Bでpycache生成を抑止します。PATHやレジストリの変更、pip、自動取得は行いません。

取り込みはUIから非同期で実行します。wheelを同一ファイルハンドルのままSHA-256検証し、許可されたパスだけを一時フォルダーへ展開します。ライセンス・メタデータも保持します。組み込みPythonの子プロセスで160サンプルの復号を試験し、成功後にディレクトリを移動して公開します。失敗・キャンセル時は一時フォルダーを除去します。既存エンジンは再検証のみで上書きしません。終了時は取り込みをキャンセルし、試験プロセスには15秒の期限を設けます。

配布物はPackage-Release.ps1で検査し、engineフォルダー、wheel、開発用環境の混入を拒否します。distにはエンジンを取り込みません。
