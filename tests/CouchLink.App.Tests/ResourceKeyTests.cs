using System.IO;
using System.Text.RegularExpressions;

namespace CouchLink.App.Tests;

/// <summary>
/// A misspelled DynamicResource fails silently: the control just has no color. Every key used anywhere
/// in CouchLink.App must be defined in Theme/*.xaml (or be a Motion key, which ThemeManager puts in from Theme/Motion.cs).
/// </summary>
public partial class ResourceKeyTests
{
    [Fact]
    public void Every_resource_key_used_in_the_app_is_defined_by_the_theme()
    {
        var defined = Directory.GetFiles(Path.Combine(RepoFiles.AppSource, "Theme"), "*.xaml")
            .SelectMany(f => DefinedKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Concat(CouchLink.App.Theme.Motion.ResourceKeys)
            .ToHashSet();

        var sources = Directory.GetFiles(RepoFiles.AppSource, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs"))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        var missing = new List<string>();
        foreach (var file in sources)
        {
            string text = File.ReadAllText(file);
            foreach (Match m in UsedKey().Matches(text))
            {
                string key = m.Groups["xaml"].Success ? m.Groups["xaml"].Value : m.Groups["cs"].Value;
                if (!defined.Contains(key))
                    missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }
        Assert.Empty(missing);
    }

    [GeneratedRegex(@"x:Key=""(\w+)""")]
    private static partial Regex DefinedKey();

    [GeneratedRegex("""\{(?:Dynamic|Static)Resource\s+(?<xaml>[A-Za-z]\w*)\}|(?:SetResourceReference\([^,]+,\s*|FindResource\(|TryFindResource\()"(?<cs>\w+)"\)?""")]
    private static partial Regex UsedKey();
}
