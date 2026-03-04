using System.Text;

namespace MkvFontMux;

internal static class AppLogger
{
    private static readonly object Sync = new();
    private static StreamWriter? _writer;

    public static void Initialize(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _writer = new StreamWriter(filePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
            {
                AutoFlush = true
            };
            Info($"Log started: {DateTimeOffset.Now:O}");
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warning(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            _writer?.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}");
        }
    }

    public static void Dispose()
    {
        lock (Sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
