using System.IO;
using System.Text;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.App.Diagnostics;

public sealed class FileRuntimeLogger : IAppLogger
{
    private const long MaximumLogBytes = 5 * 1024 * 1024;
    private const int RetainedLogCount = 3;

    private readonly object sync = new();
    private readonly string crashLogPath;
    private bool disposed;

    public FileRuntimeLogger()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string directory = Path.Combine(localAppData, "EVE Command Center", "logs");
        Directory.CreateDirectory(directory);

        LogFilePath = Path.Combine(directory, "runtime.log");
        crashLogPath = Path.Combine(directory, "crash.log");

        WriteRaw(LogFilePath, Environment.NewLine + new string('=', 88) + Environment.NewLine);
        WriteRaw(
            LogFilePath,
            $"Session started {DateTimeOffset.Now:O}; process={Environment.ProcessId}; " +
            $"version={typeof(FileRuntimeLogger).Assembly.GetName().Version}; " +
            $"os={Environment.OSVersion}{Environment.NewLine}");
    }

    public string LogFilePath { get; }

    public void Write(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception = null)
    {
        if (disposed)
        {
            return;
        }

        var builder = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" [")
            .Append(level.ToString().ToUpperInvariant())
            .Append("] [P")
            .Append(Environment.ProcessId)
            .Append(":T")
            .Append(Environment.CurrentManagedThreadId)
            .Append("] [")
            .Append(string.IsNullOrWhiteSpace(category) ? "General" : category.Trim())
            .Append("] ")
            .AppendLine(message);

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        string text = builder.ToString();

        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            RotateIfNeeded();
            WriteRaw(LogFilePath, text);

            if (level == AppLogLevel.Error)
            {
                WriteRaw(crashLogPath, text);
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            WriteRaw(
                LogFilePath,
                $"{DateTimeOffset.Now:O} [INFORMATION] [P{Environment.ProcessId}:T{Environment.CurrentManagedThreadId}] " +
                "[Application] Runtime logger stopped." + Environment.NewLine);
            disposed = true;
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            var current = new FileInfo(LogFilePath);
            if (!current.Exists || current.Length < MaximumLogBytes)
            {
                return;
            }

            string directory = current.DirectoryName!;
            string oldest = Path.Combine(directory, $"runtime.{RetainedLogCount}.log");
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (int index = RetainedLogCount - 1; index >= 1; index--)
            {
                string source = Path.Combine(directory, $"runtime.{index}.log");
                string destination = Path.Combine(directory, $"runtime.{index + 1}.log");
                if (File.Exists(source))
                {
                    File.Move(source, destination, overwrite: true);
                }
            }

            File.Move(LogFilePath, Path.Combine(directory, "runtime.1.log"), overwrite: true);
        }
        catch
        {
            // Failure to rotate must not prevent future logging attempts.
        }
    }

    private static void WriteRaw(string path, string text)
    {
        try
        {
            File.AppendAllText(path, text, Encoding.UTF8);
        }
        catch
        {
            // Logging is a best-effort diagnostic facility.
        }
    }
}
