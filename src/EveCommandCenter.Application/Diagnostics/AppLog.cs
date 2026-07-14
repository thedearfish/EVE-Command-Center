using System.Diagnostics;

namespace EveCommandCenter.Application.Diagnostics;

public enum AppLogLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

public interface IAppLogger : IDisposable
{
    string LogFilePath { get; }

    void Write(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception = null);
}

public static class AppLog
{
    private static IAppLogger? logger;
    private static string currentOperation = "startup";

    public static string CurrentOperation => Volatile.Read(ref currentOperation);

    public static string? LogFilePath => Volatile.Read(ref logger)?.LogFilePath;

    public static void Initialize(IAppLogger appLogger)
    {
        ArgumentNullException.ThrowIfNull(appLogger);
        IAppLogger? previous = Interlocked.Exchange(ref logger, appLogger);
        previous?.Dispose();
        Information("Application", "Runtime logging initialized.");
    }

    public static void Debug(string category, string message) =>
        Write(AppLogLevel.Debug, category, message);

    public static void Information(string category, string message) =>
        Write(AppLogLevel.Information, category, message);

    public static void Warning(string category, string message, Exception? exception = null) =>
        Write(AppLogLevel.Warning, category, message, exception);

    public static void Error(string category, string message, Exception? exception = null) =>
        Write(AppLogLevel.Error, category, message, exception);

    public static IDisposable BeginOperation(string operation, string category = "Operation")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        string previous = Interlocked.Exchange(ref currentOperation, operation);
        Information(category, $"BEGIN {operation}");
        return new OperationScope(operation, previous, category);
    }

    public static void SetCurrentOperation(string operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            operation = "idle";
        }

        Interlocked.Exchange(ref currentOperation, operation);
    }

    public static void Shutdown()
    {
        IAppLogger? active = Interlocked.Exchange(ref logger, null);
        active?.Dispose();
    }

    private static void Write(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception = null)
    {
        try
        {
            Volatile.Read(ref logger)?.Write(level, category, message, exception);
        }
        catch
        {
            // Diagnostics must never destabilize the application.
        }
    }

    private sealed class OperationScope(
        string operation,
        string previousOperation,
        string category) : IDisposable
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            stopwatch.Stop();
            Information(category, $"END {operation} in {stopwatch.Elapsed.TotalMilliseconds:0} ms");
            Interlocked.Exchange(ref currentOperation, previousOperation);
        }
    }
}
