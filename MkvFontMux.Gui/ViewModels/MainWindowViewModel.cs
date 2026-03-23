using MkvFontMux.Gui.Models;
using MkvFontMux.Gui.Services;

namespace MkvFontMux.Gui.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private GuiSettings _settings;
    private bool _isBusy;
    private string _heroTitle = "MkvFontMux";
    private string _heroSubtitle = "Drag folder here!";
    private string _statusText = "Ready when you are.";
    private string _logText = string.Empty;
    private string? _lastFolder;

    public MainWindowViewModel()
        : this(new SettingsService())
    {
    }

    public MainWindowViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = _settingsService.Load();
    }

    public GuiSettings Settings
    {
        get => _settings;
        private set
        {
            if (SetProperty(ref _settings, value))
            {
                RaisePropertyChanged(nameof(HasCompletedOnboarding));
            }
        }
    }

    public bool HasCompletedOnboarding => Settings.HasCompletedOnboarding;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string HeroTitle
    {
        get => _heroTitle;
        set => SetProperty(ref _heroTitle, value);
    }

    public string HeroSubtitle
    {
        get => _heroSubtitle;
        set => SetProperty(ref _heroSubtitle, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string LogText
    {
        get => _logText;
        set
        {
            if (SetProperty(ref _logText, value))
            {
                RaisePropertyChanged(nameof(HasLog));
            }
        }
    }

    public bool HasLog => !string.IsNullOrWhiteSpace(LogText);

    public string? LastFolder
    {
        get => _lastFolder;
        set => SetProperty(ref _lastFolder, value);
    }

    public void SaveSettings(GuiSettings settings)
    {
        settings.HasCompletedOnboarding = true;
        Settings = settings;
        _settingsService.Save(settings);
        StatusText = "Settings saved. Drop a folder to begin.";
    }

    public void ResetHero()
    {
        HeroTitle = "MkvFontMux";
        HeroSubtitle = IsBusy ? "Working on your folder..." : "Drag folder here!";
    }

    public void StartRun(string folder)
    {
        IsBusy = true;
        LastFolder = folder;
        HeroTitle = "Preparing your mux";
        HeroSubtitle = Path.GetFileName(folder);
        StatusText = "Scanning subtitles and fonts...";
        LogText = string.Empty;
    }

    public void CompleteRun(bool success)
    {
        IsBusy = false;
        HeroTitle = success ? "Finished" : "Finished with issues";
        HeroSubtitle = LastFolder is null ? "Drag folder here!" : Path.GetFileName(LastFolder);
        StatusText = success ? "You can review or save the log below." : "Review the log before trying again.";
    }

    public void AppendLog(string line)
    {
        LogText = string.IsNullOrWhiteSpace(LogText)
            ? line
            : $"{LogText}{Environment.NewLine}{line}";
    }
}
