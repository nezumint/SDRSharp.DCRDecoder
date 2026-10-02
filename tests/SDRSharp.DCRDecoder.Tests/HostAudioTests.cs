using SDRSharp.DCRDecoder.Vocoder;

internal static class HostAudioTests
{
    public static void Run(Action<bool,string> check)
    {
        using var buffer = new HostAudioBuffer();
        float[] output = Enumerable.Repeat(0.75f, 960).ToArray();
        buffer.RenderStereo(output, 48000, false);
        check(output.All(v => v == 0.75f), "disabled host hook passes analog audio unchanged");
        buffer.RenderStereo(output, 48000, true);
        check(output.All(v => v == 0), "enabled empty host hook replaces analog audio with silence");
        short[] pcm = Enumerable.Repeat((short)16384, 640).ToArray();
        buffer.Write(pcm); buffer.RenderStereo(output, 48000, true);
        check(output.All(v => v == 0) && buffer.BufferedMilliseconds == 80, "host buffer waits for 160 ms prebuffer");
        buffer.Complete(); buffer.RenderStereo(output, 48000, true);
        check(output.All(v => v == 0.5f), "short completed burst drains with normalized PCM");
        buffer.Invalidate(); buffer.RenderStereo(output, 48000, true);
        check(output.All(v => v == 0) && buffer.BufferedMilliseconds == 0, "source invalidation silences queued host PCM immediately");
        foreach (int rate in new[] {8000, 37500, 44100, 48000, 96000})
        {
            buffer.Clear(); buffer.Write(pcm); buffer.Complete();
            var rendered = new float[2 * (rate * 80 / 1000 + 32)];
            for (int offset = 0; offset < rendered.Length; offset += 254)
                buffer.RenderStereo(rendered.AsSpan(offset, Math.Min(254, rendered.Length - offset)), rate, true);
            int duration = 2 * rate * 80 / 1000;
            check(rendered.Take(duration).All(v => v == 0.5f) && rendered.Skip(duration).All(v => v == 0), $"8000 -> {rate}: exact stereo duration across callback boundaries");
        }
        buffer.Clear();
        short[] ramp = Enumerable.Range(0, 640).Select(i => (short)(i * 32)).ToArray();
        buffer.Write(ramp); buffer.Complete(); buffer.RenderStereo(output, 48000, true);
        check(Math.Abs(output[6] - 16 / 32768f) < 1e-7 && Math.Abs(output[12] - 32 / 32768f) < 1e-7, "host rate conversion interpolates between PCM samples");
        buffer.Clear(); buffer.Write(pcm); buffer.Write(pcm);
        var longOutput = new float[16000]; buffer.RenderStereo(longOutput, 48000, true);
        check(buffer.Underruns == 1 && longOutput[^1] == 0, "underrun outputs silence and returns to prebuffering");
        buffer.Clear(); for (int i = 0; i < 7; i++) buffer.Write(pcm);
        check(buffer.Overflows == 1 && buffer.BufferedMilliseconds == 80, "stalled host has bounded buffer and discards old PCM");
        buffer.Clear(); buffer.Write(pcm); buffer.Complete();
        float[] odd = new float[127]; buffer.RenderStereo(odd, 37500, true);
        check(odd.All(v => v == 0) && buffer.BufferedMilliseconds == 80, "incomplete stereo frame fails silent without consuming PCM");
        buffer.Clear();
        short[] tone = Enumerable.Range(0, 3200).Select(i => (short)(16000 * Math.Sin(2 * Math.PI * 1000 * i / 8000))).ToArray();
        buffer.Write(tone); buffer.Complete();
        var stereo = new float[30000];
        for (int i = 0; i < stereo.Length; i += 512)
            buffer.RenderStereo(stereo.AsSpan(i, Math.Min(512, stereo.Length - i)), 37500, true);
        check(Enumerable.Range(0, stereo.Length / 2).All(i => stereo[2*i] == stereo[2*i+1]), "stereo channels receive identical samples");
        int crossings = 0;
        for (int i = 1; i < 3750; i++)
            if (stereo[2*(i-1)] <= 0 && stereo[2*i] > 0) crossings++;
        check(crossings is >= 99 and <= 101, "1 kHz input remains 1 kHz at 37500 Hz stereo (not 2 kHz)");
        check(stereo.Skip(22500).Any(v => Math.Abs(v) > 0.1f) && buffer.BufferedMilliseconds == 0, "400 ms tone lasts full stereo duration, not 200 ms");
        buffer.Clear(); buffer.Write(pcm); buffer.Write(pcm);
        long before = buffer.Underruns;
        var block = new float[6000]; // 80 ms at 37500 stereo frames/s.
        bool continuous = true;
        for (int i = 0; i < 100; i++)
        {
            buffer.RenderStereo(block, 37500, true);
            continuous &= block.All(v => v == 0.5f) && buffer.BufferedMilliseconds == 80;
            buffer.Write(pcm);
        }
        check(continuous && buffer.Underruns == before, "8 seconds paced stereo playback consumes 80 ms per block without gaps");
        buffer.RenderStereo(output, double.NaN, true);
        check(output.All(v => v == 0), "unknown host sample rate fails silent");
    }
}
