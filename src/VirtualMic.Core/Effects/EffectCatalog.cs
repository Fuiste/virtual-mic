using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using VirtualMic.PluginApi;

namespace VirtualMic.Core.Effects;

public sealed record EffectRegistration(EffectDefinition Definition, IAudioEffectPlugin Factory, string Source)
{
    public override string ToString() => Definition.Name;
    public IReadOnlyDictionary<string, float> Parameters(IReadOnlyDictionary<string, float> values) =>
        Definition.Parameters.ToFrozenDictionary(p => p.Id, p =>
            values.TryGetValue(p.Id, out float value) && float.IsFinite(value)
                ? Math.Clamp(value, p.Minimum, p.Maximum) : p.DefaultValue);
}

public sealed class EffectCatalog : IDisposable
{
    private readonly Dictionary<string, EffectRegistration> registrations = new(StringComparer.Ordinal);
    private readonly List<PluginLoadContext> contexts = [];
    private readonly List<string> errors = [];
    public IReadOnlyList<string> Errors => errors;
    public IEnumerable<EffectRegistration> Effects => registrations.Values;
    public EffectRegistration? Find(string id) => registrations.GetValueOrDefault(id);

    public EffectCatalog()
    {
        Register(new BassBoostPlugin(), "built-in");
        Register(new DistortionPlugin(), "built-in");
    }

    public void Register(IAudioEffectPlugin plugin, string source = "plugin")
    {
        var definition = plugin.Definition;
        if (definition is null || definition.ApiVersion != EffectApi.Version)
            throw new InvalidDataException("unsupported effect api version");
        if (!ValidId(definition.Id) || string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 80)
            throw new InvalidDataException("invalid effect id or name");
        var parameters = definition.Parameters?.ToArray() ?? throw new InvalidDataException("parameters must not be null");
        if (parameters.Length > 16 || parameters.Any(p => p is null) || parameters.Select(p => p.Id).Distinct().Count() != parameters.Length)
            throw new InvalidDataException("invalid or duplicate parameters (maximum 16)");
        foreach (var p in parameters)
            if (!ValidId(p.Id) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 40 || p.Unit is null || p.Unit.Length > 16 ||
                !float.IsFinite(p.Minimum) || !float.IsFinite(p.Maximum) || !float.IsFinite(p.DefaultValue) || !float.IsFinite(p.Step) ||
                p.Minimum >= p.Maximum || p.DefaultValue < p.Minimum || p.DefaultValue > p.Maximum || p.Step <= 0)
                throw new InvalidDataException($"invalid parameter: {p.Id}");
        var snapshot = definition with { Parameters = Array.AsReadOnly(parameters) };
        if (!registrations.TryAdd(definition.Id, new(snapshot, plugin, source)))
            throw new InvalidDataException($"duplicate effect id: {definition.Id}");
    }

    public static bool ValidId(string? id) => id is not null && Regex.IsMatch(id, "^[a-z0-9][a-z0-9._-]{0,79}$", RegexOptions.CultureInvariant);

    // Call while audio is stopped. Only manifest-bearing subdirectories are loaded.
    public void LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal).Take(64))
            {
                string manifestPath = Path.Combine(folder, "plugin.json");
                if (!File.Exists(manifestPath)) continue;
                PluginLoadContext? context = null;
                bool keepContext = false;
                try
                {
                    if (new FileInfo(manifestPath).Length > 16384) throw new InvalidDataException("plugin manifest is too large");
                    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("empty manifest");
                    if (manifest.ApiVersion != EffectApi.Version) throw new InvalidDataException("unsupported plugin api version");
                    string entry = manifest.Assembly;
                    if (string.IsNullOrWhiteSpace(entry) || Path.GetFileName(entry) != entry || !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("assembly must name a dll in this plugin folder");
                    string assemblyPath = Path.GetFullPath(Path.Combine(folder, entry));
                    context = new PluginLoadContext(assemblyPath);
                    var assembly = context.LoadFromAssemblyPath(assemblyPath);
                    var types = assembly.GetExportedTypes().Where(t => !t.IsAbstract && !t.IsGenericType && typeof(IAudioEffectPlugin).IsAssignableFrom(t)).ToArray();
                    if (types.Length == 0) throw new InvalidDataException("no public IAudioEffectPlugin factory found");
                    foreach (var type in types)
                    {
                        try
                        {
                            Register((IAudioEffectPlugin)Activator.CreateInstance(type)!, Path.GetFileName(folder));
                            keepContext = true;
                        }
                        catch (Exception ex) { errors.Add($"{Path.GetFileName(folder)} / {type.Name}: {ex.GetBaseException().Message}"); }
                    }
                }
                catch (Exception ex) { errors.Add($"{Path.GetFileName(folder)}: {ex.GetBaseException().Message}"); }
                finally
                {
                    if (context is not null)
                    {
                        if (keepContext) contexts.Add(context);
                        else context.Unload();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { errors.Add($"plugins folder: {ex.Message}"); }
    }

    public void Dispose()
    {
        registrations.Clear();
        foreach (var context in contexts) context.Unload();
        contexts.Clear();
    }

    private sealed record Manifest(int ApiVersion, string Assembly);

    private sealed class PluginLoadContext(string path) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(path);
        protected override Assembly? Load(AssemblyName name)
        {
            // Share the host contract even inside a self-contained single-file app.
            // Never load a second PluginApi from the plugin's private dependencies.
            var contract = typeof(IAudioEffectPlugin).Assembly;
            if (name.Name == contract.GetName().Name) return contract;
            var resolved = resolver.ResolveAssemblyToPath(name);
            return resolved is null ? null : LoadFromAssemblyPath(resolved);
        }
        protected override nint LoadUnmanagedDll(string name)
        {
            var resolved = resolver.ResolveUnmanagedDllToPath(name);
            return resolved is null ? 0 : LoadUnmanagedDllFromPath(resolved);
        }
    }
}
