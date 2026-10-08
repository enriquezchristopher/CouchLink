namespace CouchLink.Core.Protocol;

public enum SessionMessageType : byte
{
    JoinRequest = Wire.TypeJoinRequest,
    Accepted = Wire.TypeAccepted,
    Denied = Wire.TypeDenied,
    Heartbeat = Wire.TypeHeartbeat,
    Leave = Wire.TypeLeave,
    Kicked = Wire.TypeKicked,
    HostEnded = Wire.TypeHostEnded,
}

public enum DenyReason : byte
{
    Denied = 1,
    Full = 2,
    TimedOut = 3,
    /// <summary>The host's virtual pad could not be created.</summary>
    PadFailed = 4,
}

/// <summary>
/// One message on the TCP session channel. <see cref="Name"/> is set only on a join request,
/// <see cref="Slot"/> only on Accepted, <see cref="Reason"/> only on Denied.
/// </summary>
public readonly record struct SessionMessage(SessionMessageType Type, string Name = "", byte Slot = 0, DenyReason Reason = 0)
{
    public static readonly SessionMessage Heartbeat = new(SessionMessageType.Heartbeat);
    public static readonly SessionMessage Leave = new(SessionMessageType.Leave);
    public static readonly SessionMessage Kicked = new(SessionMessageType.Kicked);
    public static readonly SessionMessage HostEnded = new(SessionMessageType.HostEnded);

    public static SessionMessage JoinRequest(string name) => new(SessionMessageType.JoinRequest, Name: PcName.Clip(name));

    public static SessionMessage Accepted(byte slot) => new(SessionMessageType.Accepted, Slot: slot);

    public static SessionMessage Denied(DenyReason reason) => new(SessionMessageType.Denied, Reason: reason);

    public override string ToString() => Type switch
    {
        SessionMessageType.JoinRequest => $"JoinRequest {Name}",
        SessionMessageType.Accepted => $"Accepted P{Slot}",
        SessionMessageType.Denied => $"Denied {Reason}",
        _ => Type.ToString(),
    };
}
