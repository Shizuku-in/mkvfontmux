namespace MkvFontMux;

public sealed class MkvFileRunResult
{
    public string FileName { get; init; } = string.Empty;
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public List<string> MissingFonts { get; } = [];
    public string Message { get; set; } = string.Empty;
}