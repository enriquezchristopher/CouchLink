using System.Text;
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "couchlink-profiles-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    public ProfileStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ProfileStore Store() => new(_folder, _log.Add);

    private static string Json(string name) => $$"""{ "format": 1, "name": "{{name}}", "controls": { "Cross": { "keys": ["K"] } } }""";

    private void File(string name, string content) =>
        System.IO.File.WriteAllText(Path.Combine(_folder, name), content, new UTF8Encoding(false));

    private static ControlProfile Sample(string name) => new(name, "Game", 6, true,
        new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Cross] = [VirtualKeys.Space] },
        new Dictionary<PadControl, string> { [PadControl.Cross] = "Pass" });

    [Fact]
    public void A_missing_folder_lists_nothing() =>
        Assert.Empty(new ProfileStore(Path.Combine(_folder, "nope"), _log.Add).List());

    [Fact]
    public void Lists_good_files_by_name_and_logs_why_bad_ones_are_skipped()
    {
        File("b.json", Json("Bravo"));
        File("a.json", Json("alpha"));
        File("broken.json", """{ "format": 1, "name": "X", "controls": { "Square": { "keys": ["Spcae"] } } }""");

        var entries = Store().List();

        Assert.Equal(["alpha", "Bravo"], entries.Select(e => e.DisplayName));
        Assert.Equal(Path.Combine(_folder, "a.json"), entries[0].Path);
        Assert.Contains(_log, line => line == "profile skipped: broken.json: \"Square\": unknown key \"Spcae\"");
    }

    [Fact]
    public void Only_top_level_json_files_are_listed()
    {
        File("good.json", Json("Good"));
        File("notes.txt", Json("Text"));
        File("good.json.1234.tmp", Json("Temp"));
        Directory.CreateDirectory(Path.Combine(_folder, "old"));
        System.IO.File.WriteAllText(Path.Combine(_folder, "old", "older.json"), Json("Older"));

        Assert.Equal(["Good"], Store().List().Select(e => e.DisplayName));
    }

    [Fact]
    public void Two_profiles_with_one_name_show_their_file_names()
    {
        File("2k22.json", Json("NBA 2K22"));
        File("2k22-alt.json", Json("nba 2k22"));

        Assert.Equal(["NBA 2K22 (2k22.json)", "nba 2k22 (2k22-alt.json)"],
            Store().List().Select(e => e.DisplayName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_file_over_64_KB_is_refused_before_reading()
    {
        File("big.json", Json("Big") + new string(' ', ProfileFile.MaxBytes));
        Assert.Equal("File is too large for a profile", ProfileStore.Read(Path.Combine(_folder, "big.json")).Error);
    }

    [Fact]
    public void A_missing_file_says_it_could_not_be_read() =>
        Assert.StartsWith("Couldn't read gone.json: ", ProfileStore.Read(Path.Combine(_folder, "gone.json")).Error);

    [Fact]
    public void A_file_with_a_byte_order_mark_and_accents_loads()
    {
        System.IO.File.WriteAllText(Path.Combine(_folder, "cafe.json"), Json("Café"), new UTF8Encoding(true));
        Assert.Equal("Café", ProfileStore.Read(Path.Combine(_folder, "cafe.json")).Profile?.Name);
    }

    [Fact]
    public void Write_then_Read_gives_the_same_profile()
    {
        string path = Path.Combine(_folder, "mine.json");
        Assert.Null(Store().Write(path, Sample("Mine")));
        ProfileAssert.Same(Sample("Mine"), ProfileStore.Read(path).Profile!);
    }

    [Fact]
    public void Write_replaces_an_existing_file_and_leaves_no_temp_file()
    {
        string path = Path.Combine(_folder, "mine.json");
        File("mine.json", Json("Old"));

        Assert.Null(Store().Write(path, Sample("New")));

        Assert.Equal("New", ProfileStore.Read(path).Profile?.Name);
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [Fact]
    public void A_failed_write_says_why_logs_it_and_leaves_nothing()
    {
        string path = Path.Combine(_folder, "missing-folder", "mine.json");

        string? error = Store().Write(path, Sample("Mine"));

        Assert.StartsWith("Couldn't save mine.json: ", error);
        Assert.Single(_log);
        Assert.Empty(Directory.GetFiles(_folder, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void TryCreateFolder_makes_the_folder()
    {
        var store = new ProfileStore(Path.Combine(_folder, "profiles"), _log.Add);
        Assert.True(store.TryCreateFolder());
        Assert.True(Directory.Exists(store.Folder));
    }

    [Theory]
    [InlineData("NBA 2K22 Café", "NBA 2K22 Café.json")]
    [InlineData("2K22: back/room?", "2K22- back-room-.json")]
    [InlineData("  ...  ", "profile.json")]
    public void Suggested_file_names_are_safe(string name, string file) =>
        Assert.Equal(file, ProfileStore.SuggestedFileName(name));
}
