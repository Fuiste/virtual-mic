using System.Collections.Concurrent;
using VirtualMic.PluginApi;

namespace VirtualMic.Core.Effects;

public sealed record EffectFault(string InstanceId, string Message);

// Configure/Dispose on the control thread. Process is called by one render thread.
// Factories and disposal stay outside the short plan-swap lock. A parameter edit
// replaces only its snapshot, preserving delay/filter state and processor identity.
public sealed class EffectChain(EffectCatalog catalog) : IDisposable
{
    public const int MaximumEffects = 16;
    private const int ChunkSamples = 2048;
    private readonly object gate = new();
    private readonly float[] backup = new float[ChunkSamples];
    private readonly ConcurrentQueue<EffectFault> faults = new();
    private Step[] plan = [];
    public bool TryDequeueFault(out EffectFault? fault) => faults.TryDequeue(out fault);

    public static void Validate(IReadOnlyList<EffectSlot> slots)
    {
        if (slots.Count > MaximumEffects || slots.Any(s => s is null || string.IsNullOrWhiteSpace(s.InstanceId) ||
            s.InstanceId.Length > 100 || !EffectCatalog.ValidId(s.EffectId) || s.Parameters is null || !Enum.IsDefined(s.Target)) ||
            slots.Select(s => s.InstanceId).Distinct().Count() != slots.Count)
            throw new InvalidDataException("invalid effects chain (maximum 16 unique slots)");
    }

    public void Configure(IReadOnlyList<EffectSlot> slots)
    {
        Validate(slots);
        var next = new List<Step>();
        var created = new List<Runtime>();
        try
        {
            foreach (var slot in slots)
            {
                var registration = catalog.Find(slot.EffectId);
                var target = registration?.Definition.MicrophoneOnly == true ? EffectTarget.Mic : slot.Target;
                var previous = plan.FirstOrDefault(s => s.Id == slot.InstanceId && s.EffectId == slot.EffectId);
                Runtime runtime;
                if (previous is not null && !(previous.Runtime.Mic is IRecreateOnEnable &&
                    (previous.Enabled != slot.Enabled || previous.Target != target))) runtime = previous.Runtime;
                else
                {
                    runtime = new Runtime(); created.Add(runtime);
                    try
                    {
                        if (registration is null) throw new InvalidOperationException($"{slot.EffectId} is unavailable");
                        runtime.Mic = registration.Factory.Create(48000, 2) ?? throw new InvalidOperationException("factory returned no processor");
                        if (!registration.Definition.MicrophoneOnly)
                            runtime.Sounds = registration.Factory.Create(48000, 2) ?? throw new InvalidOperationException("factory returned no processor");
                        if (ReferenceEquals(runtime.Mic, runtime.Sounds)) throw new InvalidOperationException("factory must return separate processor instances");
                        if (registration.Definition.RequiresSpeakerReference && runtime.Mic is not IReferenceAudioEffect)
                            throw new InvalidOperationException("reference effect must implement IReferenceAudioEffect");
                    }
                    catch (Exception ex) { Fail(runtime, slot.InstanceId, registration?.Definition.Name ?? slot.EffectId, ex); }
                }
                next.Add(new(slot.InstanceId, slot.EffectId, registration?.Definition.Name ?? slot.EffectId,
                    slot.Enabled, target,
                    registration?.Parameters(slot.Parameters) ?? new Dictionary<string, float>(), runtime,
                    registration?.Definition.RequiresSpeakerReference == true));
            }
        }
        catch { foreach (var runtime in created) runtime.Dispose(); throw; }
        Step[] old;
        lock (gate) { old = plan; plan = next.ToArray(); }
        foreach (var removed in old.Where(s => !next.Any(n => ReferenceEquals(n.Runtime, s.Runtime)))) removed.Runtime.Dispose();
    }

    public void Process(float[] mic, float[] sounds, int count, bool processSounds = true,
        ReadOnlySpan<float> reference = default, bool referenceDiscontinuity = false)
    {
        lock (gate)
        {
            foreach (var step in plan)
            {
                var runtime = step.Runtime;
                bool micOn = step.Enabled && (step.Target is EffectTarget.Mic or EffectTarget.Both)
                    && (!step.Reference || reference.Length == count);
                bool soundsOn = processSounds && step.Enabled && step.Target is EffectTarget.Sounds or EffectTarget.Both;
                try
                {
                    if ((runtime.MicOn && !micOn) || (step.Reference && referenceDiscontinuity)) runtime.Mic?.Reset();
                    if (runtime.SoundsOn && !soundsOn) runtime.Sounds?.Reset();
                }
                catch (Exception ex) { Fail(runtime, step.Id, step.Name, ex); }
                runtime.MicOn = micOn; runtime.SoundsOn = soundsOn;
                if (runtime.Failed) continue;
                if (micOn) ProcessLane(step, runtime.Mic!, mic, count, reference);
                if (soundsOn && !runtime.Failed) ProcessLane(step, runtime.Sounds!, sounds, count, default);
            }
        }
    }

    private void ProcessLane(Step step, IAudioEffect effect, float[] samples, int count, ReadOnlySpan<float> reference)
    {
        for (int offset = 0; offset < count; offset += ChunkSamples)
        {
            var block = samples.AsSpan(offset, Math.Min(ChunkSamples, count - offset));
            block.CopyTo(backup);
            try
            {
                if (step.Reference) ((IReferenceAudioEffect)effect).Process(block, reference.Slice(offset, block.Length), step.Parameters);
                else effect.Process(block, step.Parameters);
                foreach (float sample in block)
                    if (!float.IsFinite(sample)) throw new InvalidOperationException("effect produced invalid audio");
            }
            catch (Exception ex)
            {
                backup.AsSpan(0, block.Length).CopyTo(block);
                Fail(step.Runtime, step.Id, step.Name, ex);
                break;
            }
        }
    }

    private void Fail(Runtime runtime, string id, string name, Exception ex)
    {
        if (runtime.Failed) return;
        runtime.Failed = true;
        faults.Enqueue(new(id, $"{name} bypassed: {ex.GetBaseException().Message}. stop/start audio to retry."));
    }

    public void ResetSounds()
    {
        lock (gate)
            foreach (var step in plan)
                try { step.Runtime.Sounds?.Reset(); }
                catch (Exception ex) { Fail(step.Runtime, step.Id, step.Name, ex); }
    }

    public void Dispose()
    {
        Step[] old;
        lock (gate) { old = plan; plan = []; }
        foreach (var step in old) step.Runtime.Dispose();
    }

    private sealed record Step(string Id, string EffectId, string Name, bool Enabled, EffectTarget Target,
        IReadOnlyDictionary<string, float> Parameters, Runtime Runtime, bool Reference);
    private sealed class Runtime : IDisposable
    {
        public IAudioEffect? Mic, Sounds;
        public bool Failed, MicOn, SoundsOn;
        public void Dispose()
        {
            try { Mic?.Dispose(); } catch { }
            if (!ReferenceEquals(Mic, Sounds)) { try { Sounds?.Dispose(); } catch { } }
        }
    }
}
