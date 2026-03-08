using System.Windows;
using MkvFontMux;
using Forms = System.Windows.Forms;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using System.Diagnostics;

namespace MkvFontMux.Gui;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cts;
    private readonly ObservableCollection<FileReportRow> _fileRows = [];
    private readonly List<MkvFileRunResult> _latestFileResults = [];
    private string _uiLanguage = "zh";
    private bool _uiInitialized;
    private static readonly ResourceManager Localizer = new("MkvFontMux.Gui.Resources.Strings", typeof(MainWindow).Assembly);
    private const string SettingsFolderName = "MkvFontMux";
    private const string SettingsFileName = "gui.settings.json";
    private static readonly JsonSerializerOptions SettingsJsonOptions = new() { WriteIndented = true };

    public MainWindow()
    {
        InitializeComponent();
        FileReportGrid.ItemsSource = _fileRows;
        var settings = LoadSettings();
        InitializeUiLanguage(settings?.UiLanguage);

        WorkDirTextBox.Text = settings?.WorkDirectory ?? string.Empty;
        MkvmergeTextBox.Text = settings?.MkvmergePath ?? string.Empty;
        FontDirsTextBox.Text = settings?.FontDirectories ?? string.Empty;
        PyftsubsetTextBox.Text = settings?.PyftsubsetPath ?? string.Empty;

        ForceMatchCheckBox.IsChecked = settings?.ForceMatch ?? false;
        DisableSubsetCheckBox.IsChecked = settings?.DisableSubset ?? false;
        SaveLogCheckBox.IsChecked = settings?.SaveLog ?? false;
        OverwriteCheckBox.IsChecked = settings?.Overwrite ?? false;
        RemoveTempCheckBox.IsChecked = settings?.RemoveTemp ?? true;
        OnlyMatchCheckBox.IsChecked = settings?.OnlyMatch ?? false;
        SubtitleLangTextBox.Text = string.IsNullOrWhiteSpace(settings?.SubtitleLanguage) ? "chi" : settings!.SubtitleLanguage!;

        _uiInitialized = true;
        HookSettingsChangeEvents();
        ApplyLocalization();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WorkDirTextBox.Text))
        {
            System.Windows.MessageBox.Show(this, T("msg.selectWorkDir"), T("msg.tip"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetRunningState(true);
        LogTextBox.Clear();
        _fileRows.Clear();
        _latestFileResults.Clear();
        _cts = new CancellationTokenSource();

        try
        {
            var service = new MuxService();
            var result = await service.RunAsync(new MuxRequest
            {
                WorkDirectory = WorkDirTextBox.Text.Trim(),
                UseConfigIniDefaults = false,
                MkvmergeBin = NullIfWhiteSpace(MkvmergeTextBox.Text),
                ForceMatch = ForceMatchCheckBox.IsChecked == true,
                FontDirectories = ParseFontDirectories(FontDirsTextBox.Text),
                DisableSubset = DisableSubsetCheckBox.IsChecked == true,
                SaveLog = SaveLogCheckBox.IsChecked == true,
                Overwrite = OverwriteCheckBox.IsChecked == true,
                RemoveTemp = RemoveTempCheckBox.IsChecked != false,
                OnlyPrintMatchFont = OnlyMatchCheckBox.IsChecked == true,
                SubtitleLanguage = string.IsNullOrWhiteSpace(SubtitleLangTextBox.Text) ? "chi" : SubtitleLangTextBox.Text.Trim(),
                PyftsubsetPath = NullIfWhiteSpace(PyftsubsetTextBox.Text),
                Log = AppendLog
            }, _cts.Token);

            StatusTextBlock.Text = result.Success
                ? string.Format(T("status.completed"), result.SucceededFiles, result.ProcessedFiles)
                : string.Format(T("status.completedWithError"), result.SucceededFiles, result.ProcessedFiles);

            _latestFileResults.AddRange(result.Files);
            RefreshFileReportRows();
        }
        catch (OperationCanceledException)
        {
            AppendLog(T("log.cancelled"));
            StatusTextBlock.Text = T("status.cancelled");
        }
        catch (Exception ex)
        {
            AppendLog($"{T("log.exception")}: {ex.Message}");
            StatusTextBlock.Text = T("status.failed");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetRunningState(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void BrowseWorkDir_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder(WorkDirTextBox.Text);
        if (path is not null)
        {
            WorkDirTextBox.Text = path;
        }
    }

    private void BrowseFontDir_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder(FontDirsTextBox.Text);
        if (path is null)
        {
            return;
        }

        FontDirsTextBox.Text = string.IsNullOrWhiteSpace(FontDirsTextBox.Text)
            ? path
            : $"{FontDirsTextBox.Text};{path}";
    }

    private void BrowseMkvmerge_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("dialog.pickMkvmerge"),
            Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            MkvmergeTextBox.Text = dialog.FileName;
        }
    }

    private void BrowsePyftsubset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("dialog.pickPyftsubset"),
            Filter = "Executable (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            PyftsubsetTextBox.Text = dialog.FileName;
        }
    }

    private void PathTextBox_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void PathTextBox_PreviewDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            textBox.BorderBrush = System.Windows.SystemColors.HighlightBrush;
            textBox.BorderThickness = new Thickness(2);
        }

        PathTextBox_PreviewDragOver(sender, e);
    }

    private void PathTextBox_PreviewDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        ResetDropHighlight(sender as System.Windows.Controls.TextBox);
    }

    private void WorkDirTextBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        ResetDropHighlight(sender as System.Windows.Controls.TextBox);

        var directory = TryGetDroppedDirectory(e);
        if (directory is null)
        {
            return;
        }

        WorkDirTextBox.Text = directory;
    }

    private void FontDirsTextBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        ResetDropHighlight(sender as System.Windows.Controls.TextBox);

        var directory = TryGetDroppedDirectory(e);
        if (directory is null)
        {
            return;
        }

        FontDirsTextBox.Text = string.IsNullOrWhiteSpace(FontDirsTextBox.Text)
            ? directory
            : $"{FontDirsTextBox.Text};{directory}";
    }

    private void MkvmergeTextBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        ResetDropHighlight(sender as System.Windows.Controls.TextBox);

        var path = TryResolveDroppedExecutablePath(e, "mkvmerge.exe");
        if (path is null)
        {
            return;
        }

        MkvmergeTextBox.Text = path;
    }

    private void PyftsubsetTextBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        ResetDropHighlight(sender as System.Windows.Controls.TextBox);

        var path = TryResolveDroppedExecutablePath(e, "pyftsubset.exe", "pyftsubset.bat", "pyftsubset.cmd");
        if (path is null)
        {
            return;
        }

        PyftsubsetTextBox.Text = path;
    }

    private void ExportLogButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = T("dialog.exportLog"),
            Filter = "Text file (*.txt)|*.txt|Log file (*.log)|*.log|All files (*.*)|*.*",
            FileName = $"MkvFontMux-GUI-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, LogTextBox.Text);
        AppendLog($"{T("log.exported")}: {dialog.FileName}");
    }

    private void ExportMissingFontsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestFileResults.Count == 0)
        {
            System.Windows.MessageBox.Show(this, T("msg.noResultToExport"), T("msg.tip"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var uniqueMissingFonts = _latestFileResults
            .Where(file => file.MissingFonts.Count > 0)
            .SelectMany(file => file.MissingFonts)
            .Where(font => !string.IsNullOrWhiteSpace(font))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(font => font, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (uniqueMissingFonts.Length == 0)
        {
            System.Windows.MessageBox.Show(this, T("msg.noMissingFonts"), T("msg.tip"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = T("dialog.exportMissingCsv"),
            Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"MkvFontMux-MissingFonts-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("MissingFont");
        foreach (var fontName in uniqueMissingFonts)
        {
            builder.AppendLine(EscapeCsv(fontName));
        }

        File.WriteAllText(dialog.FileName, builder.ToString(), Encoding.UTF8);
        AppendLog(string.Format(T("log.exportedMissingCsv"), uniqueMissingFonts.Length, dialog.FileName));
    }

    private void LanguageZhMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ChangeUiLanguage("zh");
    }

    private void LanguageEnMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ChangeUiLanguage("en");
    }

    private void LanguageZhHantMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ChangeUiLanguage("zh-Hant");
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var repositoryUrl = "https://github.com/Shizuku-in/mkvfontmux";
        var text = $"{T("about.summary")}\n\nGitHub: {repositoryUrl}\n\n{T("about.openRepoPrompt")}";
        var result = System.Windows.MessageBox.Show(this, text, T("about.title"), MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = repositoryUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppendLog($"{T("log.exception")}: {ex.Message}");
        }
    }

    private void ChangeUiLanguage(string languageTag)
    {
        if (!_uiInitialized)
        {
            return;
        }

        SetUiLanguage(languageTag);

        SaveCurrentSettings();

        ApplyLocalization();
        RefreshFileReportRows();
    }

    private void HookSettingsChangeEvents()
    {
        WorkDirTextBox.TextChanged += AnySettingChanged;
        FontDirsTextBox.TextChanged += AnySettingChanged;
        MkvmergeTextBox.TextChanged += AnySettingChanged;
        PyftsubsetTextBox.TextChanged += AnySettingChanged;
        SubtitleLangTextBox.TextChanged += AnySettingChanged;

        ForceMatchCheckBox.Checked += AnySettingChanged;
        ForceMatchCheckBox.Unchecked += AnySettingChanged;
        DisableSubsetCheckBox.Checked += AnySettingChanged;
        DisableSubsetCheckBox.Unchecked += AnySettingChanged;
        SaveLogCheckBox.Checked += AnySettingChanged;
        SaveLogCheckBox.Unchecked += AnySettingChanged;
        OverwriteCheckBox.Checked += AnySettingChanged;
        OverwriteCheckBox.Unchecked += AnySettingChanged;
        RemoveTempCheckBox.Checked += AnySettingChanged;
        RemoveTempCheckBox.Unchecked += AnySettingChanged;
        OnlyMatchCheckBox.Checked += AnySettingChanged;
        OnlyMatchCheckBox.Unchecked += AnySettingChanged;
    }

    private void AnySettingChanged(object? sender, RoutedEventArgs e)
    {
        if (!_uiInitialized)
        {
            return;
        }

        SaveCurrentSettings();
    }

    private void AnySettingChanged(object? sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_uiInitialized)
        {
            return;
        }

        SaveCurrentSettings();
    }

    private string? PickFolder(string? initialPath)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            UseDescriptionForTitle = true,
            Description = T("dialog.pickFolder"),
            InitialDirectory = string.IsNullOrWhiteSpace(initialPath) ? Environment.CurrentDirectory : initialPath
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void SetRunningState(bool isRunning)
    {
        StartButton.IsEnabled = !isRunning;
        CancelButton.IsEnabled = isRunning;
        RunProgressBar.IsIndeterminate = isRunning;
        StatusTextBlock.Text = isRunning ? T("status.running") : StatusTextBlock.Text;
    }

    private void AppendLog(string text)
    {
        Dispatcher.Invoke(() =>
        {
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        });
    }

    private static IReadOnlyList<string>? ParseFontDirectories(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? NullIfWhiteSpace(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static string? TryGetDroppedDirectory(System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] droppedPaths || droppedPaths.Length == 0)
        {
            return null;
        }

        foreach (var path in droppedPaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string? TryResolveDroppedExecutablePath(System.Windows.DragEventArgs e, params string[] expectedFileNames)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] droppedPaths || droppedPaths.Length == 0)
        {
            return null;
        }

        foreach (var path in droppedPaths)
        {
            if (File.Exists(path) && expectedFileNames.Any(name => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)))
            {
                return path;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var fileName in expectedFileNames)
            {
                var candidate = Path.Combine(path, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void ResetDropHighlight(System.Windows.Controls.TextBox? textBox)
    {
        if (textBox is null)
        {
            return;
        }

        textBox.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        textBox.ClearValue(System.Windows.Controls.Control.BorderThicknessProperty);
    }

    private void ApplyLocalization()
    {
        Title = T("window.title");
        TitleTextBlock.Text = T("title.main");
        LanguageMenuItem.Header = T("menu.language");
        AboutMenuItem.Header = T("menu.about");
        WorkDirLabel.Text = T("label.workDir");
        FontDirsLabel.Text = T("label.fontDirs");
        MkvmergeLabel.Text = T("label.mkvmergePath");
        PyftsubsetLabel.Text = T("label.pyftsubsetPath");
        SubtitleLanguageLabel.Text = T("label.subtitleLangCode");

        BrowseWorkDirButton.Content = T("btn.browse");
        BrowseFontDirButton.Content = T("btn.browse");
        BrowseMkvmergeButton.Content = T("btn.browse");
        BrowsePyftsubsetButton.Content = T("btn.browse");

        ForceMatchCheckBox.Content = T("check.forceMatch");
        DisableSubsetCheckBox.Content = T("check.disableSubset");
        SaveLogCheckBox.Content = T("check.saveLog");
        OverwriteCheckBox.Content = T("check.overwrite");
        RemoveTempCheckBox.Content = T("check.removeTemp");
        OnlyMatchCheckBox.Content = T("check.onlyMatch");

        StartButton.Content = T("btn.start");
        CancelButton.Content = T("btn.cancel");
        ExportLogButton.Content = T("btn.exportLog");
        ExportMissingFontsButton.Content = T("btn.exportMissingCsv");

        LanguageZhMenuItem.Header = T("lang.zh");
        LanguageEnMenuItem.Header = T("lang.en");
        LanguageZhHantMenuItem.Header = T("lang.zhHant");

        if (FileReportGrid.Columns.Count >= 5)
        {
            FileReportGrid.Columns[0].Header = "MKV";
            FileReportGrid.Columns[1].Header = T("col.status");
            FileReportGrid.Columns[2].Header = T("col.missingCount");
            FileReportGrid.Columns[3].Header = T("col.missingFonts");
            FileReportGrid.Columns[4].Header = T("col.message");
        }

        if (!RunProgressBar.IsIndeterminate)
        {
            StatusTextBlock.Text = T("status.ready");
        }
    }

    private void RefreshFileReportRows()
    {
        _fileRows.Clear();

        foreach (var file in _latestFileResults)
        {
            _fileRows.Add(new FileReportRow
            {
                FileName = file.FileName,
                Status = file.Success ? T("row.success") : (file.Skipped ? T("row.skipped") : T("row.failed")),
                MissingCount = file.MissingFonts.Count,
                MissingFonts = file.MissingFonts.Count == 0 ? "-" : string.Join(", ", file.MissingFonts),
                Message = string.IsNullOrWhiteSpace(file.Message) ? "-" : file.Message
            });
        }
    }

    private string T(string key)
    {
        var text = Localizer.GetString(key, GetCulture());
        return string.IsNullOrWhiteSpace(text) ? key : text;
    }

    private CultureInfo GetCulture()
    {
        return _uiLanguage switch
        {
            "en" => new CultureInfo("en"),
            "zh-Hant" => new CultureInfo("zh-Hant"),
            _ => new CultureInfo("zh-Hans")
        };
    }

    private void InitializeUiLanguage(string? preferredLanguage)
    {
        if (IsSupportedLanguageTag(preferredLanguage))
        {
            SetUiLanguage(preferredLanguage!);
            return;
        }

        var uiCulture = CultureInfo.CurrentUICulture;
        var languageTag = "zh";

        if (uiCulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            languageTag = "en";
        }
        else if (string.Equals(uiCulture.Name, "zh-Hant", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(uiCulture.Name, "zh-TW", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(uiCulture.Name, "zh-HK", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(uiCulture.Name, "zh-MO", StringComparison.OrdinalIgnoreCase))
        {
            languageTag = "zh-Hant";
        }

        SetUiLanguage(languageTag);
    }

    private void SetUiLanguage(string languageTag)
    {
        _uiLanguage = languageTag switch
        {
            "en" => "en",
            "zh-Hant" => "zh-Hant",
            _ => "zh"
        };

        UpdateLanguageMenuChecks();
    }

    private void UpdateLanguageMenuChecks()
    {
        LanguageZhMenuItem.IsChecked = string.Equals(_uiLanguage, "zh", StringComparison.OrdinalIgnoreCase);
        LanguageEnMenuItem.IsChecked = string.Equals(_uiLanguage, "en", StringComparison.OrdinalIgnoreCase);
        LanguageZhHantMenuItem.IsChecked = string.Equals(_uiLanguage, "zh-Hant", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedLanguageTag(string? languageTag)
    {
        return languageTag is "zh" or "en" or "zh-Hant";
    }

    private static GuiSettings? LoadSettings()
    {
        try
        {
            var settingsFile = GetSettingsFilePath();
            if (!File.Exists(settingsFile))
            {
                return null;
            }

            var json = File.ReadAllText(settingsFile);
            return JsonSerializer.Deserialize<GuiSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    private void SaveCurrentSettings()
    {
        SaveSettings(new GuiSettings
        {
            UiLanguage = _uiLanguage,
            WorkDirectory = WorkDirTextBox.Text,
            FontDirectories = FontDirsTextBox.Text,
            MkvmergePath = MkvmergeTextBox.Text,
            PyftsubsetPath = PyftsubsetTextBox.Text,
            SubtitleLanguage = SubtitleLangTextBox.Text,
            ForceMatch = ForceMatchCheckBox.IsChecked == true,
            DisableSubset = DisableSubsetCheckBox.IsChecked == true,
            SaveLog = SaveLogCheckBox.IsChecked == true,
            Overwrite = OverwriteCheckBox.IsChecked == true,
            RemoveTemp = RemoveTempCheckBox.IsChecked != false,
            OnlyMatch = OnlyMatchCheckBox.IsChecked == true
        });
    }

    private static void SaveSettings(GuiSettings settings)
    {
        try
        {
            var settingsFile = GetSettingsFilePath();
            var settingsDir = Path.GetDirectoryName(settingsFile)!;
            Directory.CreateDirectory(settingsDir);

            var json = JsonSerializer.Serialize(settings, SettingsJsonOptions);

            File.WriteAllText(settingsFile, json);
        }
        catch
        {
        }
    }

    private static string GetSettingsFilePath()
    {
        var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataDir, SettingsFolderName, SettingsFileName);
    }

    private sealed class FileReportRow
    {
        public string FileName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public int MissingCount { get; init; }
        public string MissingFonts { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    private sealed class GuiSettings
    {
        public string? UiLanguage { get; init; }
        public string? WorkDirectory { get; init; }
        public string? FontDirectories { get; init; }
        public string? MkvmergePath { get; init; }
        public string? PyftsubsetPath { get; init; }
        public string? SubtitleLanguage { get; init; }
        public bool? ForceMatch { get; init; }
        public bool? DisableSubset { get; init; }
        public bool? SaveLog { get; init; }
        public bool? Overwrite { get; init; }
        public bool? RemoveTemp { get; init; }
        public bool? OnlyMatch { get; init; }
    }
}