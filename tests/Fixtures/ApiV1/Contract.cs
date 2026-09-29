// Frozen v0.2 contract. Do not reference the current API: this fixture verifies ABI compatibility.
namespace VirtualMic.PluginApi;
public static class EffectApi { public const int Version = 1; }
public sealed record EffectParameter(string Id, string Name, float Minimum, float Maximum, float DefaultValue, string Unit = "", float Step = .01f);
public sealed record EffectDefinition(string Id, string Name, IReadOnlyList<EffectParameter> Parameters, int ApiVersion = EffectApi.Version);
public interface IAudioEffectPlugin { EffectDefinition Definition { get; } IAudioEffect Create(int sampleRate, int channels); }
public interface IAudioEffect : IDisposable { void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters); void Reset(); }
