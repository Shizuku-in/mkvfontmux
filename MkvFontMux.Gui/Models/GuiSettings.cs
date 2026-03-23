namespace MkvFontMux.Gui.Models;

public sealed class GuiSettings
{
    public bool HasCompletedOnboarding { get; set; }
    public string? MkvmergePath { get; set; }
    public string? PyftsubsetPath { get; set; }
    public List<string> FontDirectories { get; set; } = [];
    public bool UseSmartFontMatching { get; set; } = true;
    public bool EnableFontSubsetting { get; set; } = true;
    public bool RemoveTemporaryFiles { get; set; } = true;
    public bool SaveMuxLogBesideSource { get; set; }
    public bool OverwriteSourceFiles { get; set; }
    public string SubtitleLanguage { get; set; } = "chi";
}
