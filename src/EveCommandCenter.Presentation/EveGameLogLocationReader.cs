using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.Presentation;

public sealed class EveGameLogLocationReader
{
    private const int MaximumFilesToInspect = 500;
    private const long MaximumFileBytes = 16 * 1024 * 1024;

    private static readonly Regex ListenerPattern = new(
        @"^\s*Listener\s*:\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex LocalChannelPattern = new(
        @"Channel\s+changed\s+to\s+Local\s*:?\s*(?<value>[^\r\n<]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JumpPattern = new(
        @"Jumping\s+from\s+.+?\s+to\s+(?<value>[^\r\n<]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EnteredPattern = new(
        @"(?:You\s+have\s+entered|Entering\s+solar\s+system|Solar\s+system\s+changed\s+to)\s+(?<value>[^\r\n<]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlTagPattern = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    private readonly Dictionary<string, CachedLog> cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset lastMissingDirectoryWarning = DateTimeOffset.MinValue;

    public IReadOnlyDictionary<string, string> Capture()
    {
        var result = new Dictionary<string, LocationStamp>(StringComparer.OrdinalIgnoreCase);
        LogDirectory[] directories = GetCandidateDirectories()
            .Where(candidate => Directory.Exists(candidate.Path))
            .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (directories.Length == 0)
        {
            if (DateTimeOffset.UtcNow - lastMissingDirectoryWarning > TimeSpan.FromMinutes(10))
            {
                lastMissingDirectoryWarning = DateTimeOffset.UtcNow;
                AppLog.Debug("Location", "No EVE chat/game-log directory was found; solar-system labels remain empty.");
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        FileInfo[] files = directories
            .SelectMany(EnumerateLogFiles)
            .Where(file => file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-14))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaximumFilesToInspect)
            .ToArray();

        var activePaths = files.Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string stalePath in cache.Keys.Where(path => !activePaths.Contains(path)).ToArray())
        {
            cache.Remove(stalePath);
        }

        foreach (FileInfo file in files)
        {
            ParsedLog? parsed = GetOrParse(file);
            if (parsed is null ||
                string.IsNullOrWhiteSpace(parsed.CharacterName) ||
                string.IsNullOrWhiteSpace(parsed.SolarSystem))
            {
                continue;
            }

            var stamp = new LocationStamp(parsed.SolarSystem, parsed.ObservedAtUtc);
            if (!result.TryGetValue(parsed.CharacterName, out LocationStamp existing) ||
                stamp.TimestampUtc >= existing.TimestampUtc)
            {
                result[parsed.CharacterName] = stamp;
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.SolarSystem,
            StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<FileInfo> EnumerateLogFiles(LogDirectory directory)
    {
        try
        {
            string pattern = directory.Kind == LogKind.Chat ? "Local_*.txt" : "*.txt";
            return new DirectoryInfo(directory.Path).EnumerateFiles(pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Enumerable.Empty<FileInfo>();
        }
    }

    private ParsedLog? GetOrParse(FileInfo file)
    {
        try
        {
            if (cache.TryGetValue(file.FullName, out CachedLog? cached) &&
                cached.LastWriteTimeUtc == file.LastWriteTimeUtc &&
                cached.Length == file.Length)
            {
                return cached.Parsed;
            }

            ParsedLog? parsed = ParseFile(file);
            cache[file.FullName] = new CachedLog(file.LastWriteTimeUtc, file.Length, parsed);
            return parsed;
        }
        catch (Exception exception)
        {
            AppLog.Debug("Location", $"Could not parse EVE log '{file.Name}': {exception.Message}");
            return null;
        }
    }

    private static ParsedLog? ParseFile(FileInfo file)
    {
        if (file.Length <= 0 || file.Length > MaximumFileBytes)
        {
            return null;
        }

        string text = ReadEveLog(file);
        Match listener = ListenerPattern.Match(text);
        if (!listener.Success)
        {
            return null;
        }

        Match? latestLocation = FindLatestLocation(text);
        if (latestLocation is null)
        {
            return null;
        }

        string characterName = CleanValue(listener.Groups["value"].Value);
        string solarSystem = CleanSystemName(latestLocation.Groups["value"].Value);
        if (string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(solarSystem))
        {
            return null;
        }

        DateTime observedAtUtc = TryParseTimestampNear(text, latestLocation.Index, out DateTime timestamp)
            ? timestamp
            : file.LastWriteTimeUtc;

        return new ParsedLog(characterName, solarSystem, observedAtUtc);
    }

    private static string ReadEveLog(FileInfo file)
    {
        using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        // EVE currently writes its logs as UTF-16 LE. BOM detection keeps this
        // compatible with older UTF-8 logs as well.
        using var reader = new StreamReader(
            stream,
            Encoding.Unicode,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: false);
        return reader.ReadToEnd();
    }

    private static Match? FindLatestLocation(string text)
    {
        Match? latest = null;
        foreach (Regex pattern in new[] { LocalChannelPattern, JumpPattern, EnteredPattern })
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (match.Success && (latest is null || match.Index > latest.Index))
                {
                    latest = match;
                }
            }
        }

        return latest;
    }

    private static bool TryParseTimestampNear(string text, int matchIndex, out DateTime timestampUtc)
    {
        timestampUtc = default;
        int lineStart = text.LastIndexOf('\n', Math.Max(0, matchIndex - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int lineEnd = text.IndexOf('\n', matchIndex);
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;
        string line = text[lineStart..lineEnd];

        int open = line.IndexOf('[');
        int close = line.IndexOf(']', open + 1);
        if (open < 0 || close <= open)
        {
            return false;
        }

        string value = line[(open + 1)..close].Trim();
        return DateTime.TryParseExact(
            value,
            "yyyy.MM.dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out timestampUtc);
    }

    private static string CleanValue(string value)
    {
        string withoutTags = HtmlTagPattern.Replace(value, string.Empty);
        return withoutTags
            .Replace("\uFEFF", string.Empty, StringComparison.Ordinal)
            .Trim()
            .Trim('[', ']', '"');
    }

    private static string CleanSystemName(string value)
    {
        string cleaned = CleanValue(value)
            .TrimEnd('.', '!', '\r', '\n');

        int parenthesis = cleaned.IndexOf(" (", StringComparison.Ordinal);
        if (parenthesis > 0)
        {
            cleaned = cleaned[..parenthesis].TrimEnd();
        }

        return cleaned.Length <= 64 ? cleaned : string.Empty;
    }

    private static IEnumerable<LogDirectory> GetCandidateDirectories()
    {
        foreach (string documents in GetDocumentRoots())
        {
            yield return new LogDirectory(Path.Combine(documents, "EVE", "logs", "Chatlogs"), LogKind.Chat);
            yield return new LogDirectory(Path.Combine(documents, "EVE", "logs", "Gamelogs"), LogKind.Game);
        }
    }

    private static IEnumerable<string> GetDocumentRoots()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            yield return documents;
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            yield return Path.Combine(profile, "Documents");
        }

        foreach (string variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            string? oneDrive = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(oneDrive))
            {
                yield return Path.Combine(oneDrive, "Documents");
            }
        }
    }

    private sealed record CachedLog(DateTime LastWriteTimeUtc, long Length, ParsedLog? Parsed);

    private sealed record ParsedLog(string CharacterName, string SolarSystem, DateTime ObservedAtUtc);

    private readonly record struct LocationStamp(string SolarSystem, DateTime TimestampUtc);

    private readonly record struct LogDirectory(string Path, LogKind Kind);

    private enum LogKind
    {
        Chat,
        Game,
    }
}
