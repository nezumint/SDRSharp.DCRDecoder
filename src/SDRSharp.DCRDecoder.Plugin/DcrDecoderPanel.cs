using SDRSharp.DCRDecoder.Core;
using SDRSharp.DCRDecoder.Vocoder;

namespace SDRSharp.DCRDecoder;

public sealed class DcrDecoderPanel : UserControl
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly Label status = new() { AutoSize = true };
    private readonly Label audioStatus = new() { AutoSize = true };
    private readonly Label validation = new() { AutoSize = true };
    private readonly CheckBox enableAudio = new() { Text = "Enable DCR audio through SDR#", AutoSize = true };
    private readonly CheckBox enablePrivacy = new() { Text = "Enable standard privacy decode", AutoSize = true };
    private readonly TextBox code = new() { Width = 110, MaxLength = 5 };
    private readonly Label engineStatus = new() { AutoSize = true };
    private readonly EmbeddedVocoderRuntime runtime;
    private readonly CancellationTokenSource importCancellation = new();
    private readonly Func<ReceiverSnapshot?> getSnapshot;
    private readonly Func<AudioEngine?> getAudio;
    private readonly Func<string?> getError;
    private readonly Action<AudioSettings> applySettings;
    private readonly string helper;
    private readonly Func<string> getHostAudioStatus;

    public DcrDecoderPanel(Func<ReceiverSnapshot?> getSnapshot, Action reset, Func<string?> getError, Func<AudioEngine?> getAudio, Action<AudioSettings> applySettings, Func<string> getHostAudioStatus)
    {
        this.applySettings = applySettings;
        this.getHostAudioStatus = getHostAudioStatus;
        this.getSnapshot = getSnapshot; this.getAudio = getAudio; this.getError = getError;
        runtime = new EmbeddedVocoderRuntime(Path.GetDirectoryName(typeof(DcrDecoderPlugin).Assembly.Location)!);
        helper = Path.Combine(Path.GetDirectoryName(typeof(DcrDecoderPlugin).Assembly.Location)!,"dcr_vocoder_host.py");
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScroll = true; MinimumSize = new Size(260,350);
        var layout = new BufferedLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        void Add(Control control) { control.Margin = new Padding(0,3,0,3); layout.Controls.Add(control,0,layout.RowCount++); }
        Add(status); Add(audioStatus); Add(enableAudio); Add(enablePrivacy);
        var privacyRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        privacyRow.Controls.Add(new Label { Text = "Privacy code (1–32767)", AutoSize = true, Padding = new Padding(0,5,4,0) }); privacyRow.Controls.Add(code); Add(privacyRow);
        Add(new Label { Text = "Audio output / volume / mute: use SDR# controls", AutoSize = true });
        Add(engineStatus);
        RefreshEngineStatus();
        var download = new LinkLabel { Text = "Download supported voice engine (Windows x64)", AutoSize = true };
        download.LinkClicked += (_,_) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(EmbeddedVocoderRuntime.DownloadPage) { UseShellExecute = true }); }
            catch (Exception ex) { validation.Text = "Open this page in your browser: " + EmbeddedVocoderRuntime.DownloadPage + "\n" + ex.Message; }
        };
        Add(download);
        var import = new Button { Text = "Import voice engine (.whl)…", AutoSize = true };
        Add(import);
        var apply = new Button { Text = "Apply audio settings", AutoSize = true };
        apply.Click += (_,_) => Apply(); Add(apply); Add(validation);
        import.Click += async (_,_) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Voice engine wheel|*.whl", CheckFileExists = true, Title = "Select " + EmbeddedVocoderRuntime.WheelName };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            import.Enabled = apply.Enabled = enableAudio.Enabled = false;
            try
            {
                var engine = getAudio();
                if (engine != null) applySettings(engine.Settings with { Enabled = false });
                enableAudio.Checked = false;
                validation.Text = "Checking and importing voice engine…";
                await Task.Run(() => runtime.InstallWheelAsync(dialog.FileName, importCancellation.Token));
                if (!IsDisposed) validation.Text = "Voice engine ready. Enable DCR audio and apply settings.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed) validation.Text = "Import failed: " + ex.Message; }
            finally
            {
                if (!IsDisposed) { import.Enabled = apply.Enabled = enableAudio.Enabled = true; RefreshEngineStatus(); }
            }
        };
        var resetButton = new Button { Text = "Reset Decoder", AutoSize = true };
        resetButton.Click += (_,_) => reset(); Add(resetButton);
        Controls.Add(layout); timer.Tick += (_,_) => RefreshStatus(); timer.Start(); RefreshStatus();
        layout.SizeChanged += (_,_) => {
            int width=Math.Max(180,layout.ClientSize.Width-20);
            status.MaximumSize=audioStatus.MaximumSize=validation.MaximumSize=engineStatus.MaximumSize=new Size(width,0);
        };
    }
    private void RefreshEngineStatus()
    {
        engineStatus.Text = !runtime.RuntimeAvailable ? "Bundled runtime missing: copy the complete DcrRuntime folder."
            : runtime.EngineAvailable ? "Voice engine: ready (1.0.0 / Windows x64)"
            : "Voice engine: not installed. Download and import the Windows x64 .whl file.";
    }
    private bool Apply()
    {
        try { return ApplyCore(); }
        catch (Exception ex) { validation.Text = "Audio settings: " + ex.Message; return false; }
        finally { enableAudio.Checked = getAudio()?.Settings.Enabled == true; }
    }
    private bool ApplyCore()
    {
        var audio = getAudio(); if(audio==null) { validation.Text="Audio engine unavailable."; return false; }
        // Always allow leaving DCR mode, even when unfinished settings are invalid.
        if (!enableAudio.Checked)
        {
            applySettings(audio.Settings with { Enabled = false });
            validation.Text = "DCR off. Normal SDR# audio passes through.";
            return true;
        }
        int? key=null;
        if(!string.IsNullOrWhiteSpace(code.Text))
        {
            if(!int.TryParse(code.Text,out int parsed) || parsed<1 || parsed>32767) { validation.Text="Privacy code must be 1–32767."; return false; }
            key=parsed;
        }
        if(enablePrivacy.Checked && key==null) { validation.Text="Enter your known privacy code."; return false; }
        RefreshEngineStatus();
        if (!runtime.RuntimeAvailable || !runtime.EngineAvailable) { validation.Text = engineStatus.Text; return false; }
        applySettings(new AudioSettings(enableAudio.Checked,enablePrivacy.Checked,key,false,100,
            -1,runtime.PythonPath,helper));
        validation.Text="Applied. Audio routed through SDR#. Unmute SDR# to listen. Privacy code is kept only in memory.";
        return true;
    }
    private void RefreshStatus()
    {
        var s=getSnapshot();
        if(s==null) { status.Text=getError()??"DCR Decoder — initializing"; return; }
        string statusText=$"DCR Decoder\n\nStatus: {s.Status}\nInput: {s.SampleRate:N0} samples/s\nResidual CFO: {s.Cfo:+0;-0;0} Hz\nFrames: {s.Frames}\nRejected frames: {s.RejectedFrames}\nAMBE frames: {s.AmbeFrames}\nDropped IQ blocks: {s.DroppedBlocks}\n\nMode: {s.Mode}\nCSM: {s.Csm}\nUser Code: {s.UserCode?.ToString()??"—"}\nMaker Code: {s.MakerCode?.ToString()??"—"}\nPrivacy: {s.Privacy}";
        string? error = getError();
        if(error!=null) statusText+="\n"+error;
        if(s.Error!=null) statusText+="\nError: "+s.Error;
        var a=getAudio()?.Snapshot;
        string audioText=a==null?"Audio: unavailable":$"Audio: {a.Status}\nBuffer: ~{a.BufferMilliseconds} ms\nPCM frames: {a.PcmFrames}\nFEC corrections: {a.CorrectedBits}\nFEC rejected groups: {a.FecRejected}\nAudio queue drops: {a.DroppedGroups}\nUnderruns: {a.Underruns}";
        if(a?.Error!=null) audioText+="\n"+a.Error;
        audioText+="\n"+getHostAudioStatus();
        // Assign complete strings once: intermediate shorter text shrinks AutoSize rows
        // and moves every control below them on each timer tick.
        var layout = status.Parent;
        layout?.SuspendLayout();
        try
        {
            if (status.Text != statusText) status.Text = statusText;
            if (audioStatus.Text != audioText) audioStatus.Text = audioText;
        }
        finally { layout?.ResumeLayout(true); }
    }
    private sealed class BufferedLayoutPanel : TableLayoutPanel
    {
        public BufferedLayoutPanel() => DoubleBuffered = true;
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing) { importCancellation.Cancel(); timer.Stop(); timer.Dispose(); code.Clear(); }
        base.Dispose(disposing);
    }
}
