using System.Text.Json;
using MkvFontMux.Gui.Models;

namespace MkvFontMux.Gui.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MkvFontMux");

        Directory.CreateDirectory(baseDirectory);
        _settingsPath = Path.Combine(baseDirectory, "gui-settings.json");
    }

    public GuiSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new GuiSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<GuiSettings>(json, SerializerOptions) ?? new GuiSettings();
        }
        catch
        {
            return new GuiSettings();
        }
    }

    public void Save(GuiSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
