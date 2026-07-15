namespace EveCommandCenter.Infrastructure.Settings;

public static class AppDataPathProvider
{
    public static string GetSettingsPath() =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static string GetFirstRunStatePath() =>
        Path.Combine(AppContext.BaseDirectory, "app-state.json");

    public static string GetLogsDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "logs");
}
