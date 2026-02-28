using Spectre.Console;
using System.Buffers.Binary;
using System.Text;

namespace MkvFontMux;

internal sealed class FontManager(IReadOnlyList<string>? customDirectories = null, bool smartMatch = true)
{
    private static readonly string[] SmartSuffixes = ["_gbk", "_gb2312", "_big5", "_jis", "_kr"];
    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf", ".ttc" };
    private static readonly string CachePath = Path.Combine(AppContext.BaseDirectory, "font_index_cache.db");
    private static bool _codePagesRegistered;

    private readonly Dictionary<string, FontMatch> _fontMap = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _fontMap.Count;

    public async Task BuildIndexAsync(CancellationToken cancellationToken = default)
    {
        var targetDirectories = customDirectories?.Count > 0
            ? customDirectories
            : GetSystemFontDirectories();
        var cache = FontIndexCache.Load(CachePath);
        var scannedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cacheHit = 0;
        var cacheMiss = 0;

        await AnsiConsole.Progress()
            .AutoRefresh(true)
            .HideCompleted(true)
            .Columns(new ProgressColumn[]
            {
                new SpinnerColumn(),
                new TaskDescriptionColumn()
            })
            .StartAsync(async context =>
            {
                var task = context.AddTask("[cyan]Scanning fonts...[/]", maxValue: 100);

                foreach (var dir in targetDirectories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                    {
                        continue;
                    }

                    await foreach (var fontPath in EnumerateFontFilesAsync(dir, cancellationToken))
                    {
                        scannedFiles.Add(fontPath);
                        if (TryProcessFromCache(cache, fontPath))
                        {
                            cacheHit++;
                        }
                        else
                        {
                            ProcessFontFile(fontPath, cache);
                            cacheMiss++;
                        }

                        task.Description = $"[cyan]Fonts indexed: {_fontMap.Count} names...[/]";
                    }
                }
            });

        cache.RemoveUnscanned(scannedFiles);
        cache.Save(CachePath);
        AppLogger.Info($"Font index cache: hit={cacheHit}, miss={cacheMiss}, files={scannedFiles.Count}");

        AnsiConsole.MarkupLine($"[green][[OK]][/] Font index built: [bold cyan]{_fontMap.Count}[/] names.");
    }

