namespace EveCommandCenter.Infrastructure.Settings;

public static class AppDataPathProvider
{
    public static string GetSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(
            localAppData,
            "EVE Command Center",
            "settings.json");
    }
}
