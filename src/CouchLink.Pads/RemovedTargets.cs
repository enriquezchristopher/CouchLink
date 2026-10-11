namespace CouchLink.Pads;

/// <summary>
/// Never free a ViGEm target. Connect starts a native notification thread (vigemclient.dll) that nothing can stop;
/// it stays blocked after unplug and reads the target struct if it ever wakes. Freeing the target (Dispose, or the
/// finalizer once unreferenced) is a use-after-free that crashes the host. Rooting it costs about one idle thread
/// and 2 handles per pad ever created.
/// </summary>
internal static class RemovedTargets
{
    private static readonly List<object> Kept = [];

    public static void Keep(object target)
    {
        lock (Kept)
            Kept.Add(target);
    }
}
