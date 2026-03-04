using System.Text;
using System.Text.RegularExpressions;

namespace MkvFontMux;

internal static class AssRewriter
{
    public static IEnumerable<string> RewriteAssFiles(IEnumerable<FileInfo> assFiles, IReadOnlyDictionary<string, string> fontMap, string tempDir)
    {
        if (fontMap.Count == 0)
        {
            return assFiles.Select(x => x.FullName);
        }

        var tempAssDir = Directory.CreateDirectory(Path.Combine(tempDir, "temp_ass"));
        var normalizedMap = fontMap.ToDictionary(kvp => Normalize(kvp.Key), kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var ass in assFiles)
        {
            var lines = File.ReadAllLines(ass.FullName, Encoding.UTF8).ToList();
            var output = new List<string>(lines.Count + fontMap.Count + 8);
            var inScriptInfo = false;
            var insertedMap = false;

            foreach (var sourceLine in lines)
            {
                var line = sourceLine;
                var stripped = line.Trim();

                if (stripped.StartsWith("[") && stripped.EndsWith("]"))
                {
                    if (string.Equals(stripped, "[Script Info]", StringComparison.OrdinalIgnoreCase))
                    {
                        inScriptInfo = true;
                        insertedMap = false;
                    }
                    else
                    {
                        if (inScriptInfo && !insertedMap)
                        {
                            foreach (var pair in fontMap)
                            {
                                output.Add($"; FontMap: {pair.Key} -> {pair.Value}");
                            }

                            insertedMap = true;
                        }

                        inScriptInfo = false;
                    }
                }

                if (stripped.StartsWith("Style:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(',');
                    if (parts.Length > 2)
                    {
                        var original = parts[1].Trim();
                        if (normalizedMap.TryGetValue(Normalize(original), out var replacement))
                        {
                            parts[1] = replacement;
                            line = string.Join(",", parts);
                        }
                    }
                }

                if (line.Contains("\\fn", StringComparison.Ordinal))
                {
                    line = Regex.Replace(
                        line,
                        "\\\\fn([^\\\\}]+)",
                        match =>
                        {
                            var fontName = match.Groups[1].Value;
                            return normalizedMap.TryGetValue(Normalize(fontName), out var replacement)
                                ? $"\\fn{replacement}"
                                : match.Value;
                        });
                }

                output.Add(line);
            }

            if (inScriptInfo && !insertedMap)
            {
                output.Add(string.Empty);
                foreach (var pair in fontMap)
                {
                    output.Add($"; FontMap: {pair.Key} -> {pair.Value}");
                }
            }

            if (!output.Any(line => string.Equals(line.Trim(), "[Script Info]", StringComparison.OrdinalIgnoreCase)))
            {
                var header = new List<string> { "[Script Info]" };
                foreach (var pair in fontMap)
                {
                    header.Add($"; FontMap: {pair.Key} -> {pair.Value}");
                }

                header.Add(string.Empty);
                output = header.Concat(output).ToList();
            }

            var outPath = Path.Combine(tempAssDir.FullName, ass.Name);
            File.WriteAllLines(outPath, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            result.Add(outPath);
        }

        return result;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
