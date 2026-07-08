using System.Text;
using System.Text.RegularExpressions;

namespace MkvFontMux;

internal static class AssParser
{
    private static readonly Regex NewLineRegex = new(@"\\[Nn]", RegexOptions.Compiled);
    private static readonly Regex OverrideTagRegex = new(@"\{.*?\}", RegexOptions.Compiled);
    public static IReadOnlyDictionary<string, HashSet<char>> Parse(string filepath)
    {
        var textByFont = new Dictionary<string, HashSet<char>>(StringComparer.OrdinalIgnoreCase);
        var encoding = EncodingDetector.Detect(filepath);
        var lines = File.ReadAllLines(filepath, encoding);
        var styles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? section = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line;
                continue;
            }

            if (string.Equals(section, "[V4+ Styles]", StringComparison.OrdinalIgnoreCase) &&
                line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(',');
                if (parts.Length > 2)
                {
                    var styleName = parts[0].Replace("Style:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                    styles[styleName] = parts[1].Trim();
                }
            }
        }

        foreach (var raw in lines)
        {
            if (!raw.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = raw.Split(',', 10);
            if (parts.Length < 10)
            {
                continue;
            }

            var styleName = parts[3].Trim();
            var font = styles.TryGetValue(styleName, out var styleFont) ? styleFont : "Default";
            var text = RegexClean(parts[9].Trim());

            if (!textByFont.TryGetValue(font, out var set))
            {
                set = [];
                textByFont[font] = set;
            }

            foreach (var ch in text)
            {
                set.Add(ch);
            }
        }

        return textByFont;
    }

    private static string RegexClean(string text)
    {
        var cleaned = NewLineRegex.Replace(text, string.Empty);
        cleaned = OverrideTagRegex.Replace(cleaned, string.Empty);
        return cleaned;
    }
}
