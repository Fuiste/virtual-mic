using VirtualMic.Core;

internal static class CaptureTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("jittery hardware timestamps preserve continuous microphone packets", () =>
        {
            var clock = new CaptureClock(); var queue = new CaptureTimeline(); var random = new Random(91);
            double previousEnd = 0, finalError = 0; var packet = Enumerable.Repeat(.1f, 960).ToArray();
            for (int block = 0; block < 80; block++)
            {
                double trueStart = 10000000 + block * 480 * CaptureClock.NominalPeriod * 1.0001;
                double jitter = block == 0 ? 0 : (random.NextDouble() * 2 - 1) * 15000; // +/- 1.5 ms
                var time = clock.Align(trueStart + jitter, 480, false);
                if (block > 0) Check(time.Start == previousEnd, "clock introduced a gap or overlap");
                queue.Write(packet, time.Start, false, time.Period);
                previousEnd = time.Start + 480 * time.Period; finalError = Math.Abs(time.Start - trueStart);
            }
            Check(queue.BufferedFrames == 38400 && queue.Generation == 0, "jitter discarded valid packets");
            Check(finalError < 10000, "smoothed capture clock diverged by more than 1 ms");
            var reset = clock.Align(40000000, 480, true); Check(reset.Start == 40000000, "explicit discontinuity not honored");
        });
        yield return ("capture timestamps align independently packetized speaker samples", () =>
        {
            var reference = new CaptureTimeline(); var mic = new CaptureTimeline();
            double origin = 123456789000, tick = 10000000.0 / 48000;
            float[] values = Enumerable.Range(0, 4000).Select(i => i % 2 == 0 ? i / 4000f : -i / 4000f).ToArray();
            reference.Write(values.AsSpan(0, 1400), origin); reference.Write(values.AsSpan(1400), origin + 700 * tick);
            mic.Write(values.AsSpan(400, 2200), origin + 200 * tick);
            float[] actual = new float[2200], expected = new float[2200]; double[] timestamps = new double[1100];
            mic.ReadMicrophone(expected, timestamps, 0); reference.ReadReference(timestamps, actual);
            Check(actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) < .0001), "speaker reference shifted channels or packet time");
        });
        yield return ("speaker gaps and microphone underruns never replay old audio", () =>
        {
            var reference = new CaptureTimeline(); reference.Write([.2f, -.2f, .3f, -.3f], 10000000);
            float[] samples = new float[6]; reference.ReadReference([10000000, 10020000, 0], samples);
            Check(samples.SequenceEqual(new[] { .2f, -.2f, 0f, 0f, 0f, 0f }), "stale speaker packet replayed");
            var mic = new CaptureTimeline(); samples.AsSpan().Fill(.5f); double[] times = [1, 2, 3];
            mic.ReadMicrophone(samples, times); Check(samples.All(x => x == 0) && times.All(x => x == 0), "underrun not cleared");
        });
        yield return ("timestamp interpolation tracks independent speaker clock drift", () =>
        {
            foreach (double drift in new[] { -.00015, .00015 })
            {
                var reference = new CaptureTimeline(); const double origin = 10000000; double step = 10000000.0 / 48000;
                var packet = new float[960]; double power = 0, error = 0;
                for (int block = 0; block < 1000; block++)
                {
                    double start = origin + block * 480 * step * (1 + drift);
                    for (int i = 0; i < 480; i++) packet[2 * i] = packet[2 * i + 1] = (float)Math.Sin((start - origin + i * step) / 10000000 * 2 * Math.PI * 250);
                    reference.Write(packet, start);
                    if (block < 3) continue;
                    var t = Enumerable.Range(0, 480).Select(i => origin + ((block - 2) * 480 + i) * step).ToArray();
                    var result = new float[960]; reference.ReadReference(t, result);
                    for (int i = 0; i < 480; i++) { double expected = Math.Sin((t[i] - origin) / 10000000 * 2 * Math.PI * 250); error += Math.Pow(result[i * 2] - expected, 2); power += expected * expected; }
                }
                Check(error / power < .00001, "reference drifted away from mic clock");
            }
        });
        yield return ("capture discontinuities flush stale packets and bound microphone latency", () =>
        {
            var queue = new CaptureTimeline(); queue.Write(Enumerable.Repeat(.8f, 20000).ToArray(), 10000000);
            float[] output = new float[4000]; double[] times = new double[2000]; queue.ReadMicrophone(output, times);
            Check(queue.Generation > 0 && times[0] > 11000000, "microphone backlog kept stale latency");
            queue.Write([.1f, .2f], 20000000, true); queue.ReadMicrophone(output, times, 0);
            Check(output[0] == .1f && output[1] == .2f && output.Skip(2).All(x => x == 0), "discontinuity replayed old capture");
        });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
