using System.IO;
using NAudio.Wave;
using VirtualMic.Core;

namespace VirtualMic.App.Audio;

public static class ClipLoader
{
    public const int MaxSamples = 48000 * 2 * 120;
    public const long MaxCacheBytes = 256L * 1024 * 1024;
    public static float[] Read(string path)
    {
        using var reader = new AudioFileReader(path);
        if (reader.TotalTime.TotalSeconds > 120.1) throw new InvalidDataException("clips must be two minutes or shorter");
        var source = AudioEngine.Stereo48(reader);
        var samples = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (samples.Count + read > MaxSamples) throw new InvalidDataException("clips must be two minutes or shorter");
            for (int i = 0; i < read; i++) samples.Add(float.IsFinite(buffer[i]) ? buffer[i] : 0);
        }
        if (samples.Count == 0) throw new InvalidDataException("the file contains no audio");
        if (samples.Count % 2 != 0) samples.Add(0);
        // Tiny boundary fades prevent single-click edges on pad retrigger / end.
        int fade = Math.Min(240, samples.Count / 4);
        for (int frame = 0; frame < fade; frame++)
            for (int ch = 0; ch < 2; ch++)
            {
                samples[frame * 2 + ch] *= (float)frame / fade;
                samples[samples.Count - 2 - frame * 2 + ch] *= (float)frame / fade;
            }
        return samples.ToArray();
    }

    public static void Write(string path, float[] samples)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        writer.WriteSamples(samples, 0, samples.Length);
    }

    // Original synthetic sounds for preview / optional starter pads. No recordings.
    public static float[] Tone(int kind)
    {
        double duration = kind == 0 ? 1.2 : kind == 1 ? .9 : kind == 2 ? 1.8 : 1.4;
        int frames = (int)(duration * 48000);
        var result = new float[frames * 2];
        double phase = 0;
        for (int i = 0; i < frames; i++)
        {
            double t = i / 48000.0;
            double frequency = kind switch
            {
                0 => t < .15 ? 440 : t < .3 ? 554.37 : 659.25,
                1 => 900 - t * 650,
                2 => 85 + 30 * Math.Sin(t * 5),
                _ => 220 + (int)(t * 7) % 4 * 110
            };
            phase += frequency * 2 * Math.PI / 48000;
            double envelope = Math.Min(1, t / .012) * Math.Min(1, (duration - t) / .12) * Math.Exp(-t * .7);
            result[i * 2] = result[i * 2 + 1] = (float)(Math.Sin(phase) * envelope * .36);
        }
        return result;
    }
}
