namespace Arcanist.Core.Videos;

/// <summary>Wakes the aligner when someone asks for an alignment, and tells listeners when one is ready.</summary>
public sealed class VideoSignals
{
    public event Action? AlignmentRequested;

    /// <summary>A video was aligned (its id).</summary>
    public event Action<Guid>? AlignmentReady;

    public void RequestAlignment() => AlignmentRequested?.Invoke();

    public void Aligned(Guid videoId) => AlignmentReady?.Invoke(videoId);
}
