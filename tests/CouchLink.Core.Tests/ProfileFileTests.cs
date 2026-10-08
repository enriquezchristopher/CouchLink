using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class ProfileFileTests
{
    /// <summary>Writes JSON with ' for " so test data stays readable.</summary>
    private static string J(string text) => text.Replace('\'', '"');

    private static string WithControls(string controls) => J($"{{'format':1,'name':'T','controls':{{{controls}}}}}");

    private const string SpecExample = """
        {
          "format": 1,
          "name": "NBA 2K22 Café layout",
          "game": "NBA 2K22",
          "sensitivity": 6,
          "invertY": false,
          "controls": {
            "Square":  { "keys": ["J", "LeftClick"], "label": "Shoot" },
            "Cross":   { "keys": ["K"], "label": "Pass" },
            "R2":      { "keys": ["LShift"], "label": "Sprint" },
            "LeftUp":  { "keys": ["W"] }
          }
        }
        """;

    private static ControlProfile Loaded(string json)
    {
        var result = ProfileFile.Load(json);
        Assert.Null(result.Error);
        return result.Profile!;
    }

    [Fact]
    public void The_spec_example_loads()
    {
        var p = Loaded(SpecExample);
        Assert.Equal("NBA 2K22 Café layout", p.Name);
        Assert.Equal("NBA 2K22", p.Game);
        Assert.Equal(6, p.SensitivityStep);
        Assert.False(p.InvertY);
        Assert.Equal([VirtualKeys.Letter('J'), VirtualKeys.LButton], p.Keys[PadControl.Square]);
        Assert.Equal([VirtualKeys.LShift], p.Keys[PadControl.R2]);
        Assert.Equal("Shoot", p.Labels[PadControl.Square]);
        Assert.False(p.Labels.ContainsKey(PadControl.LeftUp));
    }

    [Fact]
    public void Controls_left_out_have_no_key()
    {
        var p = Loaded(SpecExample);
        Assert.False(p.Keys.ContainsKey(PadControl.LeftDown));
        Assert.False(p.Keys.ContainsKey(PadControl.Circle));
    }

    [Fact]
    public void Missing_optional_fields_get_their_defaults()
    {
        var p = Loaded(WithControls(""));
        Assert.Null(p.Game);
        Assert.Equal(ControlSettings.DefaultStep, p.SensitivityStep);
        Assert.False(p.InvertY);
        Assert.Empty(p.Keys);
        Assert.Empty(p.Labels);
    }

    [Fact]
    public void Comments_trailing_commas_any_case_and_unknown_top_level_fields_are_fine()
    {
        var p = Loaded(J("""
            {
              // made for the back room
              'FORMAT': 1,
              'Name': '  Back room  ',
              'theme': 'dark',
              'controls': { 'square': { 'KEYS': ['j', 'J'], 'Label': ' Shoot ' }, },
            }
            """));
        Assert.Equal("Back room", p.Name);
        Assert.Equal([VirtualKeys.Letter('J')], p.Keys[PadControl.Square]); // listed twice, kept once
        Assert.Equal("Shoot", p.Labels[PadControl.Square]);
    }

    [Theory]
    [InlineData("{'name':'T','controls':{}}", "Missing \"format\"")]
    [InlineData("{'format':'1','name':'T','controls':{}}", "Missing \"format\"")]
    [InlineData("{'format':2,'name':'T','controls':{}}", "This profile was made for a newer CouchLink")]
    [InlineData("{'format':1,'controls':{}}", "\"name\" must be 1-60 characters")]
    [InlineData("{'format':1,'name':'   ','controls':{}}", "\"name\" must be 1-60 characters")]
    [InlineData("{'format':1,'name':'T'}", "Missing \"controls\"")]
    [InlineData("{'format':1,'name':'T','sensitivity':0,'controls':{}}", "\"sensitivity\" must be 1-10")]
    [InlineData("{'format':1,'name':'T','sensitivity':11,'controls':{}}", "\"sensitivity\" must be 1-10")]
    [InlineData("{'format':1,'name':'T','sensitivity':'5','controls':{}}", "\"sensitivity\" must be 1-10")]
    [InlineData("{'format':1,'name':'T','invertY':'yes','controls':{}}", "\"invertY\" must be true or false")]
    [InlineData("[]", "Not a valid profile file: it must be a JSON object")]
    public void Bad_top_level_fields_say_what_is_wrong(string json, string message) =>
        Assert.Equal(message, ProfileFile.Load(J(json)).Error);

    [Theory]
    [InlineData("'Sqaure':{'keys':['J']}", "Unknown control \"Sqaure\"")]
    [InlineData("'3':{'keys':['J']}", "Unknown control \"3\"")]
    [InlineData("'Square':{'keys':['Spcae']}", "\"Square\": unknown key \"Spcae\"")]
    [InlineData("'Square':{'keys':['F1']}", "\"Square\": F1 is reserved")]
    [InlineData("'Square':{'keys':['Esc']}", "\"Square\": Esc is reserved")]
    [InlineData("'Square':{'keys':['0x5B']}", "\"Square\": Left Windows is reserved")]
    [InlineData("'Cross':{'keys':['K']},'Circle':{'keys':['k']}", "\"K\" is on both Cross and Circle")]
    [InlineData("'Square':{'keys':'J'}", "\"Square\": \"keys\" must be a list of key names")]
    [InlineData("'Square':{'keys':[74]}", "\"Square\": \"keys\" must be a list of key names")]
    [InlineData("'Square':{'lable':'Shoot'}", "\"Square\": unknown field \"lable\"")]
    [InlineData("'Square':['J']", "\"Square\": must be an object with \"keys\" and \"label\"")]
    [InlineData("'Square':{'label':'1234567890123456789012345'}", "\"Square\": label must be up to 24 characters on one line")]
    [InlineData("'Square':{'label':'Shoot\\nPass'}", "\"Square\": label must be up to 24 characters on one line")]
    [InlineData("'Square':{'keys':['J']},'square':{'keys':['K']}", "Control \"Square\" is listed twice")]
    public void Bad_controls_say_what_is_wrong(string controls, string message) =>
        Assert.Equal(message, ProfileFile.Load(WithControls(controls)).Error);

    [Fact]
    public void A_name_over_60_characters_is_refused() =>
        Assert.Equal("\"name\" must be 1-60 characters",
            ProfileFile.Load(J($"{{'format':1,'name':'{new string('x', 61)}','controls':{{}}}}")).Error);

    [Fact]
    public void Broken_json_names_the_line()
    {
        var error = ProfileFile.Load("{\n  \"format\": 1\n  \"name\": \"T\"\n}").Error;
        Assert.NotNull(error);
        Assert.StartsWith("Not a valid profile file (line 3): ", error);
        Assert.DoesNotContain("LineNumber", error);
    }

    [Fact]
    public void Save_writes_fields_and_controls_in_a_fixed_order()
    {
        var profile = new ControlProfile("Test", null, 5, false,
            new Dictionary<PadControl, IReadOnlyList<ushort>>
            {
                [PadControl.Square] = [VirtualKeys.Letter('J'), VirtualKeys.LButton],
                [PadControl.Cross] = [VirtualKeys.Letter('K')],
            },
            new Dictionary<PadControl, string> { [PadControl.Square] = "Shoot", [PadControl.Touchpad] = "Map" });

        const string expected = """
            {
              "format": 1,
              "name": "Test",
              "sensitivity": 5,
              "invertY": false,
              "controls": {
                "Cross": {
                  "keys": [
                    "K"
                  ]
                },
                "Square": {
                  "keys": [
                    "J",
                    "LeftClick"
                  ],
                  "label": "Shoot"
                },
                "Touchpad": {
                  "label": "Map"
                }
              }
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n") + "\n", ProfileFile.Save(profile));
    }

    [Fact]
    public void Save_keeps_accents_readable() =>
        Assert.Contains("\"name\": \"Café\"", ProfileFile.Save(new ControlProfile("Café", null, 5, false,
            new Dictionary<PadControl, IReadOnlyList<ushort>>(), new Dictionary<PadControl, string>())));

    [Fact]
    public void The_built_in_layout_round_trips()
    {
        var profile = new ControlProfile("Default", "Any", 7, true, KeyLayout.CreateDefault().Current,
            new Dictionary<PadControl, string>());
        ProfileAssert.Same(profile, Loaded(ProfileFile.Save(profile)));
    }

    [Fact]
    public void A_profile_with_labels_round_trips()
    {
        var profile = Loaded(SpecExample);
        ProfileAssert.Same(profile, Loaded(ProfileFile.Save(profile)));
    }

    [Fact]
    public void A_key_without_an_id_round_trips_as_hex()
    {
        var profile = new ControlProfile("OEM", null, 5, false,
            new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Cross] = [0xE2] },
            new Dictionary<PadControl, string>());
        var json = ProfileFile.Save(profile);
        Assert.Contains("\"0xE2\"", json);
        ProfileAssert.Same(profile, Loaded(json));
    }
}
