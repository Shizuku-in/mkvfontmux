using Avalonia.Controls;

namespace MkvFontMux.Gui.Views;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow()
        : this(string.Empty, Array.Empty<string>())
    {
    }

    public ConfirmationWindow(string folder, IReadOnlyList<string> missingFonts)
    {
        InitializeComponent();
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

    private void OnBackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnContinueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }
}
