using System.ComponentModel;
using SDRSharp.Common;
using SDRSharp.Radio;
using SDRSharp.DCRDecoder.Core;
using SDRSharp.DCRDecoder.Vocoder;

namespace SDRSharp.DCRDecoder;

public sealed class DcrDecoderPlugin : ISharpPlugin, ICanLazyLoadGui, IExtendedNameProvider
{
    private ISharpControl? control;
    private RealtimeReceiver? receiver;
    private IqHook? hook;
    private DcrDecoderPanel? panel;
    private AudioEngine? audio;
    private string? initializationError;
    private HostAudioBuffer? hostBuffer;
    private AudioHook? audioHook;
    public string DisplayName => "DCR Decoder";
    public string Category => "Digital";
    public string MenuItemName => DisplayName;
    public UserControl Gui { get { LoadGui(); return panel!; } }
    public void LoadGui() => panel ??= new DcrDecoderPanel(() => receiver?.Snapshot, () => receiver?.Reset(), () => initializationError, () => audio, ApplyAudio,
        () => hostBuffer == null ? "SDR# audio: unavailable" : $"SDR# audio: {hostBuffer.OutputSampleRate:N0} Hz\nAudio callbacks: {hostBuffer.Callbacks}\nPCM buffer overflows: {hostBuffer.Overflows}");

    private void ApplyAudio(AudioSettings settings)
    {
        if (audio == null) throw new InvalidOperationException("Audio engine unavailable.");
        audio.Apply(settings with { Volume = 100, Muted = false });
        hostBuffer?.Clear();
    }

    public void Initialize(ISharpControl sharpControl)
    {
        if (control != null) return;
        initializationError = null;
        control = sharpControl;
        try
        {
            hostBuffer = new HostAudioBuffer();
            audio = new AudioEngine((s,ct) => new PythonVocoder(s.PythonPath,s.HelperPath,ct), _ => hostBuffer, hostBuffer.Invalidate);
            audioHook = new AudioHook(hostBuffer, () => audio?.Settings.Enabled == true);
            receiver = new RealtimeReceiver(audio);
            hook = new IqHook(receiver);
            control.RegisterStreamHook(hook, ProcessorType.DecimatedAndFilteredIQ);
            control.RegisterStreamHook(audioHook, ProcessorType.FilteredAudioOutput);
            control.PropertyChanged += ControlChanged;
            receiver.SetRunning(control.IsPlaying);
        }
        catch (Exception ex)
        {
            initializationError = "Stream hook initialization failed: " + ex.Message;
            if (hook != null) hook.Enabled = false;
            try { if (hook != null) control.UnregisterStreamHook(hook); } catch { }
            if (audioHook != null) audioHook.Enabled = false;
            try { if (audioHook != null) control.UnregisterStreamHook(audioHook); } catch { }
            receiver?.Dispose(); receiver = null;
            audio?.Dispose(); audio = null;
        }
    }
    private void ControlChanged(object? sender, PropertyChangedEventArgs args)
    {
        var live = receiver;
        var host = control;
        if (live == null || host == null) return;
        live.SetRunning(host.IsPlaying);
        if (args.PropertyName is "Frequency" or "CenterFrequency" or "InputSampleRate" or "Source" or "SwapIq" or "FilterBandwidth" or "DetectorType" or "FrequencyShift" or "FrequencyShiftEnabled" || string.IsNullOrEmpty(args.PropertyName)) live.Reset();
    }
    public void Close()
    {
        if (control == null) return;
        if (hook != null) hook.Enabled = false;
        if (audioHook != null) audioHook.Enabled = false;
        control.PropertyChanged -= ControlChanged;
        try
        {
            try { if (hook != null) control.UnregisterStreamHook(hook); }
            finally { if (audioHook != null) control.UnregisterStreamHook(audioHook); }
        }
        catch (Exception ex) { initializationError = "Unregister failed: " + ex.Message; }
        finally
        {
            try { receiver?.Dispose(); audio?.Dispose(); }
            finally
            {
                hostBuffer?.Clear();
                receiver = null; audio = null; hostBuffer = null; audioHook = null; hook = null; control = null; panel?.Dispose(); panel = null;
            }
        }
    }
    private sealed unsafe class AudioHook(HostAudioBuffer buffer, Func<bool> replace) : IRealProcessor
    {
        private volatile bool enabled = true;
        private double rate;
        public bool Enabled { get => enabled; set => enabled = value; }
        public double SampleRate { set { if (Interlocked.Exchange(ref rate, value) != value) buffer.Invalidate(); } }
        public void Process(float* samples, int length)
        {
            if (!enabled || samples == null || length <= 0) return;
            // FilteredAudioOutput supplies interleaved stereo; length counts individual floats.
            buffer.RenderStereo(new Span<float>(samples, length), Volatile.Read(ref rate), replace());
        }
    }
    private sealed unsafe class IqHook : IIQProcessor
    {
        private readonly RealtimeReceiver receiver;
        private volatile bool enabled = true;
        public IqHook(RealtimeReceiver receiver) => this.receiver = receiver;
        public bool Enabled { get => enabled; set => enabled = value; }
        public double SampleRate { set => receiver.SampleRate = value; }
        public void Process(SDRSharp.Radio.Complex* buffer, int length)
        {
            if (!enabled || !receiver.Running || buffer == null || length <= 0) return;
            int epoch = receiver.Generation;
            double rate = receiver.SampleRate;
            if (!receiver.Queue.TryReserve(length, out var destination)) { receiver.Reset(); return; }
            for (int i = 0; i < length; i++)
            {
                destination[i].Real = buffer[i].Real; destination[i].Imag = buffer[i].Imag;
            }
            if (epoch == receiver.Generation && enabled && receiver.Running) receiver.Queue.Commit(length, rate, epoch);
        }
    }
}
