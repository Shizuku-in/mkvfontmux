using System.Text;
using System.Text.RegularExpressions;

namespace MkvFontMux;

internal static class AssParser
{
    public static IReadOnlyDictionary<string, HashSet<char>> Parse(string filepath)
    {
        var textByFont = new Dictionary<string, HashSet<char>>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(filepath, Encoding.UTF8);
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
        var cleaned = text;
        cleaned = Regex.Replace(cleaned, "\\\\N|\\\\n", string.Empty);
        cleaned = Regex.Replace(cleaned, "\\{.*?\\}", string.Empty);
        return cleaned;
    }
}
