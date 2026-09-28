using System.Text.Json.Serialization;

namespace VirtualMic.Core.Effects;

[JsonConverter(typeof(JsonStringEnumConverter<EffectTarget>))]
public enum EffectTarget { Mic, Sounds, Both }

public sealed record EffectSlot
{
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public string EffectId { get; init; } = "";
    public bool Enabled { get; init; }
    public EffectTarget Target { get; init; } = EffectTarget.Mic;
    public Dictionary<string, float> Parameters { get; init; } = [];
}

public static class EffectDefaults
{
    public static EffectSlot[] FromLegacy(AudioSettings settings) =>
    [
        new() { InstanceId = "legacy-bass", EffectId = "virtualmic.bass", Enabled = settings.BassEnabled,
            Parameters = new() { ["gain"] = settings.BassDb } },
        new() { InstanceId = "legacy-distortion", EffectId = "virtualmic.distortion", Enabled = settings.DistortionEnabled,
            Parameters = new() { ["drive"] = settings.Drive, ["mix"] = settings.DistortionMix * 100 } }
    ];
}
