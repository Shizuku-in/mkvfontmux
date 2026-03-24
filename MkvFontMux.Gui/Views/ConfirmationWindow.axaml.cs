using Avalonia.Controls;
using Avalonia.Input;

namespace MkvFontMux.Gui.Views;

public partial class ConfirmationWindow : Window
{
    private bool _allowImmediateClose;
    private bool _isClosingAnimated;
    private bool _pendingResult;

    public ConfirmationWindow()
        : this(string.Empty, Array.Empty<string>())
    {
    }

    public ConfirmationWindow(string folder, IReadOnlyList<string> missingFonts)
    {
        InitializeComponent();
        Opened += OnOpened;
        FolderText.Text = $"Folder: {folder}";

        if (missingFonts.Count == 0)
        {
            TitleText.Text = "Fonts look complete";
            BodyText.Text = "No missing fonts were detected in the pre-scan. Do you want to continue with muxing?";
            MissingFontsListBox.ItemsSource = new[] { "No missing fonts detected." };
            FootnoteText.Text = "Processing will start immediately if you continue.";
            return;
        }

        TitleText.Text = "Missing fonts detected";
        BodyText.Text = "Some fonts could not be resolved. You can continue anyway or go back without processing.";
        MissingFontsListBox.ItemsSource = missingFonts;
        FootnoteText.Text = $"{missingFonts.Count} missing font(s) found.";
    }

    private async void OnBackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CloseWithAnimationAsync(false);
    }

    private async void OnContinueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CloseWithAnimationAsync(true);
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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowImmediateClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (!_isClosingAnimated)
        {
            _ = CloseWithAnimationAsync(false);
        }

        base.OnClosing(e);
    }

    private async Task CloseWithAnimationAsync(bool result)
    {
        if (_isClosingAnimated)
        {
            return;
        }

        _isClosingAnimated = true;
        _pendingResult = result;
        ShellRoot.Opacity = 0;
        await Task.Delay(220);

        _allowImmediateClose = true;
        Close(_pendingResult);
    }
}
