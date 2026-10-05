namespace CouchLink.Core.Diagnostics;

/// <summary>Facts about the app and machine at crash time. Must not contain personal data.</summary>
public sealed record CrashContext(
    string AppVersion,
    DateTimeOffset Time,
    TimeSpan Uptime,
    string Windows,
    string Runtime,
    string Cpu,
    long RamBytes,
    string Gpu,
    string ViGEmBus,
    string Mode);
