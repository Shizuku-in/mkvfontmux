namespace MkvFontMux.Gui.Services;

public static class PlatformFontDirectories
{
    public static IReadOnlyList<string> GetDefaultDirectories()
    {
        var results = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windows))
            {
                results.Add(Path.Combine(windows, "Fonts"));
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                results.Add(Path.Combine(localAppData, "Microsoft", "Windows", "Fonts"));
            }

            return results;
        }

        if (OperatingSystem.IsMacOS())
        {
            results.Add("/Library/Fonts");
            results.Add("/System/Library/Fonts");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                results.Add(Path.Combine(home, "Library", "Fonts"));
            }

            return results;
        }

        results.Add("/usr/share/fonts");
        var linuxHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(linuxHome))
        {
            results.Add(Path.Combine(linuxHome, ".local", "share", "fonts"));
        }

        return results;
    }
}
