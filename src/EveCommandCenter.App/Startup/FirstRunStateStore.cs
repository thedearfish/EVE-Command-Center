using System.IO;
using System.Text.Json;
using EveCommandCenter.Infrastructure.Settings;

namespace EveCommandCenter.App.Startup;

public sealed class FirstRunStateStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string statePath;

    public FirstRunStateStore()
        : this(AppDataPathProvider.GetFirstRunStatePath())
    {
    }

    internal FirstRunStateStore(string statePath)
    {
        this.statePath = statePath;
    }

    public bool IsFirstRun()
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return true;
            }

            string json = File.ReadAllText(statePath);
            PersistedState? state = JsonSerializer.Deserialize<PersistedState>(json);
            return state is null || !state.FirstRunCompleted;
        }
        catch
        {
            return true;
        }
    }

    public void MarkCompleted()
    {
        string? directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var state = new PersistedState(CurrentSchemaVersion, true);
        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        string temporaryPath = statePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, statePath, true);
    }

    private sealed record PersistedState(int SchemaVersion, bool FirstRunCompleted);
}
