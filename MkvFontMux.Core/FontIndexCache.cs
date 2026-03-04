using System.Text;

namespace MkvFontMux;

internal sealed class FontIndexCache
{
    private const int Magic = 0x4D464458;
    private const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, CachedFontFile> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static FontIndexCache Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new FontIndexCache();
            }

            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            var magic = reader.ReadInt32();
            if (magic != Magic)
            {
                return new FontIndexCache();
            }

            var version = reader.ReadInt32();
            if (version != CurrentVersion)
            {
                return new FontIndexCache();
            }

            var fileCount = reader.ReadInt32();
            if (fileCount < 0)
            {
                return new FontIndexCache();
            }

            var data = new FontIndexCache();
            for (var i = 0; i < fileCount; i++)
            {
                var filePath = reader.ReadString();
                var fileSize = reader.ReadInt64();
                var lastWriteUtcTicks = reader.ReadInt64();
                var entryCount = reader.ReadInt32();

                if (entryCount < 0)
                {
                    return new FontIndexCache();
                }

                var entries = new List<CachedFontEntry>(entryCount);
                for (var j = 0; j < entryCount; j++)
                {
                    entries.Add(new CachedFontEntry
                    {
                        FontIndex = reader.ReadInt32(),
                        Name = reader.ReadString()
                    });
                }

                data.Files[filePath] = new CachedFontFile
                {
                    FileSize = fileSize,
                    LastWriteUtcTicks = lastWriteUtcTicks,
                    Entries = entries
                };
            }

            return data;
        }
        catch
        {
            return new FontIndexCache();
        }
    }

    public void Save(string path)
    {
        try
        {
            Version = CurrentVersion;

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(Files.Count);

            foreach (var pair in Files)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value.FileSize);
                writer.Write(pair.Value.LastWriteUtcTicks);
                writer.Write(pair.Value.Entries.Count);

                foreach (var entry in pair.Value.Entries)
                {
                    writer.Write(entry.FontIndex);
                    writer.Write(entry.Name ?? string.Empty);
                }
            }
        }
        catch
        {
        }
    }

    public bool TryGet(string filePath, long fileSize, long lastWriteUtcTicks, out List<CachedFontEntry> entries)
    {
        entries = [];
        if (!Files.TryGetValue(filePath, out var file))
        {
            return false;
        }

        if (file.FileSize != fileSize || file.LastWriteUtcTicks != lastWriteUtcTicks)
        {
            return false;
        }

        if (file.Entries.Count == 0)
        {
            return false;
        }

        entries = file.Entries;
        return true;
    }

    public void Update(string filePath, long fileSize, long lastWriteUtcTicks, IReadOnlyList<CachedFontEntry> entries)
    {
        Files[filePath] = new CachedFontFile
        {
            FileSize = fileSize,
            LastWriteUtcTicks = lastWriteUtcTicks,
            Entries = [.. entries]
        };
    }

    public void RemoveUnscanned(HashSet<string> scannedFiles)
    {
        var stale = Files.Keys.Where(k => !scannedFiles.Contains(k)).ToArray();
        foreach (var key in stale)
        {
            Files.Remove(key);
        }
    }
}

internal sealed class CachedFontFile
{
    public long FileSize { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public List<CachedFontEntry> Entries { get; set; } = [];
}

internal sealed class CachedFontEntry
{
    public int FontIndex { get; set; }
    public string Name { get; set; } = string.Empty;
}
