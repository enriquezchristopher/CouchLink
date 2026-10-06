namespace CouchLink.Core.Net;

public static class Ports
{
    /// <summary>Host -> client video (and audio, from v1.3).</summary>
    public const int Video = 47802;

    /// <summary>Client -> host controller state, and keyframe requests until v1.4.</summary>
    public const int Input = 47803;
}
