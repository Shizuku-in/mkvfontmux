using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using MkvFontMux.Gui.Models;
using MkvFontMux.Gui.Services;

namespace MkvFontMux.Gui.Views;

public partial class SettingsWindow : Window
{
    private readonly bool _forceInitialSetup;
    private readonly List<string> _fontDirectories;
    private bool _saved;

    public SettingsWindow()
        : this(new GuiSettings(), false)
    {
    }

    public SettingsWindow(GuiSettings currentSettings, bool forceInitialSetup)
    {
        InitializeComponent();
        Opened += OnOpened;
        _forceInitialSetup = forceInitialSetup;
        _fontDirectories = [.. currentSettings.FontDirectories];

        IntroText.Text = forceInitialSetup
            ? "Review the defaults and press Save to unlock drag-and-drop."
            : "Adjust tool paths, font folders, and processing defaults.";

        CancelButton.IsVisible = !forceInitialSetup;
        CloseButton.IsVisible = !forceInitialSetup;
        DefaultFontsTextBlock.Text = $"System font folders: {string.Join("  |  ", PlatformFontDirectories.GetDefaultDirectories())}";

        MkvmergePathTextBox.Text = currentSettings.MkvmergePath;
        PyftsubsetPathTextBox.Text = currentSettings.PyftsubsetPath;
        UseSmartMatchCheckBox.IsChecked = currentSettings.UseSmartFontMatching;
        EnableSubsetCheckBox.IsChecked = currentSettings.EnableFontSubsetting;
        RemoveTempCheckBox.IsChecked = currentSettings.RemoveTemporaryFiles;
        SaveSidecarLogCheckBox.IsChecked = currentSettings.SaveMuxLogBesideSource;
        OverwriteSourceCheckBox.IsChecked = currentSettings.OverwriteSourceFiles;
        SubtitleLanguageComboBox.SelectedIndex = currentSettings.SubtitleLanguage switch
        {
            "cht" => 1,
            "eng" => 2,
            "jpn" => 3,
            _ => 0
        };

        RefreshFontList();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        ShellRoot.Opacity = 1;
        ShellRoot.RenderTransform = null;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_forceInitialSetup && !_saved)
        {
            e.Cancel = true;
            ValidationText.Text = "Please save once on first run. Blank paths are allowed.";
        }

        base.OnClosing(e);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private async void OnPickMkvmergeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await PickFileAsync("Select mkvmerge");
        if (!string.IsNullOrWhiteSpace(path))
        {
            MkvmergePathTextBox.Text = path;
        }
    }

    private async void OnPickPyftsubsetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await PickFileAsync("Select pyftsubset");
        if (!string.IsNullOrWhiteSpace(path))
        {
            PyftsubsetPathTextBox.Text = path;
        }
    }

    private async void OnAddFontFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select font folders",
            AllowMultiple = true
        });

        foreach (var folder in folders)
        {
            var path = folder.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path) && !_fontDirectories.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _fontDirectories.Add(path);
            }
        }

        RefreshFontList();
    }

    private void OnRemoveFontFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var selected = FontDirectoriesListBox.SelectedItems?
            .OfType<string>()
            .Where(path => !path.StartsWith("("))
            .ToArray();

        if (selected is null || selected.Length == 0)
        {
            return;
        }

        foreach (var item in selected)
        {
            _fontDirectories.RemoveAll(path => string.Equals(path, item, StringComparison.OrdinalIgnoreCase));
        }

        RefreshFontList();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_forceInitialSetup)
        {
            ValidationText.Text = "Please save once on first run. Blank paths are allowed.";
            return;
        }

        Close(null);
    }

    private void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        var mkvmerge = NormalizePath(MkvmergePathTextBox.Text);
        var pyftsubset = NormalizePath(PyftsubsetPathTextBox.Text);

        if (!ValidateOptionalFile(mkvmerge, "mkvmerge") || !ValidateOptionalFile(pyftsubset, "pyftsubset"))
        {
            return;
        }

        var invalidFontFolder = _fontDirectories.FirstOrDefault(path => !Directory.Exists(path));
        if (invalidFontFolder is not null)
        {
            ValidationText.Text = $"Font folder does not exist: {invalidFontFolder}";
            return;
        }

        _saved = true;
        Close(new GuiSettings
        {
            HasCompletedOnboarding = true,
            MkvmergePath = mkvmerge,
            PyftsubsetPath = pyftsubset,
            FontDirectories = [.. _fontDirectories],
            UseSmartFontMatching = UseSmartMatchCheckBox.IsChecked != false,
            EnableFontSubsetting = EnableSubsetCheckBox.IsChecked != false,
            RemoveTemporaryFiles = RemoveTempCheckBox.IsChecked != false,
            SaveMuxLogBesideSource = SaveSidecarLogCheckBox.IsChecked == true,
            OverwriteSourceFiles = OverwriteSourceCheckBox.IsChecked == true,
            SubtitleLanguage = ((ComboBoxItem?)SubtitleLanguageComboBox.SelectedItem)?.Tag?.ToString() ?? "chi"
        });
    }

    private void RefreshFontList()
    {
        FontDirectoriesListBox.ItemsSource = _fontDirectories.Count == 0
            ? new[] { "(Using system font folders)" }
            : _fontDirectories.ToArray();
    }

    private bool ValidateOptionalFile(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
        {
            return true;
        }

        ValidationText.Text = $"{label} path does not exist: {path}";
        return false;
    }

    private async Task<string?> PickFileAsync(string title)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    private static string? NormalizePath(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim();
}
