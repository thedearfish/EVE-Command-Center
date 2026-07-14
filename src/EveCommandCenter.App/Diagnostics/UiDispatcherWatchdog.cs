using System.Diagnostics;
using System.Windows.Threading;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.App.Diagnostics;

public sealed class UiDispatcherWatchdog : IDisposable
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan InitialTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RepeatedWarningInterval = TimeSpan.FromSeconds(5);

    private readonly Dispatcher dispatcher;
    private readonly CancellationTokenSource cancellation = new();
    private Task? monitorTask;
    private bool disposed;

    public UiDispatcherWatchdog(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Start()
    {
        if (monitorTask is not null)
        {
            return;
        }

        monitorTask = Task.Run(() => MonitorAsync(cancellation.Token));
        AppLog.Information("Watchdog", "UI dispatcher watchdog started.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellation.Cancel();

        try
        {
            monitorTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Shutdown must not be blocked by diagnostics.
        }

        cancellation.Dispose();
        AppLog.Information("Watchdog", "UI dispatcher watchdog stopped.");
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(ProbeInterval, cancellationToken).ConfigureAwait(false);

                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    return;
                }

                var acknowledgement = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                long startedAt = Stopwatch.GetTimestamp();

                try
                {
                    _ = dispatcher.BeginInvoke(
                        DispatcherPriority.Send,
                        new Action(() => acknowledgement.TrySetResult()));
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                Task firstWait = await Task.WhenAny(
                        acknowledgement.Task,
                        Task.Delay(InitialTimeout, cancellationToken))
                    .ConfigureAwait(false);

                if (firstWait == acknowledgement.Task)
                {
                    continue;
                }

                while (!acknowledgement.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
                {
                    TimeSpan delay = Stopwatch.GetElapsedTime(startedAt);
                    AppLog.Warning(
                        "Watchdog",
                        $"UI dispatcher has not responded for {delay.TotalSeconds:0.0} seconds. " +
                        $"Last operation: {AppLog.CurrentOperation}.");

                    Task nextWait = await Task.WhenAny(
                            acknowledgement.Task,
                            Task.Delay(RepeatedWarningInterval, cancellationToken))
                        .ConfigureAwait(false);

                    if (nextWait == acknowledgement.Task)
                    {
                        break;
                    }
                }

                if (acknowledgement.Task.IsCompleted)
                {
                    TimeSpan blockedFor = Stopwatch.GetElapsedTime(startedAt);
                    AppLog.Information(
                        "Watchdog",
                        $"UI dispatcher recovered after {blockedFor.TotalSeconds:0.0} seconds.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception exception)
        {
            AppLog.Error("Watchdog", "UI dispatcher watchdog failed.", exception);
        }
    }
}
