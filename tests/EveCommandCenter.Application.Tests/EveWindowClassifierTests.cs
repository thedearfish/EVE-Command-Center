using EveCommandCenter.Application.Discovery;
using EveCommandCenter.Core.Clients;

namespace EveCommandCenter.Application.Tests;

public sealed class EveWindowClassifierTests
{
    private readonly EveWindowClassifier _classifier = new();

    [Fact]
    public void Classify_ReturnsCharacter_ForVisibleExeFileWindowWithCharacterTitle()
    {
        WindowCandidate candidate = CreateCandidate("exefile", "EVE - Torik", isVisible: true);

        EveWindowClassification result = _classifier.Classify(candidate);

        Assert.Equal(EveWindowKind.Character, result.Kind);
        Assert.Equal("Torik", result.DisplayName);
    }

    [Theory]
    [InlineData("EVE")]
    [InlineData("EVE - Character Selection")]
    public void Classify_ReturnsCharacterSelection_WhenNoCharacterIsActive(string title)
    {
        WindowCandidate candidate = CreateCandidate("exefile", title, isVisible: true);

        EveWindowClassification result = _classifier.Classify(candidate);

        Assert.Equal(EveWindowKind.CharacterSelection, result.Kind);
        Assert.Equal("Character Selection", result.DisplayName);
    }

    [Fact]
    public void Classify_RejectsWindowFromAnotherProcess()
    {
        WindowCandidate candidate = CreateCandidate("notepad", "EVE - Torik", isVisible: true);

        EveWindowClassification result = _classifier.Classify(candidate);

        Assert.Equal(EveWindowKind.NotEve, result.Kind);
    }

    [Fact]
    public void Classify_RejectsInvisibleWindow()
    {
        WindowCandidate candidate = CreateCandidate("exefile", "EVE - Torik", isVisible: false);

        EveWindowClassification result = _classifier.Classify(candidate);

        Assert.Equal(EveWindowKind.NotEve, result.Kind);
    }

    private static WindowCandidate CreateCandidate(
        string processName,
        string title,
        bool isVisible) =>
        new(
            new WindowId(123),
            456,
            processName,
            title,
            isVisible,
            IsMinimized: false,
            IsResponsive: true);
}
