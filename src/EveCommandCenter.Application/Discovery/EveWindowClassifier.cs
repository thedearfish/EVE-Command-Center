namespace EveCommandCenter.Application.Discovery;

public sealed class EveWindowClassifier
{
    private const string EveProcessName = "exefile";
    private const string EveTitlePrefix = "EVE";

    public EveWindowClassification Classify(WindowCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!candidate.IsVisible || candidate.WindowId.IsEmpty)
        {
            return NotEve();
        }

        if (!string.Equals(candidate.ProcessName, EveProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return NotEve();
        }

        string title = candidate.Title.Trim();
        if (!title.StartsWith(EveTitlePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return NotEve();
        }

        string displayName = ExtractDisplayName(title);
        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.Equals("Character Selection", StringComparison.OrdinalIgnoreCase))
        {
            return new EveWindowClassification(EveWindowKind.CharacterSelection, "Character Selection");
        }

        return new EveWindowClassification(EveWindowKind.Character, displayName);
    }

    private static string ExtractDisplayName(string title)
    {
        if (title.Equals(EveTitlePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        const string separator = " - ";
        int separatorIndex = title.IndexOf(separator, StringComparison.Ordinal);
        return separatorIndex < 0
            ? string.Empty
            : title[(separatorIndex + separator.Length)..].Trim();
    }

    private static EveWindowClassification NotEve() =>
        new(EveWindowKind.NotEve, string.Empty);
}
