using System.Security.Cryptography;

namespace MkvFontMux;

internal static class ToolResolver
{
    public static FileInfo? ResolveMkvmergeBin(string? cliPath)
    {
        if (!string.IsNullOrWhiteSpace(cliPath))
        {
            var p = new FileInfo(Environment.ExpandEnvironmentVariables(cliPath));
            if (p.Exists)
            {
                return p;
            }
        }

        var envPath = Environment.GetEnvironmentVariable("MKVMERGE_BIN");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            var p = new FileInfo(Environment.ExpandEnvironmentVariables(envPath));
            if (p.Exists)
            {
                return p;
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = new FileInfo(Path.Combine(segment, OperatingSystem.IsWindows() ? "mkvmerge.exe" : "mkvmerge"));
            if (candidate.Exists)
            {
                return candidate;
            }
        }

        return null;
    }

    public static string GenerateRandomName(int length = 10)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        }

        return new string(chars);
    }
}
