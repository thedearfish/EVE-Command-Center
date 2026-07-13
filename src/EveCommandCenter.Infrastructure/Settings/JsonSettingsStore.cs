using System.Text.Json;

namespace EveCommandCenter.Infrastructure.Settings;

public sealed class JsonSettingsStore<T>(
    string filePath,
    JsonSerializerOptions? serializerOptions = null)
    where T : class, new()
{
    private readonly JsonSerializerOptions _serializerOptions =
        serializerOptions ?? new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

    public async Task<T> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new T();
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<T>(
                stream,
                _serializerOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? new T();
    }

    public async Task SaveAsync(
        T settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = filePath + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    _serializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporaryPath, filePath, overwrite: true);
    }
}
