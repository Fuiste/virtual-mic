namespace VirtualMic.Core;

// Hardware QPC timestamps can jitter even when packet samples are continuous.
// Follow their long-term phase with a bounded PLL instead of dropping perfectly
// good audio whenever one packet's reported timestamp overlaps its predecessor.
public sealed class CaptureClock
{
    public const double NominalPeriod = 10000000.0 / 48000;
    private double next;
    public (double Start, double Period) Align(double timestamp, int frames, bool discontinuity)
    {
        if (frames <= 0 || !double.IsFinite(timestamp) || timestamp <= 0) throw new ArgumentException("invalid capture clock input");
        double error = timestamp - next;
        if (next == 0 || discontinuity || Math.Abs(error) > 50000) // gaps over 5 ms are real discontinuities/speaker silence
        {
            next = timestamp + frames * NominalPeriod;
            return (timestamp, NominalPeriod);
        }
        double start = next;
        double period = NominalPeriod + Math.Clamp(error * .03 / frames, -NominalPeriod * .005, NominalPeriod * .005);
        next = start + frames * period;
        return (start, period);
    }
}
