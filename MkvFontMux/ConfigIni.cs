namespace MkvFontMux;

internal sealed record AppDefaults(string? MkvmergeBin, string? FontDirectory, string? PyftsubsetBin)
{
    public static readonly AppDefaults Empty = new(null, null, null);
}

internal static class ConfigIni
{
    private const string ConfigFileName = "config.ini";

    public static AppDefaults LoadOrCreate()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, GetDefaultTemplate());
            return AppDefaults.Empty;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(configPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            var splitAt = line.IndexOf('=');
            if (splitAt <= 0)
            {
                continue;
            }

            var key = line[..splitAt].Trim();
            var value = line[(splitAt + 1)..].Trim().Trim('"', '\'');
            values[key] = value;
        }

        return new AppDefaults(
            EmptyToNull(values.GetValueOrDefault("mkvmerge-bin")),
            EmptyToNull(values.GetValueOrDefault("font-directory")),
            EmptyToNull(values.GetValueOrDefault("pyftsubset-bin")));
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string GetDefaultTemplate()
    {
        return """
               # MkvFontMux default settings
               # Use ';' to separate multiple directories.
               mkvmerge-bin=
               font-directory=
               pyftsubset-bin=
               """;
    }
}
