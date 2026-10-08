namespace CouchLink.Core.Net;

public static class Ports
{
    /// <summary>Host -> LAN broadcast: "I'm hosting" once a second. Only clients listen on it.</summary>
    public const int Discovery = 47800;

    /// <summary>Client -> host TCP session channel: join, approval, heartbeat, leave, kick.</summary>
    public const int Session = 47801;

    /// <summary>Host -> client video and audio; host -> client timing replies.</summary>
    public const int Video = 47802;

    /// <summary>Client -> host controller state, keyframe requests and timing pings.</summary>
    public const int Input = 47803;
}
