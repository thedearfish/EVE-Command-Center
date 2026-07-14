using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using EveCommandCenter.App.Shell;
using EveCommandCenter.App.Startup;
using EveCommandCenter.Application.Discovery;
using EveCommandCenter.Infrastructure.Settings;
using EveCommandCenter.Presentation;
using EveCommandCenter.Windows.Activation;
using EveCommandCenter.Windows.Discovery;

namespace EveCommandCenter.App;

public partial class App : System.Windows.Application
{
    private static readonly object LogSync = new();
    private static readonly string LogPath = CreateLogPath();

    private ApplicationLifecycleController? lifecycle;
    private FloatingPreviewManager? previewManager;
    private GlobalHotkeyService? hotkeyService;
    private MainWindowViewModel? viewModel;
    private JsonSettingsStore<AppSettings>? settingsStore;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        WriteLog("Application object created.");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            WriteLog($"Startup begin. Base directory: {AppContext.BaseDirectory}");
            WriteLog($"OS: {Environment.OSVersion}; 64-bit process: {Environment.Is64BitProcess}");

            base.OnStartup(e);
            WriteLog("WPF base startup completed.");

            settingsStore = new JsonSettingsStore<AppSettings>(AppDataPathProvider.GetSettingsPath());
            AppSettings settings = LoadSettings();

            var source = new Win32WindowSnapshotSource();
            var classifier = new EveWindowClassifier();
            viewModel = new MainWindowViewModel(source, classifier, CreatePresentationSettings(settings));
            viewModel.SettingsSaveRequested += OnSettingsSaveRequested;
            viewModel.SettingsPersistRequested += OnSettingsPersistRequested;
            WriteLog("Settings view model and EVE monitor created.");

            var activationService = new Win32WindowActivationService();
            previewManager = new FloatingPreviewManager(viewModel, activationService);

            var navigation = new ClientNavigationController(
                viewModel,
                activationService,
                new Win32ForegroundWindowSource());

            hotkeyService = new GlobalHotkeyService(
                () => _ = navigation.NextAsync(),
                () => _ = navigation.PreviousAsync(),
                previewManager.ToggleVisibility);

            ApplyHotkeys(reportSuccess: false);

            var settingsWindow = new MainWindow(viewModel);
            WriteLog("Settings window created.");

            MainWindow = settingsWindow;
            lifecycle = new ApplicationLifecycleController(
                settingsWindow,
                new FirstRunStateStore(),
                previewManager.ToggleVisibility,
                exitCode => Shutdown(exitCode));

            bool forceSettingsWindow = ShouldShowSettingsWindow(e.Args);
            lifecycle.Start(forceSettingsWindow);

