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

    private readonly SemaphoreSlim ioGate = new(1, 1);

    public async Task<T> LoadAsync(CancellationToken cancellationToken = default)
    {
        await ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string temporaryPath = filePath + ".tmp";

            if (File.Exists(filePath))
            {
                try
                {
                    T settings = await DeserializeAsync(filePath, cancellationToken).ConfigureAwait(false);
                    TryDelete(temporaryPath);
                    return settings;
                }
                catch (Exception exception) when (
                    exception is JsonException or IOException or UnauthorizedAccessException)
                {
                    if (!File.Exists(temporaryPath))
                    {
                        throw;
                    }

                    T recovered = await DeserializeAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
                    File.Move(temporaryPath, filePath, overwrite: true);
                    return recovered;
                }
            }

            if (File.Exists(temporaryPath))
            {
                T recovered = await DeserializeAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, filePath, overwrite: true);
                return recovered;
            }

            return new T();
        }
        finally
        {
            ioGate.Release();
        }
    }

    public async Task SaveAsync(
        T settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = filePath + ".tmp";

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        settings,
                        _serializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            ioGate.Release();
        }
    }

    private async Task<T> DeserializeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);

        return await JsonSerializer.DeserializeAsync<T>(
                stream,
                _serializerOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? new T();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A stale temporary file can be retried on the next load/save.
        }
    }
}
