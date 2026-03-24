using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MkvFontMux;
using MkvFontMux.Gui.Models;
using MkvFontMux.Gui.ViewModels;

namespace MkvFontMux.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private async void OnOpened(object? sender, EventArgs e)
    {
        LockWindowWidth();
        PlayEntranceAnimations();
        ViewModel.ResetHero();
        if (!ViewModel.HasCompletedOnboarding)
        {
            await ShowSettingsAsync(forceInitialSetup: true);
        }
    }

    private async void OnOpenSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ShowSettingsAsync(forceInitialSetup: false);
    }

    private async Task ShowSettingsAsync(bool forceInitialSetup)
    {
        var window = new SettingsWindow(ViewModel.Settings, forceInitialSetup);
        var result = await window.ShowDialog<GuiSettings?>(this);
        if (result is not null)
        {
            ViewModel.SaveSettings(result);
            ViewModel.ResetHero();
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            e.DragEffects = DragDropEffects.None;
            HeroCardRoot.Classes.Set("drag-active", false);
            return;
        }

        var hasFolder = e.Data.GetFiles()?.Any(file => file?.TryGetLocalPath() is { } path && Directory.Exists(path)) == true;
        e.DragEffects = hasFolder ? DragDropEffects.Copy : DragDropEffects.None;
        HeroCardRoot.Classes.Set("drag-active", hasFolder);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        HeroCardRoot.Classes.Set("drag-active", false);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        HeroCardRoot.Classes.Set("drag-active", false);

        if (ViewModel.IsBusy)
        {
            return;
        }

        var folder = e.Data.GetFiles()?
            .Select(item => item?.TryGetLocalPath())
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));

        if (string.IsNullOrWhiteSpace(folder))
        {
            ViewModel.StatusText = "Please drop a real folder path.";
            return;
        }

        if (!ViewModel.HasCompletedOnboarding)
        {
            await ShowSettingsAsync(forceInitialSetup: true);
            if (!ViewModel.HasCompletedOnboarding)
            {
                return;
            }
        }

        await ProcessFolderAsync(folder);
    }

    private async Task ProcessFolderAsync(string folder)
    {
        ViewModel.StartRun(folder);
        var settings = ViewModel.Settings;

        try
        {
            var scanResult = await RunMuxAsync(folder, settings, onlyPrintMatchFont: true);
            foreach (var line in scanResult.Messages)
            {
                ViewModel.AppendLog(line);
            }

            if (!scanResult.Success && scanResult.Files.Count == 0)
            {
                ViewModel.CompleteRun(success: false);
                await ShowLogWindowAsync();
                return;
            }

            var missingFonts = scanResult.Files
                .SelectMany(file => file.MissingFonts)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var confirmWindow = new ConfirmationWindow(folder, missingFonts);
            var shouldContinue = await confirmWindow.ShowDialog<bool>(this);
            if (!shouldContinue)
            {
                ViewModel.IsBusy = false;
                ViewModel.HeroTitle = "Cancelled";
                ViewModel.HeroSubtitle = Path.GetFileName(folder);
                ViewModel.StatusText = "Nothing was changed.";
                return;
            }

            ViewModel.StatusText = "Muxing files...";
            var processResult = await RunMuxAsync(folder, settings, onlyPrintMatchFont: false);
            foreach (var line in processResult.Messages)
            {
                ViewModel.AppendLog(line);
            }

            var summary = processResult.Success
                ? $"Completed: {processResult.SucceededFiles}/{processResult.ProcessedFiles} files succeeded."
                : $"Completed with issues: {processResult.SucceededFiles}/{processResult.ProcessedFiles} files succeeded.";

            ViewModel.AppendLog(summary);
            ViewModel.CompleteRun(processResult.Success);
            await ShowLogWindowAsync();
        }
        catch (Exception ex)
        {
            ViewModel.AppendLog($"Unexpected error: {ex.Message}");
            ViewModel.CompleteRun(success: false);
            await ShowLogWindowAsync();
        }
    }

    private async Task<MuxRunResult> RunMuxAsync(string folder, GuiSettings settings, bool onlyPrintMatchFont)
    {
        var service = new MuxService();
        return await service.RunAsync(new MuxRequest
        {
            WorkDirectory = folder,
            UseConfigIniDefaults = false,
            MkvmergeBin = EmptyToNull(settings.MkvmergePath),
            PyftsubsetPath = EmptyToNull(settings.PyftsubsetPath),
            FontDirectories = settings.FontDirectories.Count == 0 ? null : settings.FontDirectories,
            ForceMatch = !settings.UseSmartFontMatching,
            DisableSubset = !settings.EnableFontSubsetting,
            RemoveTemp = settings.RemoveTemporaryFiles,
            SaveLog = !onlyPrintMatchFont && settings.SaveMuxLogBesideSource,
            Overwrite = settings.OverwriteSourceFiles,
            SubtitleLanguage = settings.SubtitleLanguage,
            OnlyPrintMatchFont = onlyPrintMatchFont,
            Log = _ => { }
        });
    }

    private async Task ShowLogWindowAsync()
    {
        if (!ViewModel.HasLog)
        {
            return;
        }

        var logWindow = new LogWindow(ViewModel);
        await logWindow.ShowDialog(this);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void PlayEntranceAnimations()
    {
        HeaderContent.Opacity = 1;
        HeaderContent.RenderTransform = null;

        CloseButton.Opacity = 1;
        CloseButton.RenderTransform = null;

        HeroCardRoot.Opacity = 1;
        HeroCardRoot.RenderTransform = null;

        SettingsButtonRoot.Opacity = 1;
        SettingsButtonRoot.RenderTransform = null;
    }

    private void LockWindowWidth()
    {
        var width = Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        MinWidth = width;
        MaxWidth = width;
    }
}
