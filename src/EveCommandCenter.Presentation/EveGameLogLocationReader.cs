using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.Presentation;

public sealed class EveGameLogLocationReader
{
    private const int MaximumFilesToInspect = 80;
    private const long MaximumFileBytes = 2 * 1024 * 1024;

    private static readonly Regex ListenerPattern = new(
        @"^\s*Listener\s*:\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex LocalChannelPattern = new(
        @"Channel\s+changed\s+to\s+Local\s*:\s*(?<value>[^\r\n<]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlTagPattern = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    private readonly Dictionary<string, CachedLog> cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset lastMissingDirectoryWarning = DateTimeOffset.MinValue;

    public IReadOnlyDictionary<string, string> Capture()
    {
        var result = new Dictionary<string, LocationStamp>(StringComparer.OrdinalIgnoreCase);
        string[] directories = GetCandidateDirectories()
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (directories.Length == 0)
        {
            if (DateTimeOffset.UtcNow - lastMissingDirectoryWarning > TimeSpan.FromMinutes(10))
            {
                lastMissingDirectoryWarning = DateTimeOffset.UtcNow;
                AppLog.Debug("Location", "No EVE game-log directory was found; solar-system labels remain empty.");
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        FileInfo[] files = directories
            .SelectMany(directory =>
            {
                try
                {
                    return new DirectoryInfo(directory).EnumerateFiles("*.txt", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    return Enumerable.Empty<FileInfo>();
                }
            })
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

            var stamp = new LocationStamp(parsed.SolarSystem, file.LastWriteTimeUtc);
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
            AppLog.Debug("Location", $"Could not parse EVE game log '{file.Name}': {exception.Message}");
            return null;
        }
    }

    private static ParsedLog? ParseFile(FileInfo file)
    {
        if (file.Length <= 0 || file.Length > MaximumFileBytes)
        {
            return null;
        }

        string text;
        using (var stream = new FileStream(
                   file.FullName,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        Match listener = ListenerPattern.Match(text);
        MatchCollection locations = LocalChannelPattern.Matches(text);
        if (!listener.Success || locations.Count == 0)
        {
            return null;
        }

        string characterName = CleanValue(listener.Groups["value"].Value);
        string solarSystem = CleanValue(locations[^1].Groups["value"].Value);
        return string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(solarSystem)
            ? null
            : new ParsedLog(characterName, solarSystem);
    }

    private static string CleanValue(string value)
    {
        string withoutTags = HtmlTagPattern.Replace(value, string.Empty);
        return withoutTags
            .Replace("\uFEFF", string.Empty, StringComparison.Ordinal)
            .Trim()
            .Trim('[', ']', '"');
    }

    private static IEnumerable<string> GetCandidateDirectories()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            yield return Path.Combine(documents, "EVE", "logs", "Gamelogs");
        }

        foreach (string variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            string? oneDrive = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(oneDrive))
            {
                yield return Path.Combine(oneDrive, "Documents", "EVE", "logs", "Gamelogs");
            }
        }
    }

    private sealed record CachedLog(DateTime LastWriteTimeUtc, long Length, ParsedLog? Parsed);

    private sealed record ParsedLog(string CharacterName, string SolarSystem);

    private readonly record struct LocationStamp(string SolarSystem, DateTime TimestampUtc);
}
