# ローカルSDK調査結果（2026-10-02）

対象：ユーザー提供 `sdrsharp-plugin-sdk-vs2022-dotnet9.zip` と `sdrsharp-x64/SDRSharp.dotnet9.exe`。

- 本体FileVersion / ProductVersion：`1.0.0.1921`。
- SDKサンプル：`net9.0-windows`、WinForms、unsafe有効。
- `SDRSharp.Common.dll` / `SDRSharp.Radio.dll` のTargetFrameworkAttribute：`.NETCoreApp,Version=v9.0`。
- SDK DLLはreference assemblyであり実行用ではない。MetadataLoadContextで型定義を確認した。
- 本体フォルダーに.NET 8版もあるが、このSDKで作るプラグインの対象は.NET 9版。

確認済みのAPI：

```csharp
// SDRSharp.Common.ISharpPlugin
void Initialize(ISharpControl control);
void Close();
UserControl Gui { get; }
string DisplayName { get; }

// SDRSharp.Common.ISharpControl
void RegisterStreamHook(object processor, SDRSharp.Radio.ProcessorType type);
void UnregisterStreamHook(object processor);
// INotifyPropertyChanged; IsPlaying, Frequency, CenterFrequencyなどを公開。

// SDRSharp.Radio.IIQProcessor : IStreamProcessor : IBaseProcessor
unsafe void Process(SDRSharp.Radio.Complex* buffer, int length);
double SampleRate { set; }
bool Enabled { get; set; }

// Complex fields
float Real;
float Imag;
```

`ProcessorType.DecimatedAndFilteredIQ` と `RawIQ` の存在を確認。
古いサンプルの `HasGui` / `GuiControl` はこのISharpPluginのメンバーではない。

本体の `SDRSharp.config` は `core.pluginsDirectory=Plugins`、SDKの開発設定は `.`。
従って本体側の配置候補は `Plugins`。この時点ではGUI起動による実ロードは未検証。
提供SDKのライセンスは `SDK/sdrplugins/LICENSE.txt` に保存。SDK reference DLLをプラグイン配布物に含めない。
