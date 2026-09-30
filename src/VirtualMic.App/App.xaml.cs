using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using VirtualMic.App.Audio;

namespace VirtualMic.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Length == 2 && e.Args[0] == "--verify-gaming")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.BeginInvoke(async () =>
                {
                    try { await Gaming.GamingSmoke.Run(e.Args[1]); Shutdown(0); }
                    catch (Exception ex) { Directory.CreateDirectory(e.Args[1]); File.WriteAllText(Path.Combine(e.Args[1], "error.txt"), ex.ToString()); Shutdown(1); }
                });
                return;
            }
            if (e.Args.Length == 2 && e.Args[0] == "--verify-capture")
            {
                // Explicit local diagnostic: captures briefly into memory; no
                // output endpoint, recording, settings mutation or sound playback.
                Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        var store = new VirtualMic.Core.LibraryStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VirtualMic"));
                        var state = store.Load();
                        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
                        using var mic = new TimedCapture(state.MicrophoneId ?? throw new InvalidOperationException("no saved microphone"), false, ex => errors.Enqueue(ex.Message));
                        using var speakers = new TimedCapture(state.MonitorId ?? throw new InvalidOperationException("no saved speaker output"), true, ex => errors.Enqueue(ex.Message));
                        await Task.Delay(1200);
                        var report = new { capturePassed = mic.Available && speakers.Available && mic.ReliableTimestamps && speakers.ReliableTimestamps && mic.Timeline.BufferedFrames > 24000 && errors.IsEmpty,
                            microphoneFrames = mic.Timeline.BufferedFrames, speakerFrames = speakers.Timeline.BufferedFrames,
                            totalMicrophoneFrames = Interlocked.Read(ref mic.CapturedFrames), microphoneDiscontinuities = mic.Discontinuities, timelineGeneration = mic.Timeline.Generation,
                            timestampsValid = mic.ReliableTimestamps && speakers.ReliableTimestamps, errors = errors.ToArray(), audioPlayed = false, audioSaved = false };
                        File.WriteAllText(e.Args[1], JsonSerializer.Serialize(report)); Shutdown(report.capturePassed ? 0 : 1);
                    }
                    catch (Exception ex) { File.WriteAllText(e.Args[1], ex.ToString()); Shutdown(1); }
                });
                return;
            }
            if (e.Args.Length == 3 && e.Args[0] == "--verify-plugins")
            {
                try
                {
                    VirtualMic.Diagnostics.PluginSmoke.Run(e.Args[1]);
                    foreach (var test in VirtualMic.Diagnostics.BundledPluginTests.Cases(e.Args[1])) test.Run();
                    File.WriteAllText(e.Args[2], "{\"pluginSmokePassed\":true,\"audioOpened\":false}"); Shutdown(0);
                }
                catch (Exception ex) { File.WriteAllText(e.Args[2], ex.ToString()); Shutdown(1); }
                return;
            }
            if (e.Args.Length == 2 && e.Args[0] == "--list-devices")
            {
                var inventory = new { capture = AudioEngine.Devices(DataFlow.Capture), render = AudioEngine.Devices(DataFlow.Render) };
                File.WriteAllText(e.Args[1], JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0); return;
            }
            if (e.Args.Length == 2 && e.Args[0] == "--verify-library")
            {
                var testWindow = new MainWindow(false, Path.Combine(e.Args[1], Guid.NewGuid().ToString("N")));
                MainWindow = testWindow;
                Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        await testWindow.VerifyLibraryLifecycle();
                        File.WriteAllText(Path.Combine(e.Args[1], "result.json"), "{\"librarySmokePassed\":true,\"audioOpened\":false}");
                        Shutdown(0);
                    }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(e.Args[1], "error.txt"), ex.ToString()); Shutdown(1); }
                });
                return;
            }
            bool render = e.Args.Length == 2 && e.Args[0] is "--render-preview" or "--render-compact" or "--render-empty" or "--render-error" or "--render-cleanup";
            var window = new MainWindow(render);
            MainWindow = window;
            if (render)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -10000; window.Top = -10000; window.ShowInTaskbar = false;
                window.ShowActivated = false;
                window.Show();
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    try
                    {
                        window.VerifyUi(); window.UpdateLayout();
                        if (e.Args[0] is "--render-cleanup" or "--render-compact") window.CleanVoicePreview();
                        if (e.Args[0] == "--render-empty") window.EmptyPreview();
                        if (e.Args[0] == "--render-error") window.ErrorPreview();
                        if (e.Args[0] == "--render-compact") { window.Width = window.MinWidth; window.Height = window.MinHeight; }
                        window.UpdateLayout();
                        var surface = window.Root;
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth) + 56, (int)Math.Ceiling(surface.ActualHeight) + 34, 96, 96, PixelFormats.Pbgra32);
                        var drawing = new DrawingVisual();
                        using (var context = drawing.RenderOpen())
                        {
                            context.DrawRectangle((Brush)FindResource("Bg"), null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                            context.DrawRectangle(new VisualBrush(surface) { Stretch = Stretch.Fill }, null, new Rect(28, 18, surface.ActualWidth, surface.ActualHeight));
                        }
                        bitmap.Render(drawing);
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var stream = File.Create(e.Args[1])) png.Save(stream);
                        File.WriteAllText(e.Args[1] + ".json", JsonSerializer.Serialize(new { uiSmokePassed = true, audioOpened = false, width = bitmap.PixelWidth, height = bitmap.PixelHeight }));
                        Shutdown(0);
                    }
                    catch (Exception ex) { File.WriteAllText(e.Args[1] + ".error.txt", ex.ToString()); Shutdown(1); }
                });
            }
            else window.Show();
        }
        catch (Exception ex)
        {
            if (e.Args.Length == 2) File.WriteAllText(e.Args[1] + ".error.txt", ex.ToString());
            else MessageBox.Show($"virtual mic could not start.\n\n{ex.Message}", "virtual mic", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
