using System.Diagnostics;

namespace MkvFontMux;

internal static class MkvMuxer
{
    public static async Task<(bool Success, string? ErrorText)> MuxAsync(
        string mkvmergeBin,
        string mkvPath,
        IReadOnlyList<(string FilePath, string LanguageCode)> assTracks,
        IReadOnlyList<FontAttachment> attachments,
        string outPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = mkvmergeBin,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outPath);
        psi.ArgumentList.Add(mkvPath);

        foreach (var assTrack in assTracks)
        {
            psi.ArgumentList.Add("--language");
            psi.ArgumentList.Add($"0:{assTrack.LanguageCode}");
            psi.ArgumentList.Add(assTrack.FilePath);
        }

        foreach (var item in attachments)
        {
            psi.ArgumentList.Add("--attachment-mime-type");
            psi.ArgumentList.Add(item.MimeType);
            psi.ArgumentList.Add("--attach-file");
            psi.ArgumentList.Add(item.FilePath);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return (false, "Failed to start mkvmerge process.");
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stderr = await stderrTask;

            return process.ExitCode == 0
                ? (true, null)
                : (false, string.IsNullOrWhiteSpace(stderr) ? "mkvmerge exited with non-zero code." : stderr);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
