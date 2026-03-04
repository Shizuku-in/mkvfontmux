namespace MkvFontMux;

public sealed class CliOptions(
    DirectoryInfo workDirectory,
    string? mkvmergeBin,
    bool forceMatch,
    IReadOnlyList<string>? fontDirectories,
    bool disableSubset,
    bool saveLog,
    bool overwrite,
    bool removeTemp,
    bool onlyPrintMatchFont,
    string subtitleLanguage,
    string? pyftsubsetPath)
{
    public DirectoryInfo WorkDirectory { get; } = workDirectory;
    public string? MkvmergeBin { get; } = mkvmergeBin;
    public bool ForceMatch { get; } = forceMatch;
    public IReadOnlyList<string>? FontDirectories { get; } = fontDirectories;
    public bool DisableSubset { get; } = disableSubset;
    public bool SaveLog { get; } = saveLog;
    public bool Overwrite { get; } = overwrite;
    public bool RemoveTemp { get; } = removeTemp;
    public bool OnlyPrintMatchFont { get; } = onlyPrintMatchFont;
    public string SubtitleLanguage { get; } = subtitleLanguage;
    public string? PyftsubsetPath { get; } = pyftsubsetPath;

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m"] = "mkvmerge-bin",
        ["f"] = "force-match",
        ["d"] = "font-directory",
        ["n"] = "disable-subset",
        ["l"] = "save-log",
        ["o"] = "overwrite",
        ["r"] = "remove-temp",
        ["p"] = "only-print-matchfont",
        ["s"] = "subtitle-language",
        ["y"] = "pyftsubset-bin"
    };

    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mkvmerge-bin",
        "font-directory",
        "subtitle-language",
        "pyftsubset-bin"
    };

    public static CliOptions? Parse(string[] args, AppDefaults defaults)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? dirArg = null;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!TryParseOptionToken(token, out var key, out var inlineValue))
            {
                dirArg ??= token;
                continue;
            }

            var optionValue = inlineValue;
            if (optionValue is null && ValueOptions.Contains(key) && i + 1 < args.Length && !IsOptionToken(args[i + 1]))
            {
                optionValue = args[++i];
            }

            if (optionValue is not null)
            {
                values[key] = optionValue;
            }
            else if (ValueOptions.Contains(key))
            {
                values[key] = string.Empty;
            }
            else
            {
                flags.Add(key);
            }
        }

        if (string.IsNullOrWhiteSpace(dirArg))
        {
            return null;
        }

        IReadOnlyList<string>? fontDirs = null;
        var fontDirectoryValue = values.GetValueOrDefault("font-directory") ?? defaults.FontDirectory;
        if (!string.IsNullOrWhiteSpace(fontDirectoryValue))
        {
            fontDirs = fontDirectoryValue
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }

        return new CliOptions(
            new DirectoryInfo(dirArg.Trim('"', '\'')),
            values.GetValueOrDefault("mkvmerge-bin") ?? defaults.MkvmergeBin,
            flags.Contains("force-match"),
            fontDirs,
            flags.Contains("disable-subset"),
            flags.Contains("save-log"),
            flags.Contains("overwrite"),
            flags.Contains("remove-temp"),
            flags.Contains("only-print-matchfont"),
            values.GetValueOrDefault("subtitle-language") ?? "chi",
            values.GetValueOrDefault("pyftsubset-bin") ?? defaults.PyftsubsetBin);
    }

    private static bool IsOptionToken(string token)
    {
        return token.StartsWith("--", StringComparison.Ordinal) || token.StartsWith("-", StringComparison.Ordinal);
    }

    private static bool TryParseOptionToken(string token, out string key, out string? inlineValue)
    {
        key = string.Empty;
        inlineValue = null;

        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var body = token[2..];
            var eqAt = body.IndexOf('=');
            if (eqAt >= 0)
            {
                key = body[..eqAt];
                inlineValue = body[(eqAt + 1)..];
            }
            else
            {
                key = body;
            }

            return !string.IsNullOrWhiteSpace(key);
        }

        if (token.StartsWith("-", StringComparison.Ordinal) && token.Length > 1)
        {
            var alias = token[1..];
            if (Aliases.TryGetValue(alias, out var mapped))
            {
                key = mapped;
                return true;
            }
        }

        return false;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: MkvFontMux <dir> [options]");
        Console.WriteLine("  --mkvmerge-bin <path>, -m   Path to mkvmerge executable");
        Console.WriteLine("  --force-match, -f           Force exact font name matching");
        Console.WriteLine("  --font-directory <d1;d2>, -d Custom font scan directories");
        Console.WriteLine("  --disable-subset, -n        Disable font subsetting");
        Console.WriteLine("  --save-log, -l              Save logs to mux.log");
        Console.WriteLine("  --overwrite, -o             Overwrite source MKV");
        Console.WriteLine("  --remove-temp, -r           Remove temporary files");
        Console.WriteLine("  --only-print-matchfont, -p  Report font matching only");
        Console.WriteLine("  --subtitle-language <code>, -s Language code for ASS tracks (default: chi)");
        Console.WriteLine("  --pyftsubset-bin <path>, -y Optional pyftsubset executable path");
        Console.WriteLine("  defaults from exe-dir config.ini: mkvmerge-bin/font-directory/pyftsubset-bin");
    }
}
