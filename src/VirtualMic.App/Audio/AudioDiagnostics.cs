using System.IO;

namespace VirtualMic.App.Audio;

internal static class AudioDiagnostics
{
    private static readonly object Gate = new();
    public static string Describe(string stage, Exception? error)
    {
        string message = error is null ? stage : $"{stage}: {error.Message} (0x{error.HResult:X8})";
        try
        {
            lock (Gate)
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VirtualMic", "logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "audio.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}{error}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return message;
    }
}
