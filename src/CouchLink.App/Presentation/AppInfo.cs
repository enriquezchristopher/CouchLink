namespace CouchLink.App.Presentation;

/// <summary>The version shown on the Start screen and in About CouchLink.</summary>
internal static class AppInfo
{
    public static string Version { get; } = Format(typeof(AppInfo).Assembly.GetName().Version);

    public static string AboutText =>
        $"Version {Version}\nCouch co-op over the LAN. No accounts, no cloud.\nLicensed under the GNU GPL v3.";

    internal static string Format(Version? version) =>
        version is null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";
}
