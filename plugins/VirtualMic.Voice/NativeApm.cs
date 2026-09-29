using System.Runtime.InteropServices;

namespace VirtualMic.Voice;

// C ABI from LSXPrime/webrtc-audio-processing, distributed by the pinned
// SoundFlow.Extensions.WebRtc.Apm package. No managed allocations per frame.
internal static unsafe class NativeApm
{
    private const string Library = "webrtc-apm";
    [DllImport(Library, EntryPoint = "webrtc_apm_create", CallingConvention = CallingConvention.Cdecl)] internal static extern nint Create();
    [DllImport(Library, EntryPoint = "webrtc_apm_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern void Destroy(nint apm);
    [DllImport(Library, EntryPoint = "webrtc_apm_config_create", CallingConvention = CallingConvention.Cdecl)] internal static extern nint ConfigCreate();
    [DllImport(Library, EntryPoint = "webrtc_apm_config_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern void ConfigDestroy(nint config);
    [DllImport(Library, EntryPoint = "webrtc_apm_config_set_echo_canceller", CallingConvention = CallingConvention.Cdecl)] internal static extern void Echo(nint config, int enabled, int mobile);
    [DllImport(Library, EntryPoint = "webrtc_apm_config_set_noise_suppression", CallingConvention = CallingConvention.Cdecl)] internal static extern void Noise(nint config, int enabled, int level);
    [DllImport(Library, EntryPoint = "webrtc_apm_config_set_high_pass_filter", CallingConvention = CallingConvention.Cdecl)] internal static extern void HighPass(nint config, int enabled);
    [DllImport(Library, EntryPoint = "webrtc_apm_apply_config", CallingConvention = CallingConvention.Cdecl)] internal static extern int Apply(nint apm, nint config);
    [DllImport(Library, EntryPoint = "webrtc_apm_initialize", CallingConvention = CallingConvention.Cdecl)] internal static extern int Initialize(nint apm);
    [DllImport(Library, EntryPoint = "webrtc_apm_stream_config_create", CallingConvention = CallingConvention.Cdecl)] internal static extern nint StreamCreate(int rate, nuint channels);
    [DllImport(Library, EntryPoint = "webrtc_apm_stream_config_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern void StreamDestroy(nint config);
    [DllImport(Library, EntryPoint = "webrtc_apm_process_stream", CallingConvention = CallingConvention.Cdecl)] internal static extern int Process(nint apm, float** input, nint source, nint destination, float** output);
    [DllImport(Library, EntryPoint = "webrtc_apm_process_reverse_stream", CallingConvention = CallingConvention.Cdecl)] internal static extern int Reverse(nint apm, float** input, nint source, nint destination, float** output);
    [DllImport(Library, EntryPoint = "webrtc_apm_set_stream_delay_ms", CallingConvention = CallingConvention.Cdecl)] internal static extern void Delay(nint apm, int milliseconds);
}
