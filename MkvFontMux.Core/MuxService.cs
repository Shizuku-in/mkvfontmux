namespace MkvFontMux;

public sealed class MuxService
{
    private static readonly HashSet<string> IgnoreFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "arial", "sans-serif"
    };

    private static readonly string[] SimplifiedChineseKeywords =
    [
        "sc", "chs", "zhs", "zh-cn", "gb", "gbk", "gb2312", "简体", "简中"
    ];

    private static readonly string[] TraditionalChineseKeywords =
    [
        "tc", "cht", "zht", "zh-tw", "big5", "繁体", "繁中"
    ];

    private static readonly string[] EnglishKeywords =
    [
        "en", "eng", "english", "英文", "英字"
    ];

    private static readonly string[] JapaneseKeywords =
    [
        "jp", "ja", "jpn", "japanese", "日文", "日语", "日字"
    ];

    public async Task<MuxRunResult> RunAsync(MuxRequest request, CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        void Log(string text)
        {
            messages.Add(text);
            request.Log?.Invoke(text);
        }

        var defaults = request.UseConfigIniDefaults ? ConfigIni.LoadOrCreate() : AppDefaults.Empty;
        var workDirectory = new DirectoryInfo(request.WorkDirectory);
        if (!workDirectory.Exists)
        {
            return MuxRunResult.Fail($"Directory not found: {workDirectory.FullName}");
        }

        var mkvmerge = ToolResolver.ResolveMkvmergeBin(request.MkvmergeBin ?? defaults.MkvmergeBin);
        if (mkvmerge is null)
        {
            return MuxRunResult.Fail("mkvmerge not found. Use settings, env MKVMERGE_BIN, or PATH.");
        }

        var pyftsubsetPath = request.PyftsubsetPath ?? defaults.PyftsubsetBin;
        AppLogger.Initialize(request.SaveLog ? Path.Combine(workDirectory.FullName, "mux.log") : null);

        var smartMatch = !request.ForceMatch;
        var fontManager = new FontManager(request.FontDirectories, smartMatch);
        await fontManager.BuildIndexAsync();
        Log($"Font index count: {fontManager.Count}");

        var mkvFiles = workDirectory.EnumerateFiles("*.mkv", SearchOption.TopDirectoryOnly).ToArray();
        if (mkvFiles.Length == 0)
        {
            return MuxRunResult.Fail("No MKV files found in the directory.");
        }

        var tempDir = Directory.CreateDirectory(Path.Combine(workDirectory.FullName, "temp_fonts_mux"));
        var result = new MuxRunResult { ProcessedFiles = mkvFiles.Length };

        try
        {
            foreach (var mkv in mkvFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileResult = await ProcessMkvAsync(mkv, request, mkvmerge, fontManager, tempDir, pyftsubsetPath, Log, cancellationToken);
                result.Files.Add(fileResult);

                if (fileResult.Success)
                {
                    result.SucceededFiles++;
                }
                else
                {
                    result.FailedFiles++;
                }
            }
        }
        finally
        {
            if (request.RemoveTemp && tempDir.Exists)
            {
                tempDir.Delete(recursive: true);
                Log("Temporary files removed.");
            }

            AppLogger.Dispose();
        }

        foreach (var message in messages)
        {
            result.Messages.Add(message);
        }

        result.Success = result.FailedFiles == 0;
        return result;
    }

    private static async Task<MkvFileRunResult> ProcessMkvAsync(
        FileInfo mkv,
        MuxRequest request,
        FileInfo mkvmerge,
        FontManager fontManager,
        DirectoryInfo tempDir,
        string? pyftsubsetPath,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var fileResult = new MkvFileRunResult
        {
            FileName = mkv.Name
        };

        log($"Processing: {mkv.Name}");

        var baseName = Path.GetFileNameWithoutExtension(mkv.Name);
        var assFiles = mkv.Directory!
            .EnumerateFiles("*.ass", SearchOption.TopDirectoryOnly)
            .Where(file => file.Name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var assLanguageByName = assFiles.ToDictionary(
            ass => ass.Name,
            ass => ResolveSubtitleLanguageCodeByKeyword(ass.Name, request.SubtitleLanguage),
            StringComparer.OrdinalIgnoreCase);

        foreach (var ass in assFiles)
        {
            log($"ASS language: {ass.Name} -> {assLanguageByName[ass.Name]}");
        }

        if (assFiles.Length == 0)
        {
            fileResult.Success = false;
            fileResult.Skipped = true;
            fileResult.Message = "No matching ASS subtitles found.";
            log($"Warning: no matching ASS subtitles found for {mkv.Name}, skipping.");
            return fileResult;
        }

        var neededFonts = new Dictionary<string, HashSet<char>>(StringComparer.OrdinalIgnoreCase);
        foreach (var ass in assFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = AssParser.Parse(ass.FullName);
            foreach (var pair in parsed)
            {
                if (IgnoreFonts.Contains(pair.Key))
                {
                    continue;
                }

                if (!neededFonts.TryGetValue(pair.Key, out var set))
                {
                    set = [];
                    neededFonts[pair.Key] = set;
                }

                foreach (var ch in pair.Value)
                {
                    set.Add(ch);
                }
            }
        }

        var validFonts = new List<(string AssFont, FontMatch Match, HashSet<char> Chars)>();
        foreach (var pair in neededFonts)
        {
            var matched = fontManager.FindFont(pair.Key);
            if (matched is null)
            {
                fileResult.MissingFonts.Add(pair.Key);
                log($"Missing font: {pair.Key}");
                continue;
            }

            validFonts.Add((pair.Key, matched, pair.Value));
        }

        if (request.OnlyPrintMatchFont)
        {
            fileResult.Success = true;
            fileResult.Message = fileResult.MissingFonts.Count == 0
                ? "Match report completed."
                : $"Match report completed with {fileResult.MissingFonts.Count} missing fonts.";
            log("Font match report only. Skipping remaining steps.");
            return fileResult;
        }

        if (validFonts.Count == 0)
        {
            fileResult.Success = false;
            fileResult.Message = "No valid fonts to process.";
            log($"Warning: no valid fonts for {mkv.Name}");
            return fileResult;
        }

        var attachments = new List<FontAttachment>();
        var fontNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in validFonts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var randomName = ToolResolver.GenerateRandomName();
            var mappedName = request.DisableSubset ? item.AssFont : randomName;
            fontNameMap[item.AssFont] = mappedName;

            var processedFont = await FontSubsetter.ProcessAsync(
                item.Match,
                item.Chars,
                tempDir.FullName,
                randomName,
                request.DisableSubset,
                pyftsubsetPath);

            if (processedFont is null)
            {
                continue;
            }

            var key = $"{processedFont.FilePath}|{processedFont.MimeType}";
            if (dedupe.Add(key))
            {
                attachments.Add(processedFont);
            }
        }

        var rewrittenAss = AssRewriter.RewriteAssFiles(assFiles, fontNameMap, tempDir.FullName).ToArray();
        var assTracks = rewrittenAss
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                var code = assLanguageByName.GetValueOrDefault(fileName) ?? request.SubtitleLanguage;
                return (FilePath: path, LanguageCode: code);
            })
            .ToArray();

        var outFile = request.Overwrite
            ? new FileInfo(Path.Combine(tempDir.FullName, $"temp_{mkv.Name}"))
            : new FileInfo(Path.Combine(Directory.CreateDirectory(Path.Combine(mkv.Directory!.FullName, "output")).FullName, mkv.Name));

        var muxResult = await MkvMuxer.MuxAsync(
            mkvmerge.FullName,
            mkv.FullName,
            assTracks,
            attachments,
            outFile.FullName);

        if (!muxResult.Success)
        {
            fileResult.Success = false;
            fileResult.Message = muxResult.ErrorText ?? "Mux failed.";
            log($"Mux failed for {mkv.Name}: {muxResult.ErrorText ?? "Unknown error"}");
            return fileResult;
        }

        if (request.Overwrite)
        {
            File.Copy(outFile.FullName, mkv.FullName, overwrite: true);
            log($"OK: overwrote {mkv.Name}");
        }
        else
        {
            log($"OK: created output/{mkv.Name}");
        }

        fileResult.Success = true;
        fileResult.Message = fileResult.MissingFonts.Count == 0
            ? "Completed."
            : $"Completed with {fileResult.MissingFonts.Count} missing fonts.";
        return fileResult;
    }

    private static string ResolveSubtitleLanguageCodeByKeyword(string assFileName, string fallbackCode)
    {
        var name = Path.GetFileNameWithoutExtension(assFileName).ToLowerInvariant();
        var tokens = name
            .Split([' ', '.', '-', '_', '[', ']', '(', ')', '{', '}', '+', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ContainsKeyword(name, tokens, TraditionalChineseKeywords))
        {
            return "cht";
        }

        if (ContainsKeyword(name, tokens, SimplifiedChineseKeywords))
        {
            return "chi";
        }

        if (ContainsKeyword(name, tokens, EnglishKeywords))
        {
            return "eng";
        }

        if (ContainsKeyword(name, tokens, JapaneseKeywords))
        {
            return "jpn";
        }

        return string.IsNullOrWhiteSpace(fallbackCode) ? "chi" : fallbackCode;
    }

    private static bool ContainsKeyword(string normalizedName, HashSet<string> tokens, IEnumerable<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            if (IsAsciiKeyword(keyword))
            {
                if (tokens.Contains(keyword))
                {
                    return true;
                }
            }
            else if (normalizedName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAsciiKeyword(string value)
    {
        return value.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
    }
}
