namespace EveCommandCenter.Application.Discovery;

public interface IWindowSnapshotSource
{
    IReadOnlyList<WindowCandidate> Capture();
}
