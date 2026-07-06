namespace MkvFontMux;

public sealed class CliOptions(
    DirectoryInfo workDirectory,
    string? mkvmergeBin,
    bool forceMatch,
    IReadOnlyList<string>? fontDirectories,
    bool disableSubset,
    bool saveLog,
    bool overwrite,
    bool saveTemp,
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
    public bool SaveTemp { get; } = saveTemp;
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
        ["r"] = "save-temp",
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
            flags.Contains("save-temp"),
            flags.Contains("only-print-matchfont"),
            values.GetValueOrDefault("subtitle-language") ?? "zh-Hans",
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
        PrintOption("--mkvmerge-bin <path>, -m", "Mkvmerge executable path");
        PrintOption("--force-match, -f", "Force exact font name matching");
        PrintOption("--font-directory <d1;d2>, -d", "Custom font scan directories");
        PrintOption("--disable-subset, -n", "Disable font subsetting");
        PrintOption("--save-log, -l", "Save logs to mux.log");
        PrintOption("--overwrite, -o", "Overwrite source MKV");
        PrintOption("--save-temp, -r", "Save temporary files");
        PrintOption("--only-print-matchfont, -p", "Report font matching only");
        PrintOption("--subtitle-language <code>, -s", "Language code for ASS tracks (default: zh-Hans)");
        PrintOption("--pyftsubset-bin <path>, -y", "Pyftsubset executable path");
        Console.WriteLine();
        Console.WriteLine("  Defaults are read from config.ini in the executable directory.");
        Console.WriteLine("  Supported keys: mkvmerge-bin, font-directory, pyftsubset-bin");
    }

    private static void PrintOption(string option, string description)
    {
        Console.WriteLine($"  {option.PadRight(34)}{description}");
    }
}
