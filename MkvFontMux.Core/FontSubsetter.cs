using System.Diagnostics;

namespace MkvFontMux;

internal static class FontSubsetter
{
    public static async Task<FontAttachment?> ProcessAsync(
        FontMatch font,
        HashSet<char> chars,
        string outputDir,
        string newFamilyName,
        bool disableSubset,
        string? pyftsubsetPath)
    {
        if (disableSubset)
        {
            var mime = GetMime(font.FilePath);
            return new FontAttachment(font.FilePath, mime);
        }

        if (chars.Count == 0)
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(font.FilePath);
        if (font.FontIndex > 0)
        {
            name += $"_sub{font.FontIndex}";
        }

        var ext = Path.GetExtension(font.FilePath).Equals(".otf", StringComparison.OrdinalIgnoreCase) ? ".otf" : ".ttf";
        var outputPath = Path.Combine(outputDir, $"{name}_{newFamilyName}{ext}");
        var subsetCommand = pyftsubsetPath ?? "pyftsubset";

        var psi = new ProcessStartInfo
        {
            FileName = subsetCommand,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add(font.FilePath);
        psi.ArgumentList.Add($"--text={new string(chars.ToArray())}");
        psi.ArgumentList.Add($"--output-file={outputPath}");
        psi.ArgumentList.Add($"--font-number={font.FontIndex}");
        psi.ArgumentList.Add("--layout-features=*");
        psi.ArgumentList.Add("--name-IDs=*");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                AppLogger.Error($"Failed to start subset process for {font.FilePath}");
                return null;
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                AppLogger.Error($"pyftsubset failed for {font.FilePath}: {stderr}");
                return null;
            }

            if (!File.Exists(outputPath))
            {
                AppLogger.Error($"Subset output missing: {outputPath}");
                return null;
            }

            if (!FontNameObfuscator.TryObfuscate(outputPath, newFamilyName, 0, out var obfError))
            {
                AppLogger.Warning($"Name obfuscation skipped for {outputPath}: {obfError}");
            }

            return new FontAttachment(outputPath, GetMime(outputPath));
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Subset exception for {font.FilePath}: {ex.Message}");
            return null;
        }
    }

    private static string GetMime(string path)
    {
        return Path.GetExtension(path).Equals(".otf", StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.ms-opentype"
            : "application/x-truetype-font";
    }
}
