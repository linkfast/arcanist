namespace Arcanist.Core.Organization;

/// <summary>
/// Tells <see cref="LibraryWatcher"/> that which folders to watch changed (a library started or stopped watching
/// for changes or organizing itself, or its folder changed), so it does not wait for its next periodic check.
/// </summary>
public sealed class LibraryWatchSignal
{
    private int _requested;

    public void Refresh() => Interlocked.Exchange(ref _requested, 1);

    internal bool TakeRequest() => Interlocked.Exchange(ref _requested, 0) == 1;
}
