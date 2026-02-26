using Spectre.Console;

namespace MkvFontMux;

internal static class ConsoleSafe
{
    public static string Escape(string? value) => Markup.Escape(value ?? string.Empty);
}