    public FontMatch? FindFont(string assName)
    {
        var target = Normalize(assName);
        if (_fontMap.TryGetValue(target, out var directMatch))
        {
            return directMatch;
        }

        if (!smartMatch)
        {
            return null;
        }

        foreach (var suffix in SmartSuffixes)
        {
            if (!target.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var clean = target[..^suffix.Length];
            if (_fontMap.TryGetValue(clean, out var fallback))
            {
                return fallback;
            }
        }

        return null;
    }

    public static IEnumerable<string> GetSystemFontDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windows))
            {
                yield return Path.Combine(windows, "Fonts");
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "Microsoft", "Windows", "Fonts");
            }

            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "/Library/Fonts";
            yield return "/System/Library/Fonts";

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                yield return Path.Combine(home, "Library", "Fonts");
            }

            yield break;
        }

        yield return "/usr/share/fonts";
        var linuxHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(linuxHome))
        {
            yield return Path.Combine(linuxHome, ".local", "share", "fonts");
        }
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private async IAsyncEnumerable<string> EnumerateFontFilesAsync(string rootDir, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        foreach (var file in Directory.EnumerateFiles(rootDir, "*.*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FontExtensions.Contains(Path.GetExtension(file)))
            {
                continue;
            }

            yield return file;
            await Task.Yield();
        }
    }

    private bool TryProcessFromCache(FontIndexCache cache, string filePath)
    {
        var info = new FileInfo(filePath);
        if (!cache.TryGet(filePath, info.Length, info.LastWriteTimeUtc.Ticks, out var entries))
        {
            return false;
        }

        foreach (var entry in entries)
        {
            RegisterName(entry.Name, filePath, entry.FontIndex);
        }

        return true;
    }

    private void ProcessFontFile(string filePath, FontIndexCache cache)
    {
        var info = new FileInfo(filePath);
        var parsedEntries = new List<CachedFontEntry>();
        var ext = Path.GetExtension(filePath);

        try
        {
            if (ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase))
            {
                var count = ReadTtcFontCount(filePath);
                if (count <= 0)
                {
                    RegisterSingleFont(filePath, 0, parsedEntries);
                }
                else
                {
                    for (var fontIndex = 0; fontIndex < count; fontIndex++)
                    {
                        RegisterSingleFont(filePath, fontIndex, parsedEntries);
                    }
                }
            }
            else
            {
                RegisterSingleFont(filePath, 0, parsedEntries);
            }

            cache.Update(filePath, info.Length, info.LastWriteTimeUtc.Ticks, parsedEntries);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to process font file: {filePath} - {ex.Message}");
        }
    }

    private void RegisterSingleFont(string filePath, int fontIndex, List<CachedFontEntry> parsedEntries)
    {
        try
        {
            var names = ReadFontNames(filePath, fontIndex);
            var addedAny = false;

            foreach (var name in names)
            {
                RegisterName(name, filePath, fontIndex);
                parsedEntries.Add(new CachedFontEntry
                {
                    FontIndex = fontIndex,
                    Name = name
                });
                addedAny = true;
            }

            if (!addedAny)
            {
                var fallbackAdded = TryRegisterFallbackNamesFromFileName(filePath, fontIndex, parsedEntries);
                if (fallbackAdded)
                {
                    AppLogger.Warning($"Unreadable font table, fallback to filename aliases: {filePath} (index={fontIndex})");
                }
                else
                {
                    AppLogger.Warning($"Skipped unreadable font: {filePath} (index={fontIndex})");
                }
            }
        }
        catch (Exception ex)
        {
            var fallbackAdded = TryRegisterFallbackNamesFromFileName(filePath, fontIndex, parsedEntries);
            if (fallbackAdded)
            {
                AppLogger.Warning($"Failed to read font table, fallback to filename aliases: {filePath} (index={fontIndex}) - {ex.Message}");
            }
            else
            {
                AppLogger.Warning($"Failed to register font: {filePath} (index={fontIndex}) - {ex.Message}");
            }
        }
    }

    private bool TryRegisterFallbackNamesFromFileName(string filePath, int fontIndex, List<CachedFontEntry> parsedEntries)
    {
        var baseName = Path.GetFileNameWithoutExtension(filePath).Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return false;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            baseName
        };

        foreach (var part in baseName.Split(['&', '＆'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            candidates.Add(part);

            if (part.EndsWith("(P)", StringComparison.OrdinalIgnoreCase) && part.Length > 3)
            {
                candidates.Add(part[..^3].Trim());
            }
            else if (part.EndsWith("P", StringComparison.OrdinalIgnoreCase) && part.Length > 1)
            {
                candidates.Add(part[..^1].Trim());
            }
        }

        var added = false;
        foreach (var name in candidates)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            RegisterName(name, filePath, fontIndex);
            parsedEntries.Add(new CachedFontEntry
            {
                FontIndex = fontIndex,
                Name = name
            });
            added = true;
        }

        return added;
    }

    private static IEnumerable<string> ReadFontNames(string filePath, int fontIndex)
    {
        using var stream = File.OpenRead(filePath);
        if (!TryGetSfntOffset(stream, fontIndex, out var sfntOffset))
        {
            return [];
        }

        return ReadNameRecords(stream, sfntOffset).ToArray();
    }

    private static int ReadTtcFontCount(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        if (!TryReadTag(stream, out var tag) || !string.Equals(tag, "ttcf", StringComparison.Ordinal))
        {
            return 0;
        }

        _ = ReadUInt32BigEndian(stream);
        var fontCount = ReadUInt32BigEndian(stream);
        if (fontCount > int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)fontCount;
    }

    private static bool TryGetSfntOffset(Stream stream, int fontIndex, out uint sfntOffset)
    {
        sfntOffset = 0;
        stream.Position = 0;

        if (!TryReadTag(stream, out var tag))
        {
            return false;
        }

        if (!string.Equals(tag, "ttcf", StringComparison.Ordinal))
        {
            return fontIndex == 0;
        }

        _ = ReadUInt32BigEndian(stream);
        var count = ReadUInt32BigEndian(stream);
        if (fontIndex < 0 || fontIndex >= count)
        {
            return false;
        }

        stream.Position = 12 + (fontIndex * 4L);
        sfntOffset = ReadUInt32BigEndian(stream);
        return true;
    }

    private static IEnumerable<string> ReadNameRecords(Stream stream, uint sfntOffset)
    {
        var tableDirOffset = sfntOffset + 12u;
        stream.Position = sfntOffset + 4u;
        var numTables = ReadUInt16BigEndian(stream);

        uint? nameTableOffset = null;
        uint? nameTableLength = null;

        for (var i = 0; i < numTables; i++)
        {
            stream.Position = tableDirOffset + (i * 16u);
            if (!TryReadTag(stream, out var tag))
            {
                break;
            }

            _ = ReadUInt32BigEndian(stream);
            var tableOffset = ReadUInt32BigEndian(stream);
            var tableLength = ReadUInt32BigEndian(stream);

            if (string.Equals(tag, "name", StringComparison.Ordinal))
            {
                nameTableOffset = tableOffset;
                nameTableLength = tableLength;
                break;
            }
        }

        if (nameTableOffset is null || nameTableLength is null)
        {
            yield break;
        }

        var tableStart = sfntOffset + nameTableOffset.Value;
        var tableEnd = tableStart + nameTableLength.Value;
        if (tableEnd > stream.Length)
        {
            yield break;
        }

        stream.Position = tableStart;
        _ = ReadUInt16BigEndian(stream);
        var nameCount = ReadUInt16BigEndian(stream);
        var stringStorageOffset = ReadUInt16BigEndian(stream);

        var recordsStart = tableStart + 6u;
        var storageStart = tableStart + stringStorageOffset;

        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < nameCount; i++)
        {
            stream.Position = recordsStart + (i * 12u);
            var platformId = ReadUInt16BigEndian(stream);
            var encodingId = ReadUInt16BigEndian(stream);
            _ = ReadUInt16BigEndian(stream);
            var nameId = ReadUInt16BigEndian(stream);
            var length = ReadUInt16BigEndian(stream);
            var offset = ReadUInt16BigEndian(stream);

            if (nameId is not (1 or 4 or 6))
            {
                continue;
            }

            var strStart = storageStart + offset;
            var strEnd = strStart + length;
            if (strStart >= tableEnd || strEnd > tableEnd)
            {
                continue;
            }

            stream.Position = strStart;
            var raw = ReadBytes(stream, length);
            var decoded = DecodeNameString(platformId, encodingId, raw).Trim();
            if (string.IsNullOrWhiteSpace(decoded))
            {
                continue;
            }

            if (dedupe.Add(decoded))
            {
                yield return decoded;
            }
        }
    }

    private static string DecodeNameString(ushort platformId, ushort encodingId, byte[] raw)
    {
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        if (platformId is 0 or 3)
        {
            return Encoding.BigEndianUnicode.GetString(raw).Trim('\0');
        }

        if (platformId == 1)
        {
            EnsureCodePagesRegistered();
            try
            {
                return Encoding.GetEncoding(10000).GetString(raw).Trim('\0');
            }
            catch
            {
                return Encoding.Latin1.GetString(raw).Trim('\0');
            }
        }

        if (encodingId == 1 && raw.Length % 2 == 0)
        {
            return Encoding.BigEndianUnicode.GetString(raw).Trim('\0');
        }

        try
        {
            return Encoding.UTF8.GetString(raw).Trim('\0');
        }
        catch
        {
            return Encoding.Latin1.GetString(raw).Trim('\0');
        }
    }

    private static void EnsureCodePagesRegistered()
    {
        if (_codePagesRegistered)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _codePagesRegistered = true;
    }

    private static bool TryReadTag(Stream stream, out string tag)
    {
        var bytes = ReadBytes(stream, 4);
        if (bytes.Length != 4)
        {
            tag = string.Empty;
            return false;
        }

        tag = Encoding.ASCII.GetString(bytes);
        return true;
    }

    private static byte[] ReadBytes(Stream stream, int count)
    {
        var buffer = new byte[count];
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = stream.Read(buffer, totalRead, count - totalRead);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        if (totalRead == count)
        {
            return buffer;
        }

        return buffer[..totalRead];
    }

    private static ushort ReadUInt16BigEndian(Stream stream)
    {
        var bytes = ReadBytes(stream, 2);
        if (bytes.Length != 2)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading UInt16.");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    private static uint ReadUInt32BigEndian(Stream stream)
    {
        var bytes = ReadBytes(stream, 4);
        if (bytes.Length != 4)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading UInt32.");
        }

        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private void RegisterName(string? name, string filePath, int fontIndex)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var normalized = Normalize(name);
        var match = new FontMatch(filePath, fontIndex, name);

        _fontMap[normalized] = match;
        _fontMap[normalized.Replace(" ", string.Empty)] = match;
    }
}

internal sealed record FontMatch(string FilePath, int FontIndex, string RealName);