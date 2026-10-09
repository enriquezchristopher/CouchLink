using System.IO;
namespace CouchLink.App.Tests;

/// <summary>Finds source files from the test's bin folder by walking up to the folder with CouchLink.slnx.</summary>
internal static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    public static string AppSource => Path.Combine(Root, "src", "CouchLink.App");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CouchLink.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("CouchLink.slnx not found above " + AppContext.BaseDirectory);
    }
}