            WriteLog(forceSettingsWindow
                ? "Application started in tray mode with settings explicitly requested."
                : "Application started in tray mode.");
        }
        catch (Exception exception)
        {
            ReportFatalStartupError(exception);

            if (lifecycle is not null)
            {
                lifecycle.RequestExit(-1);
            }
            else
            {
                Shutdown(-1);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.SettingsSaveRequested -= OnSettingsSaveRequested;
            viewModel.SettingsPersistRequested -= OnSettingsPersistRequested;
            TrySaveSettings();
        }

        hotkeyService?.Dispose();
        hotkeyService = null;

        previewManager?.Dispose();
        previewManager = null;

        lifecycle?.Dispose();
        lifecycle = null;

        WriteLog($"Application exit. Code: {e.ApplicationExitCode}.");
        base.OnExit(e);
    }

    private void OnSettingsSaveRequested(object? sender, EventArgs e)
    {
        if (viewModel is null || settingsStore is null)
        {
            return;
        }

        try
        {
            AppSettings settings = CreatePersistedSettings(viewModel.CreateSettingsSnapshot());
            settingsStore.SaveAsync(settings).GetAwaiter().GetResult();
            ApplyHotkeys(reportSuccess: true);
        }
        catch (Exception exception)
        {
            WriteLog("Settings save failed.", exception);
            viewModel.SetSettingsResult($"Could not save settings: {exception.Message}");
        }
    }

    private void OnSettingsPersistRequested(object? sender, EventArgs e) => TrySaveSettings();

    private AppSettings LoadSettings()
    {
        try
        {
            return settingsStore!.LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            WriteLog("Settings load failed. Defaults will be used.", exception);
            return new AppSettings();
        }
    }

    private void ApplyHotkeys(bool reportSuccess)
    {
        if (viewModel is null || hotkeyService is null)
        {
            return;
        }

        string? error = hotkeyService.Apply(new HotkeyBindings(
            viewModel.NextCharacterHotkey,
            viewModel.PreviousCharacterHotkey,
            viewModel.TogglePreviewsHotkey));

        if (error is not null)
        {
            viewModel.SetSettingsResult($"Hotkey error: {error}");
        }
        else if (reportSuccess)
        {
            viewModel.SetSettingsResult("Settings saved and global hotkeys registered.");
        }
    }

    private void TrySaveSettings()
    {
        if (viewModel is null || settingsStore is null)
        {
            return;
        }

        try
        {
            settingsStore
                .SaveAsync(CreatePersistedSettings(viewModel.CreateSettingsSnapshot()))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            WriteLog("Automatic settings save failed.", exception);
        }
    }

    private static SettingsSnapshot CreatePresentationSettings(AppSettings settings) =>
        new(
            settings.Preview.AutoCreate,
            settings.Preview.AlwaysOnTop,
            settings.Preview.ShowHeader,
            Math.Clamp(settings.Preview.ThumbnailWidth, 220, 1920),
            Math.Clamp(settings.Preview.ThumbnailHeight, 140, 1080),
            Math.Clamp(settings.Preview.Opacity, 0.35, 1.0),
            settings.Hotkeys.NextCharacter,
            settings.Hotkeys.PreviousCharacter,
            settings.Hotkeys.TogglePreviews,
            (settings.Preview.Characters ?? Array.Empty<CharacterPreviewSettings>())
                .Where(character => !string.IsNullOrWhiteSpace(character.CharacterName))
                .Select(character => new CharacterPreviewProfileSnapshot(
                    character.CharacterName,
                    character.CustomLabel,
                    ParseContentMode(character.ContentMode),
                    character.Left,
                    character.Top,
                    character.Width,
                    character.Height))
                .ToArray());

    private static PreviewContentMode ParseContentMode(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out PreviewContentMode mode)
            ? mode
            : PreviewContentMode.Standard;

    private static AppSettings CreatePersistedSettings(SettingsSnapshot settings) =>
        new()
        {
            General = new GeneralSettings
            {
                StartMinimized = true,
                MinimizeToTray = true,
            },
            Preview = new PreviewSettings
            {
                AutoCreate = settings.AutoCreatePreviews,
                AlwaysOnTop = settings.AlwaysOnTop,
                ShowHeader = settings.ShowPreviewHeader,
                ThumbnailWidth = settings.PreviewWidth,
                ThumbnailHeight = settings.PreviewHeight,
                Opacity = settings.PreviewOpacity,
                Characters = settings.CharacterProfiles
                    .Select(character => new CharacterPreviewSettings
                    {
                        CharacterName = character.CharacterName,
                        CustomLabel = character.CustomLabel,
                        ContentMode = character.ContentMode.ToString(),
                        Left = character.Left,
                        Top = character.Top,
                        Width = character.Width,
                        Height = character.Height,
                    })
                    .ToArray(),
            },
            Hotkeys = new HotkeySettings
            {
                NextCharacter = settings.NextCharacterHotkey,
                PreviousCharacter = settings.PreviousCharacterHotkey,
                TogglePreviews = settings.TogglePreviewsHotkey,
            },
        };

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog("Unhandled dispatcher exception.", e.Exception);
        ShowError("EVE Command Center encountered an unexpected error.", e.Exception);
        e.Handled = true;

        if (lifecycle is not null)
        {
            lifecycle.RequestExit(-2);
        }
        else
        {
            Shutdown(-2);
        }
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        WriteLog($"Unhandled AppDomain exception. Terminating: {e.IsTerminating}.", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteLog("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    private static bool ShouldShowSettingsWindow(IEnumerable<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (argument.Equals("--settings", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--show-settings", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("/settings", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReportFatalStartupError(Exception exception)
    {
        WriteLog("Fatal startup error.", exception);
        ShowError("EVE Command Center could not start.", exception);
    }

    private static void ShowError(string title, Exception exception)
    {
        string message = $"{exception.GetType().FullName}: {exception.Message}\n\n" +
                         $"Diagnostic log:\n{LogPath}";

        try
        {
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Logging is the final fallback if WPF cannot display a dialog.
        }
    }

    private static string CreateLogPath()
    {
        string[] candidateDirectories =
        [
            Path.Combine(AppContext.BaseDirectory, "logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EVE Command Center", "logs"),
            Path.Combine(Path.GetTempPath(), "EVE Command Center", "logs"),
        ];

        foreach (string directory in candidateDirectories)
        {
            try
            {
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, "startup.log");
            }
            catch
            {
                // Try the next writable location.
            }
        }

        return Path.Combine(Path.GetTempPath(), "eve-command-center-startup.log");
    }

    private static void WriteLog(string message, Exception? exception = null)
    {
        try
        {
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [")
                .Append(Environment.ProcessId)
                .Append("] ")
                .AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (LogSync)
            {
                File.AppendAllText(LogPath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Startup diagnostics must never cause a secondary crash.
        }
    }
}
