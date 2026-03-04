namespace MkvFontMux;

public sealed class MuxRequest
{
    public required string WorkDirectory { get; init; }
    public bool UseConfigIniDefaults { get; init; } = true;
    public string? MkvmergeBin { get; init; }
    public bool ForceMatch { get; init; }
    public IReadOnlyList<string>? FontDirectories { get; init; }
    public bool DisableSubset { get; init; }
    public bool SaveLog { get; init; }
    public bool Overwrite { get; init; }
    public bool RemoveTemp { get; init; } = true;
    public bool OnlyPrintMatchFont { get; init; }
    public string SubtitleLanguage { get; init; } = "chi";
    public string? PyftsubsetPath { get; init; }
    public Action<string>? Log { get; init; }
}