using Spectre.Console;

namespace MkvFontMux;

internal sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        var defaults = ConfigIni.LoadOrCreate();
        var options = CliOptions.Parse(args, defaults);
        if (options is null)
        {
            CliOptions.PrintHelp();
            return 1;
        }

        var titleMode = options.OnlyPrintMatchFont
            ? "[bold cyan]Font match mode (report only)[/]"
            : "[bold green]Mux mode[/]";

        AnsiConsole.Write(new Panel($"[bold white]MKV Font Mux Tool[/]\n[dim]Directory: {Markup.Escape(options.WorkDirectory.FullName)}[/]\n{titleMode}")
            .BorderColor(Color.Blue)
            .Expand());

        var service = new MuxService();
        var result = await service.RunAsync(new MuxRequest
        {
            WorkDirectory = options.WorkDirectory.FullName,
            MkvmergeBin = options.MkvmergeBin,
            ForceMatch = options.ForceMatch,
            FontDirectories = options.FontDirectories,
            DisableSubset = options.DisableSubset,
            SaveLog = options.SaveLog,
            Overwrite = options.Overwrite,
            RemoveTemp = options.RemoveTemp,
            OnlyPrintMatchFont = options.OnlyPrintMatchFont,
            SubtitleLanguage = options.SubtitleLanguage,
            PyftsubsetPath = options.PyftsubsetPath,
            Log = text => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(text)}[/]")
        });

        AnsiConsole.MarkupLine(result.Success
            ? $"[bold green]Completed:[/] {result.SucceededFiles}/{result.ProcessedFiles} succeeded."
            : $"[bold red]Completed with errors:[/] {result.SucceededFiles}/{result.ProcessedFiles} succeeded.");

        return result.Success ? 0 : 1;
    }
}
