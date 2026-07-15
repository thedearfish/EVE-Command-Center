using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using EveCommandCenter.App.Diagnostics;
using EveCommandCenter.App.Shell;
using EveCommandCenter.App.Startup;
using EveCommandCenter.Application.Diagnostics;
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
    private SerializedSettingsWriter? settingsWriter;
    private UiDispatcherWatchdog? uiWatchdog;

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

            InitializeRuntimeDiagnostics();
            AppLog.Information(
                "Application",
                $"Startup begin; baseDirectory={AppContext.BaseDirectory}; " +
                $"os={Environment.OSVersion}; 64Bit={Environment.Is64BitProcess}.");

            uiWatchdog = new UiDispatcherWatchdog(Dispatcher);
            uiWatchdog.Start();

            settingsStore = new JsonSettingsStore<AppSettings>(AppDataPathProvider.GetSettingsPath());
            settingsWriter = new SerializedSettingsWriter(settingsStore);
            AppSettings settings = LoadSettings();

            var source = new Win32WindowSnapshotSource();
            var classifier = new EveWindowClassifier();
            viewModel = new MainWindowViewModel(source, classifier, CreatePresentationSettings(settings));
            viewModel.SettingsSaveRequested += OnSettingsSaveRequested;
            viewModel.SettingsPersistRequested += OnSettingsPersistRequested;
            WriteLog("Settings view model and EVE monitor created.");
            AppLog.Information("Application", "Settings view model and EVE client monitor created.");

            var activationService = new Win32WindowActivationService();
            previewManager = new FloatingPreviewManager(viewModel, activationService);

            var navigation = new ClientNavigationController(
                viewModel,
                activationService,
                new Win32ForegroundWindowSource());

            hotkeyService = new GlobalHotkeyService(
                () => _ = navigation.NextAsync(),
                () => _ = navigation.PreviousAsync(),
                previewManager.ToggleVisibility,
                characterName => _ = navigation.ActivateCharacterAsync(characterName));

            ApplyHotkeys(reportSuccess: false);

            var settingsWindow = new MainWindow(viewModel);
            WriteLog("Settings window created.");
            AppLog.Information("Application", "Settings window created.");

            MainWindow = settingsWindow;
            lifecycle = new ApplicationLifecycleController(
                settingsWindow,
                new FirstRunStateStore(),
                previewManager.ToggleVisibility,
                exitCode => Shutdown(exitCode));

            bool forceSettingsWindow = ShouldShowSettingsWindow(e.Args);
            lifecycle.Start(forceSettingsWindow);

            string startupMessage = forceSettingsWindow
                ? "Application started in tray mode with settings explicitly requested."
                : "Application started in tray mode.";
            WriteLog(startupMessage);
            AppLog.Information("Application", startupMessage);
            AppLog.SetCurrentOperation("idle");
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
        AppLog.SetCurrentOperation("application shutdown");
        AppLog.Information("Application", $"Application exit started. Code: {e.ApplicationExitCode}.");

        if (viewModel is not null)
        {
            viewModel.SettingsSaveRequested -= OnSettingsSaveRequested;
            viewModel.SettingsPersistRequested -= OnSettingsPersistRequested;
        }

        previewManager?.Dispose();
        previewManager = null;

        SaveFinalSettings();

        viewModel?.Dispose();
        viewModel = null;

        hotkeyService?.Dispose();
        hotkeyService = null;

        lifecycle?.Dispose();
        lifecycle = null;

        settingsWriter?.Dispose();
        settingsWriter = null;

        uiWatchdog?.Dispose();
        uiWatchdog = null;

        WriteLog($"Application exit. Code: {e.ApplicationExitCode}.");
        AppLog.Information("Application", $"Application exit completed. Code: {e.ApplicationExitCode}.");
        AppLog.Shutdown();
        base.OnExit(e);
    }

    private async void OnSettingsSaveRequested(object? sender, EventArgs e)
    {
        if (viewModel is null || settingsWriter is null)
        {
            return;
        }

        AppSettings settings = CreatePersistedSettings(viewModel.CreateSettingsSnapshot());
        bool saved = await settingsWriter.SaveAsync(settings, "manual settings save");

        if (!saved)
        {
            viewModel.SetSettingsResult("Could not save settings. See runtime.log for details.");
            return;
        }

        ApplyHotkeys(reportSuccess: true);
    }

    private async void OnSettingsPersistRequested(object? sender, EventArgs e)
    {
        if (viewModel is null || settingsWriter is null)
        {
            return;
        }

        AppSettings settings = CreatePersistedSettings(viewModel.CreateSettingsSnapshot());
        _ = await settingsWriter.SaveAsync(settings, "automatic character layout/profile update");
    }

    private AppSettings LoadSettings()
    {
        try
        {
            string settingsPath = AppDataPathProvider.GetSettingsPath();
            bool interruptedSavePresent = File.Exists(settingsPath + ".tmp");
            AppLog.Information(
                "Settings",
                $"Loading settings from {settingsPath}; interruptedSavePresent={interruptedSavePresent}.");

            AppSettings settings = settingsStore!.LoadAsync().GetAwaiter().GetResult();
            AppLog.Information("Settings", "Settings loaded successfully.");
            return settings;
        }
        catch (Exception exception)
        {
            WriteLog("Settings load failed. Defaults will be used.", exception);
            AppLog.Error("Settings", "Settings load failed. Defaults will be used.", exception);
            return new AppSettings();
        }
    }

    private void ApplyHotkeys(bool reportSuccess)
    {
        if (viewModel is null || hotkeyService is null)
        {
            return;
        }

        CharacterHotkeyBinding[] characterBindings = viewModel.CharacterProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.ActivationHotkey))
            .Select(profile => new CharacterHotkeyBinding(
                profile.CharacterName,
                profile.ActivationHotkey))
            .ToArray();

        string? error = hotkeyService.Apply(
            new HotkeyBindings(
                viewModel.NextCharacterHotkey,
                viewModel.PreviousCharacterHotkey,
                viewModel.TogglePreviewsHotkey),
            characterBindings);

        if (error is not null)
        {
            viewModel.SetSettingsResult($"Hotkey error: {error}");
        }
        else if (reportSuccess)
        {
            viewModel.SetSettingsResult("Settings saved and all global hotkeys registered.");
        }
    }

    private void SaveFinalSettings()
    {
        if (viewModel is null || settingsWriter is null)
        {
            return;
        }

        try
        {
            AppSettings settings = CreatePersistedSettings(viewModel.CreateSettingsSnapshot());
            bool saved = settingsWriter
                .SaveAsync(settings, "application exit")
                .GetAwaiter()
                .GetResult();

            if (!saved)
            {
                WriteLog("Final settings save failed. See runtime.log.");
            }
        }
        catch (Exception exception)
        {
            WriteLog("Final settings save failed.", exception);
            AppLog.Error("Settings", "Final settings save failed.", exception);
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
                    character.ActivationHotkey,
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
                        ActivationHotkey = character.ActivationHotkey,
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

    private void InitializeRuntimeDiagnostics()
    {
        try
        {
            AppLog.Initialize(new FileRuntimeLogger());
            WriteLog($"Runtime log initialized: {AppLog.LogFilePath}");
        }
        catch (Exception exception)
        {
            WriteLog("Runtime logging initialization failed.", exception);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog("Unhandled dispatcher exception.", e.Exception);
        AppLog.Error("Application", "Unhandled dispatcher exception.", e.Exception);
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
        AppLog.Error(
            "Application",
            $"Unhandled AppDomain exception. Terminating: {e.IsTerminating}.",
            e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteLog("Unobserved task exception.", e.Exception);
        AppLog.Error("Application", "Unobserved task exception.", e.Exception);
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
        AppLog.Error("Application", "Fatal startup error.", exception);
        ShowError("EVE Command Center could not start.", exception);
    }

    private static void ShowError(string title, Exception exception)
    {
        string diagnosticPath = AppLog.LogFilePath ?? LogPath;
        string message = $"{exception.GetType().FullName}: {exception.Message}\n\n" +
                         $"Diagnostic log:\n{diagnosticPath}";

        try
        {
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
        }
    }

    private static string CreateLogPath()
    {
        string[] candidateDirectories =
        [
            AppDataPathProvider.GetLogsDirectory(),
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
        }
    }
}
