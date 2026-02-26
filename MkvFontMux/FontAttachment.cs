namespace MkvFontMux;

internal sealed class FontAttachment(string filePath, string mimeType)
{
    public string FilePath { get; } = filePath;
    public string MimeType { get; } = mimeType;
}
