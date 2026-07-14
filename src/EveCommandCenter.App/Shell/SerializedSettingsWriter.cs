using System.Diagnostics;
using EveCommandCenter.Application.Diagnostics;
using EveCommandCenter.Infrastructure.Settings;

namespace EveCommandCenter.App.Shell;

public sealed class SerializedSettingsWriter : IDisposable
{
    private readonly JsonSettingsStore<AppSettings> store;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public SerializedSettingsWriter(JsonSettingsStore<AppSettings> store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<bool> SaveAsync(
        AppSettings settings,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(settings);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            AppLog.Information("Settings", $"Settings save started. Reason: {reason}.");
            await store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            AppLog.Information(
                "Settings",
                $"Settings save completed in {stopwatch.Elapsed.TotalMilliseconds:0} ms. Reason: {reason}.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppLog.Warning("Settings", $"Settings save cancelled. Reason: {reason}.");
            return false;
        }
        catch (Exception exception)
        {
            AppLog.Error("Settings", $"Settings save failed. Reason: {reason}.", exception);
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Dispose();
    }
}
