using Spectre.Console;

namespace MkvFontMux;

internal sealed class Program
{
    private static readonly HashSet<string> IgnoreFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "arial", "sans-serif"
    };

    public static async Task<int> Main(string[] args)
    {
        var defaults = ConfigIni.LoadOrCreate();
        var options = CliOptions.Parse(args, defaults);
        if (options is null)
        {
            CliOptions.PrintHelp();
            return 1;
        }

        AppLogger.Initialize(options.SaveLog ? Path.Combine(options.WorkDirectory.FullName, "mux.log") : null);

        if (!options.WorkDirectory.Exists)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] directory not found: [grey]{ConsoleSafe.Escape(options.WorkDirectory.FullName)}[/]");
            AppLogger.Error($"Directory not found: {options.WorkDirectory.FullName}");
            return 1;
        }

        var mkvmerge = ToolResolver.ResolveMkvmergeBin(options.MkvmergeBin);
        if (mkvmerge is null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] mkvmerge not found. Use --mkvmerge-bin, env MKVMERGE_BIN, or PATH.");
            AppLogger.Error("mkvmerge not found.");
            return 1;
        }

        var titleMode = options.OnlyPrintMatchFont
            ? "[bold cyan]Font match mode (report only)[/]"
            : "[bold green]Mux mode[/]";

        AnsiConsole.Write(new Panel($"[bold white]MKV Font Mux Tool[/]\n[dim]Directory: {ConsoleSafe.Escape(options.WorkDirectory.FullName)}[/]\n{titleMode}")
            .BorderColor(Color.Blue)
            .Expand());

        var smartMatch = !options.ForceMatch;
        var fontManager = new FontManager(options.FontDirectories, smartMatch);
        await fontManager.BuildIndexAsync();
        AppLogger.Info($"Font index count: {fontManager.Count}");

        var mkvFiles = options.WorkDirectory.EnumerateFiles("*.mkv", SearchOption.TopDirectoryOnly).ToArray();
        if (mkvFiles.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]No MKV files found in the directory.[/]");
            AppLogger.Warning("No MKV files found.");
            return 1;
        }

        var tempDir = Directory.CreateDirectory(Path.Combine(options.WorkDirectory.FullName, "temp_fonts_mux"));

        try
        {
            foreach (var mkv in mkvFiles)
            {
                await ProcessMkvAsync(mkv, options, mkvmerge, fontManager, tempDir);
            }
        }
        finally
        {
            if (options.RemoveTemp && tempDir.Exists)
            {
                tempDir.Delete(recursive: true);
                AnsiConsole.MarkupLine("[dim]Temporary files removed.[/]");
                AppLogger.Info("Temporary files removed.");
            }

            AppLogger.Dispose();
        }

        return 0;
    }

    private static async Task ProcessMkvAsync(
        FileInfo mkv,
        CliOptions options,
        FileInfo mkvmerge,
        FontManager fontManager,
        DirectoryInfo tempDir)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[blue]Processing: {ConsoleSafe.Escape(mkv.Name)}[/]"));

        var baseName = Path.GetFileNameWithoutExtension(mkv.Name);
        var assFiles = mkv.Directory!
            .EnumerateFiles("*.ass", SearchOption.TopDirectoryOnly)
            .Where(file => file.Name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (assFiles.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] no matching ASS subtitles found. Skipping.");
            AppLogger.Warning($"No matching ASS for {mkv.FullName}");
            return;
        }

        var neededFonts = new Dictionary<string, HashSet<char>>(StringComparer.OrdinalIgnoreCase);
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("[green]Parsing subtitle font usage...[/]", _ =>
            {
                foreach (var ass in assFiles)
                {
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

                return Task.CompletedTask;
            });

        var report = new Table().Border(TableBorder.Rounded).Title("Font Match Report");
        report.AddColumn("ASS Font");
        report.AddColumn("Status");
        report.AddColumn("Source File");
        report.AddColumn("Char Count");

        var validFonts = new List<(string AssFont, FontMatch Match, HashSet<char> Chars)>();

        foreach (var pair in neededFonts)
        {
            var matched = fontManager.FindFont(pair.Key);
            if (matched is null)
            {
                report.AddRow(ConsoleSafe.Escape(pair.Key), "[bold red]Missing[/]", "---", pair.Value.Count.ToString());
                continue;
            }

            report.AddRow(
                ConsoleSafe.Escape(pair.Key),
                "[green]OK[/]",
                $"{ConsoleSafe.Escape(matched.RealName)}\n[dim]({ConsoleSafe.Escape(Path.GetFileName(matched.FilePath))})[/]",
                pair.Value.Count.ToString());

            validFonts.Add((pair.Key, matched, pair.Value));
        }

        AnsiConsole.Write(report);

        if (options.OnlyPrintMatchFont)
        {
            AnsiConsole.MarkupLine("[italic dim]Font match report only. Skipping remaining steps.[/]");
            return;
        }

        if (validFonts.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] no valid fonts to process.");
            AppLogger.Warning($"No valid fonts for {mkv.FullName}");
            return;
        }

        var attachments = new List<FontAttachment>();
        var fontNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new SpinnerColumn(),
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn()
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Generating font subsets...[/]", maxValue: validFonts.Count);
                foreach (var item in validFonts)
                {
                    var randomName = ToolResolver.GenerateRandomName();
                    var mappedName = options.DisableSubset ? item.AssFont : randomName;
                    fontNameMap[item.AssFont] = mappedName;

                    var result = await FontSubsetter.ProcessAsync(
                        item.Match,
                        item.Chars,
                        tempDir.FullName,
                        randomName,
                        options.DisableSubset,
                        options.PyftsubsetPath);

                    if (result is not null)
                    {
                        var key = $"{result.FilePath}|{result.MimeType}";
                        if (dedupe.Add(key))
                        {
                            attachments.Add(result);
                        }
                    }

                    task.Increment(1);
                }
            });

        var rewrittenAss = AssRewriter.RewriteAssFiles(assFiles, fontNameMap, tempDir.FullName).ToArray();
        var outFile = options.Overwrite
            ? new FileInfo(Path.Combine(tempDir.FullName, $"temp_{mkv.Name}"))
            : new FileInfo(Path.Combine(Directory.CreateDirectory(Path.Combine(mkv.Directory!.FullName, "output")).FullName, mkv.Name));

        var muxResult = await MkvMuxer.MuxAsync(
            mkvmerge.FullName,
            mkv.FullName,
            rewrittenAss,
            attachments,
            outFile.FullName,
            options.SubtitleLanguage);

        if (!muxResult.Success)
        {
            AnsiConsole.MarkupLine("[red]Mux failed.[/]");
            AnsiConsole.Write(new Panel(ConsoleSafe.Escape(muxResult.ErrorText ?? "Unknown error")).Header("Error details").BorderColor(Color.Red));
            AppLogger.Error($"Mux failed for {mkv.FullName}: {muxResult.ErrorText}");
            return;
        }

        if (options.Overwrite)
        {
            File.Copy(outFile.FullName, mkv.FullName, overwrite: true);
            AnsiConsole.MarkupLine($"[bold green]OK:[/] overwrote {ConsoleSafe.Escape(mkv.Name)}");
            AppLogger.Info($"Overwrote MKV: {mkv.FullName}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold green]OK:[/] created output/{ConsoleSafe.Escape(mkv.Name)}");
            AppLogger.Info($"Created output MKV: {outFile.FullName}");
        }
    }
}
