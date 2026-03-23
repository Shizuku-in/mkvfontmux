using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using MkvFontMux.Gui.ViewModels;

namespace MkvFontMux.Gui.Views;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    public LogWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        SummaryText.Text = viewModel.StatusText;
        FolderText.Text = string.IsNullOrWhiteSpace(viewModel.LastFolder)
            ? string.Empty
            : $"Folder: {viewModel.LastFolder}";
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        ShellRoot.Opacity = 1;
        ShellRoot.RenderTransform = null;
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private async void OnSaveLogClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasLog)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save log",
            SuggestedFileName = "mkvfontmux-log.txt",
            DefaultExtension = "txt",
            ShowOverwritePrompt = true
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await File.WriteAllTextAsync(path, viewModel.LogText);
        SummaryText.Text = $"Log saved to {path}";
    }
}
