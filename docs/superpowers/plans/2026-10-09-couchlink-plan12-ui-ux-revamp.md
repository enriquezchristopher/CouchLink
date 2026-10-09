# UI/UX Revamp Implementation Plan (plan 12)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every CouchLink.App window the dark "game launcher" theme and the reworked host, join, session, controls and popup flows from the spec, without changing what the app does.

**Architecture:** A hand-built WPF theme (brush tokens, implicit and named styles, vector icons) is merged into the application resources in code by `ThemeManager`. Small reusable controls live in `Ui/`, display logic lives in plain `Presentation/` classes with unit tests, and every view and window is rewritten in XAML on top of them. Views keep their public events so `MainWindow`'s wiring barely moves.

**Tech Stack:** C# / .NET 10, WPF (XAML + code-behind), xUnit 2.9.3, PowerShell + UI Automation for screenshots.

**Spec:** [docs/superpowers/specs/2026-10-09-couchlink-ui-ux-revamp-design.md](../specs/2026-10-09-couchlink-ui-ux-revamp-design.md)

## Global Constraints

- No new NuGet packages in `src/`. The new test project uses the same package versions as `tests/CouchLink.Core.Tests`.
- `TreatWarningsAsErrors` is on (Directory.Build.props). Every task must build with zero warnings.
- Brushes are always referenced by key with `DynamicResource` (XAML) or `SetResourceReference` (C#). No hex colors outside `Theme/Tokens.xaml` and `Ui/PlayerColors.cs`.
- Sizes, styles, fonts and icons use `StaticResource`, except in `Diagnostics/CrashDialog.xaml`, which uses only `DynamicResource` so it still opens if the theme failed to load.
- Font: `Segoe UI Variable Text, Segoe UI` (`BodyFont`); stats and key caps: `Cascadia Mono, Consolas` (`MonoFont`). Body size 14.
- Main window 540×660, minimum 460×560.
- Behavior stays the same except: Stop hosting confirms when players are connected; the address box needs four dot-separated numbers.
- The Session screen's buttons and expander, and the header buttons while that screen shows, are `Focusable="False"` (Raw Input keys still reach a focused control).
- `ApprovalPopup` keeps `Topmost = true` and `ShowActivated = false`.
- Every interactive control has `AutomationProperties.AutomationId` and, when its content isn't plain text, `AutomationProperties.Name`.
- Commits use Conventional Commit types (see CONTRIBUTING.md) and carry **no** Claude co-author line.
- Never mention Sunshine anywhere.
- Run the app tests with `dotnet test tests/CouchLink.App.Tests` from the repo root; the whole suite with `dotnet test`. FFmpeg must be present (`./eng/get-ffmpeg.ps1`) for the Video tests.

## Review Focus

1. **Animations turned off in Windows.** The 120 ms hover fade and the 180 ms screen fade must be instant, which only works if `FastDuration` is in the application resources *before* `Controls.xaml` is merged. Expect `ThemeManager.Install` to set it first → Task 4 test checks the order and the zero duration.
2. **Typos in resource keys.** A misspelled `DynamicResource` fails silently (the control just has no color). Expect every key used anywhere in CouchLink.App to be defined in the theme → Task 4 `ResourceKeyTests` scans all `.xaml` and `.cs` files, and every later task reruns it.
3. **Enter on the Stop hosting confirm.** The host presses Enter out of habit; it must not disconnect everyone → Task 6 test: with `danger: true`, Cancel is the default button.
4. **Typing a partial address** such as `192.168` or `10.1`. Today .NET turns it into another address and the client tries to connect to it → Task 3 test: rejected with the usual message.
5. **A key that moves from a control with two keys.** "Circle has no key now" must not appear while Circle still has a key → Task 3 test, and Task 11 wires the count of keys left.

---

## File Structure

```
src/CouchLink.App/
  CouchLink.App.csproj          + InternalsVisibleTo CouchLink.App.Tests
  App.xaml.cs                   calls ThemeManager.Install
  MainWindow.xaml(.cs)          header + screen host, ThemedDialog, Motion
  Theme/
    Tokens.xaml                 dark brushes
    HighContrast.xaml           same keys from SystemColors
    Icons.xaml                  Geometry resources
    Controls.xaml               fonts, text styles, control styles/templates
    ThemeManager.cs             merges dictionaries, High Contrast switch
    WindowTheme.cs              window background/font + dark title bar
    Motion.cs                   enter animation
  Ui/
    ThemeProps.cs               attached props read by templates
    Glyph.cs                    24×24 stroke icon
    PlayerColors.cs             slot → color
    PlayerChip.cs  KeyCap.cs  StatusPill.cs  Banner.cs
    StepTracker.cs  Spinner.cs
    AppHeader.xaml(.cs)
    ThemedDialog.xaml(.cs)
  Presentation/
    AppInfo.cs  SessionText.cs  StopHostingPrompt.cs  AskCountdown.cs
    AddressInput.cs  ControlFilter.cs  BindMessage.cs
  Views/
    StartView.xaml(.cs)  HostLobbyView.xaml(.cs)
    JoinListView.xaml(.cs)  SessionView.xaml(.cs)
  ControlsWindow.xaml(.cs)      (was code-only ControlsWindow.cs)
  ApprovalPopup.xaml(.cs)       (was code-only ApprovalPopup.cs)
  SaveProfileDialog.xaml(.cs)   (was code-only SaveProfileDialog.cs)
  Diagnostics/CrashDialog.xaml(.cs) (was code-only CrashDialog.cs)
tests/CouchLink.App.Tests/
  CouchLink.App.Tests.csproj  Wpf.cs  RepoFiles.cs  and one test file per unit
eng/screenshots.ps1
docs/images/*.png  README.md  docs/cafe-setup-guide.md  docs/gate-results.md
```

---

### Task 1: App test project, player colors and app info

**Files:**
- Create: `tests/CouchLink.App.Tests/CouchLink.App.Tests.csproj`
- Create: `tests/CouchLink.App.Tests/Wpf.cs`
- Create: `tests/CouchLink.App.Tests/PlayerColorsTests.cs`, `tests/CouchLink.App.Tests/AppInfoTests.cs`
- Create: `src/CouchLink.App/Ui/PlayerColors.cs`, `src/CouchLink.App/Presentation/AppInfo.cs`
- Modify: `src/CouchLink.App/CouchLink.App.csproj`, `CouchLink.slnx`

**Interfaces:**
- Produces: `PlayerColors.ColorFor(byte slot) → Color?`, `PlayerColors.BrushFor(byte slot) → Brush` (frozen), `PlayerColors.TextOnPlayer → Brush` (#0F0F23, frozen); `AppInfo.Version → string` ("1.9.0"), `AppInfo.Format(Version?) → string`, `AppInfo.AboutText → string`; test helper `Wpf.Run(Action)` (runs on one shared STA thread that owns an `Application`).

- [ ] **Step 1: Create the test project**

`tests/CouchLink.App.Tests/CouchLink.App.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <PlatformTarget>x64</PlatformTarget>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CouchLink.App\CouchLink.App.csproj" />
  </ItemGroup>

</Project>
```

Add it to `CouchLink.slnx` inside the `/tests/` folder, after the Audio tests line:

```xml
    <Project Path="tests/CouchLink.App.Tests/CouchLink.App.Tests.csproj" />
```

Add to `src/CouchLink.App/CouchLink.App.csproj`, as a new `ItemGroup` before the profiles one:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="CouchLink.App.Tests" />
  </ItemGroup>
```

`tests/CouchLink.App.Tests/Wpf.cs`:

```csharp
using System.Windows;
using System.Windows.Threading;

namespace CouchLink.App.Tests;

/// <summary>
/// WPF objects need an STA thread, and the theme's pack URIs need an Application. Every WPF test runs
/// on this one thread, which owns one Application for the whole test run.
/// </summary>
internal static class Wpf
{
    private static readonly Lazy<Dispatcher> Ui = new(Start);

    public static void Run(Action action) => Ui.Value.Invoke(action);

    public static T Run<T>(Func<T> func) => Ui.Value.Invoke(func);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "WPF tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/CouchLink.App.Tests/PlayerColorsTests.cs`:

```csharp
using System.Windows.Media;
using CouchLink.App.Ui;

namespace CouchLink.App.Tests;

public class PlayerColorsTests
{
    [Theory]
    [InlineData(1, 0xC4, 0xB5, 0xFD)]
    [InlineData(2, 0xFD, 0xA4, 0xAF)]
    [InlineData(3, 0xFC, 0xD3, 0x4D)]
    [InlineData(10, 0xA5, 0xB4, 0xFC)]
    public void Slots_have_the_spec_colors(byte slot, byte r, byte g, byte b)
    {
        Assert.Equal(Color.FromRgb(r, g, b), PlayerColors.ColorFor(slot));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(255)]
    public void Slots_outside_1_to_10_have_no_color(byte slot)
    {
        Assert.Null(PlayerColors.ColorFor(slot));
        Assert.Equal(Color.FromRgb(0x27, 0x27, 0x3B), ((SolidColorBrush)PlayerColors.BrushFor(slot)).Color);
    }

    [Fact]
    public void All_ten_colors_differ()
    {
        var colors = Enumerable.Range(1, 10).Select(s => PlayerColors.ColorFor((byte)s)).ToList();
        Assert.Equal(10, colors.Distinct().Count());
    }

    [Fact]
    public void Dark_text_on_every_player_color_is_at_least_9_5_to_1()
    {
        var text = ((SolidColorBrush)PlayerColors.TextOnPlayer).Color;
        for (byte slot = 1; slot <= 10; slot++)
            Assert.True(Contrast(PlayerColors.ColorFor(slot)!.Value, text) >= 9.5, $"P{slot}");
    }

    [Fact]
    public void Brushes_are_frozen_so_any_thread_can_use_them()
    {
        Assert.True(PlayerColors.BrushFor(4).IsFrozen);
        Assert.True(PlayerColors.TextOnPlayer.IsFrozen);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c) =>
        0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(byte v)
    {
        double s = v / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
```

`tests/CouchLink.App.Tests/AppInfoTests.cs`:

```csharp
using CouchLink.App.Presentation;

namespace CouchLink.App.Tests;

public class AppInfoTests
{
    [Fact]
    public void Version_has_three_parts()
    {
        Assert.Equal("1.9.0", AppInfo.Format(new Version(1, 9, 0, 0)));
        Assert.Equal("?", AppInfo.Format(null));
    }

    [Fact]
    public void Version_comes_from_the_app_assembly()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
    }

    [Fact]
    public void About_text_has_version_and_license()
    {
        Assert.Contains($"Version {AppInfo.Version}", AppInfo.AboutText);
        Assert.Contains("GNU GPL v3", AppInfo.AboutText);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: build FAILS with `The type or namespace name 'Ui' does not exist in the namespace 'CouchLink.App'` and the same for `Presentation`.

- [ ] **Step 4: Write the implementation**

`src/CouchLink.App/Ui/PlayerColors.cs`:

```csharp
using System.Windows.Media;

namespace CouchLink.App.Ui;

/// <summary>
/// Each player's color, P1 (the host) to P10: the lobby rows, the join list and the session screen use
/// the same one, so a player can find themselves. Fixed identity colors, not theme tokens.
/// </summary>
internal static class PlayerColors
{
    private static readonly Color[] Slots =
    [
        Rgb(0xC4B5FD), Rgb(0xFDA4AF), Rgb(0xFCD34D), Rgb(0x6EE7B7), Rgb(0x7DD3FC),
        Rgb(0xF9A8D4), Rgb(0xBEF264), Rgb(0xFDBA74), Rgb(0x5EEAD4), Rgb(0xA5B4FC),
    ];

    private static readonly Brush[] Brushes = Slots.Select(Frozen).ToArray();
    private static readonly Brush Unknown = Frozen(Rgb(0x27273B));

    /// <summary>Dark text that reads on every player color (9.5:1 or better).</summary>
    public static Brush TextOnPlayer { get; } = Frozen(Rgb(0x0F0F23));

    public static Color? ColorFor(byte slot) => slot is >= 1 and <= 10 ? Slots[slot - 1] : null;

    public static Brush BrushFor(byte slot) => slot is >= 1 and <= 10 ? Brushes[slot - 1] : Unknown;

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
```

`src/CouchLink.App/Presentation/AppInfo.cs`:

```csharp
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
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS (11 tests).

- [ ] **Step 6: Commit**

```bash
git add CouchLink.slnx src/CouchLink.App/CouchLink.App.csproj src/CouchLink.App/Ui/PlayerColors.cs src/CouchLink.App/Presentation/AppInfo.cs tests/CouchLink.App.Tests
git commit -m "test(app): app test project with player colors and version"
```

---

### Task 2: Session text, Stop hosting prompt and approval countdown

**Files:**
- Create: `src/CouchLink.App/Presentation/SessionText.cs`, `StopHostingPrompt.cs`, `AskCountdown.cs`
- Test: `tests/CouchLink.App.Tests/SessionTextTests.cs`, `StopHostingPromptTests.cs`, `AskCountdownTests.cs`

**Interfaces:**
- Consumes: `CouchLink.Core.Session.ClientState { Connecting, Waiting, Playing, Reconnecting, Ended }`.
- Produces:
  - `readonly record struct SessionText(string Heading, string Hint, int Step, string LeaveText, bool Playing, bool Reconnecting)` with `static SessionText? For(ClientState state, string host, byte slot)` (null for `Ended`).
  - `static class StopHostingPrompt { static string? For(IReadOnlyList<string> names); }`
  - `readonly record struct AskCountdown(double Remaining, string Text)` with `static AskCountdown For(TimeSpan elapsed, TimeSpan timeout)`; `Remaining` is 1 → 0.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/SessionTextTests.cs`:

```csharp
using CouchLink.App.Presentation;
using CouchLink.Core.Session;

namespace CouchLink.App.Tests;

public class SessionTextTests
{
    [Fact]
    public void Connecting_is_step_one_and_cancels()
    {
        var text = SessionText.For(ClientState.Connecting, "PORTAL-SERVER", 0)!.Value;
        Assert.Equal("Connecting to PORTAL-SERVER…", text.Heading);
        Assert.Equal("", text.Hint);
        Assert.Equal(0, text.Step);
        Assert.Equal("Cancel", text.LeaveText);
        Assert.False(text.Playing);
        Assert.False(text.Reconnecting);
    }

    [Fact]
    public void Waiting_is_step_two_and_says_what_the_host_sees()
    {
        var text = SessionText.For(ClientState.Waiting, "PORTAL-SERVER", 0)!.Value;
        Assert.Equal("Waiting for PORTAL-SERVER to let you in", text.Heading);
        Assert.Equal("The host sees a popup and can allow or deny.", text.Hint);
        Assert.Equal(1, text.Step);
        Assert.Equal("Cancel", text.LeaveText);
    }

    [Fact]
    public void Playing_names_the_player_and_leaves()
    {
        var text = SessionText.For(ClientState.Playing, "PORTAL-SERVER", 3)!.Value;
        Assert.Equal("You're P3", text.Heading);
        Assert.Equal("Playing on PORTAL-SERVER", text.Hint);
        Assert.Equal(2, text.Step);
        Assert.Equal("Leave", text.LeaveText);
        Assert.True(text.Playing);
        Assert.False(text.Reconnecting);
    }

    [Fact]
    public void Reconnecting_keeps_step_three_and_says_the_slot_is_kept()
    {
        var text = SessionText.For(ClientState.Reconnecting, "PORTAL-SERVER", 3)!.Value;
        Assert.Equal("Reconnecting to PORTAL-SERVER…", text.Heading);
        Assert.Equal("Your slot is kept for a minute.", text.Hint);
        Assert.Equal(2, text.Step);
        Assert.Equal("Leave", text.LeaveText);
        Assert.True(text.Reconnecting);
        Assert.False(text.Playing);
    }

    [Fact]
    public void Ended_changes_nothing()
    {
        Assert.Null(SessionText.For(ClientState.Ended, "PORTAL-SERVER", 3));
    }
}
```

`tests/CouchLink.App.Tests/StopHostingPromptTests.cs`:

```csharp
using CouchLink.App.Presentation;

namespace CouchLink.App.Tests;

public class StopHostingPromptTests
{
    [Fact]
    public void No_players_needs_no_prompt() => Assert.Null(StopHostingPrompt.For([]));

    [Fact]
    public void One_player_is_named() =>
        Assert.Equal("PC-07 will be disconnected.", StopHostingPrompt.For(["PC-07"]));

    [Fact]
    public void Two_players_are_named() =>
        Assert.Equal("PC-07 and PC-11 will be disconnected.", StopHostingPrompt.For(["PC-07", "PC-11"]));

    [Fact]
    public void Three_players_are_named() =>
        Assert.Equal("PC-07, PC-11 and PC-12 will be disconnected.", StopHostingPrompt.For(["PC-07", "PC-11", "PC-12"]));

    [Fact]
    public void Four_or_more_are_counted() =>
        Assert.Equal("4 players will be disconnected.", StopHostingPrompt.For(["A", "B", "C", "D"]));
}
```

`tests/CouchLink.App.Tests/AskCountdownTests.cs`:

```csharp
using CouchLink.App.Presentation;

namespace CouchLink.App.Tests;

public class AskCountdownTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void At_the_start_the_bar_is_full()
    {
        var c = AskCountdown.For(TimeSpan.Zero, Timeout);
        Assert.Equal(1.0, c.Remaining, 3);
        Assert.Equal("Denied automatically in 30 s", c.Text);
    }

    [Fact]
    public void Halfway_the_bar_is_half()
    {
        var c = AskCountdown.For(TimeSpan.FromSeconds(15), Timeout);
        Assert.Equal(0.5, c.Remaining, 3);
        Assert.Equal("Denied automatically in 15 s", c.Text);
    }

    [Fact]
    public void Part_seconds_round_up()
    {
        Assert.Equal("Denied automatically in 22 s", AskCountdown.For(TimeSpan.FromSeconds(8.4), Timeout).Text);
    }

    [Fact]
    public void Past_the_timeout_it_stops_at_zero()
    {
        var c = AskCountdown.For(TimeSpan.FromSeconds(31), Timeout);
        Assert.Equal(0.0, c.Remaining, 3);
        Assert.Equal("Denied automatically in 0 s", c.Text);
    }

    [Fact]
    public void A_zero_timeout_does_not_divide_by_zero()
    {
        Assert.Equal(0.0, AskCountdown.For(TimeSpan.Zero, TimeSpan.Zero).Remaining, 3);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: build FAILS: `The name 'SessionText' does not exist in the current context` (and `StopHostingPrompt`, `AskCountdown`).

- [ ] **Step 3: Write the implementation**

`src/CouchLink.App/Presentation/SessionText.cs`:

```csharp
using CouchLink.Core.Session;

namespace CouchLink.App.Presentation;

/// <summary>
/// What the Session screen says in each client state. <see cref="Step"/> is the StepTracker's current
/// step: 0 Connect, 1 Host lets you in, 2 Play.
/// </summary>
internal readonly record struct SessionText(string Heading, string Hint, int Step, string LeaveText, bool Playing, bool Reconnecting)
{
    /// <summary>Null for <see cref="ClientState.Ended"/>: the screen is about to go, so it keeps what it shows.</summary>
    public static SessionText? For(ClientState state, string host, byte slot) => state switch
    {
        ClientState.Connecting => new($"Connecting to {host}…", "", 0, "Cancel", false, false),
        ClientState.Waiting => new($"Waiting for {host} to let you in", "The host sees a popup and can allow or deny.", 1, "Cancel", false, false),
        ClientState.Playing => new($"You're P{slot}", $"Playing on {host}", 2, "Leave", true, false),
        ClientState.Reconnecting => new($"Reconnecting to {host}…", "Your slot is kept for a minute.", 2, "Leave", false, true),
        _ => null,
    };
}
```

`src/CouchLink.App/Presentation/StopHostingPrompt.cs`:

```csharp
namespace CouchLink.App.Presentation;

/// <summary>Who Stop hosting would disconnect, for the confirm dialog. Null: nobody, stop at once.</summary>
internal static class StopHostingPrompt
{
    public static string? For(IReadOnlyList<string> names) => names.Count switch
    {
        0 => null,
        1 => $"{names[0]} will be disconnected.",
        2 => $"{names[0]} and {names[1]} will be disconnected.",
        3 => $"{names[0]}, {names[1]} and {names[2]} will be disconnected.",
        _ => $"{names.Count} players will be disconnected.",
    };
}
```

`src/CouchLink.App/Presentation/AskCountdown.cs`:

```csharp
namespace CouchLink.App.Presentation;

/// <summary>The approval toast's countdown: how much of the bar is left (1 to 0) and its text.</summary>
internal readonly record struct AskCountdown(double Remaining, string Text)
{
    public static AskCountdown For(TimeSpan elapsed, TimeSpan timeout)
    {
        var left = timeout - elapsed;
        if (left < TimeSpan.Zero)
            left = TimeSpan.Zero;
        double remaining = timeout <= TimeSpan.Zero ? 0 : left / timeout;
        int seconds = (int)Math.Ceiling(left.TotalSeconds);
        return new(remaining, $"Denied automatically in {seconds} s");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.App/Presentation tests/CouchLink.App.Tests
git commit -m "feat(app): session text, stop-hosting prompt and approval countdown"
```

---

### Task 3: Address input, control filter and bind messages

**Files:**
- Create: `src/CouchLink.App/Presentation/AddressInput.cs`, `ControlFilter.cs`, `BindMessage.cs`
- Test: `tests/CouchLink.App.Tests/AddressInputTests.cs`, `ControlFilterTests.cs`, `BindMessageTests.cs`

**Interfaces:**
- Consumes: `CouchLink.Core.Input.KeyNames.Of(ushort)`, `KeyNames.Of(PadControl)`, `PadControl`.
- Produces:
  - `static class AddressInput { const string Error = "Enter an IP address like 192.168.1.23."; static bool TryParse(string? text, out IPAddress? address, out string? error); }` (`[NotNullWhen]` on both outs).
  - `static class ControlFilter { static string RowName(PadControl); static bool Matches(PadControl control, string group, string? label, string? query); }`
  - `static class BindMessage { static string Reserved(ushort key); static string? Moved(ushort key, PadControl? movedFrom, int keysLeftOnMovedFrom); }`

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/AddressInputTests.cs`:

```csharp
using System.Net;
using CouchLink.App.Presentation;

namespace CouchLink.App.Tests;

public class AddressInputTests
{
    [Theory]
    [InlineData("192.168.1.23", "192.168.1.23")]
    [InlineData("  10.0.0.5 ", "10.0.0.5")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    public void Four_part_IPv4_addresses_are_accepted(string text, string expected)
    {
        Assert.True(AddressInput.TryParse(text, out var address, out var error));
        Assert.Equal(IPAddress.Parse(expected), address);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("pc-07")]
    [InlineData("192.168")]
    [InlineData("10.1")]
    [InlineData("1")]
    [InlineData("192.168.1.256")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    public void Anything_else_is_rejected_with_the_usual_message(string? text)
    {
        Assert.False(AddressInput.TryParse(text, out var address, out var error));
        Assert.Null(address);
        Assert.Equal("Enter an IP address like 192.168.1.23.", error);
    }
}
```

`tests/CouchLink.App.Tests/ControlFilterTests.cs`:

```csharp
using CouchLink.App.Presentation;
using CouchLink.Core.Input;

namespace CouchLink.App.Tests;

public class ControlFilterTests
{
    [Theory]
    [InlineData(PadControl.LeftUp, "Up")]
    [InlineData(PadControl.RightLeft, "Left")]
    [InlineData(PadControl.DpadDown, "Down")]
    [InlineData(PadControl.DpadRight, "Right")]
    [InlineData(PadControl.Square, "Square")]
    [InlineData(PadControl.L2, "L2")]
    public void Rows_inside_a_stick_or_dpad_group_show_only_the_direction(PadControl control, string name)
    {
        Assert.Equal(name, ControlFilter.RowName(control));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_query_shows_everything(string? query)
    {
        Assert.True(ControlFilter.Matches(PadControl.Cross, "Buttons", null, query));
    }

    [Fact]
    public void Matches_the_full_control_name_ignoring_case()
    {
        Assert.True(ControlFilter.Matches(PadControl.LeftUp, "Left stick", null, "STICK UP"));
    }

    [Fact]
    public void Matches_the_group_name()
    {
        Assert.True(ControlFilter.Matches(PadControl.L1, "Shoulders", null, "shoulder"));
    }

    [Fact]
    public void Matches_the_label()
    {
        Assert.True(ControlFilter.Matches(PadControl.Square, "Buttons", "Shoot / Steal", "shoot"));
    }

    [Fact]
    public void A_query_that_matches_nothing_hides_the_row()
    {
        Assert.False(ControlFilter.Matches(PadControl.Square, "Buttons", "Shoot", "sprint"));
    }

    [Fact]
    public void Surrounding_spaces_in_the_query_are_ignored()
    {
        Assert.True(ControlFilter.Matches(PadControl.Cross, "Buttons", null, "  cross "));
    }
}
```

`tests/CouchLink.App.Tests/BindMessageTests.cs`:

```csharp
using CouchLink.App.Presentation;
using CouchLink.Core.Input;

namespace CouchLink.App.Tests;

public class BindMessageTests
{
    private const ushort Num5 = 0x65, Esc = 0x1B;

    [Fact]
    public void Reserved_names_the_key() =>
        Assert.Equal("Esc is reserved. Press another key.", BindMessage.Reserved(Esc));

    [Fact]
    public void Nothing_moved_says_nothing() => Assert.Null(BindMessage.Moved(Num5, null, 0));

    [Fact]
    public void A_control_left_without_keys_is_called_out() =>
        Assert.Equal("Num 5 moved here from Circle. Circle has no key now.", BindMessage.Moved(Num5, PadControl.Circle, 0));

    [Fact]
    public void A_control_with_another_key_left_is_not_called_out() =>
        Assert.Equal("Num 5 moved here from Circle.", BindMessage.Moved(Num5, PadControl.Circle, 1));

    [Fact]
    public void Stick_controls_use_their_full_name() =>
        Assert.Equal("Num 5 moved here from D-pad up. D-pad up has no key now.", BindMessage.Moved(Num5, PadControl.DpadUp, 0));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: build FAILS: `The name 'AddressInput' does not exist in the current context` (and `ControlFilter`, `BindMessage`).

- [ ] **Step 3: Write the implementation**

`src/CouchLink.App/Presentation/AddressInput.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace CouchLink.App.Presentation;

/// <summary>
/// The Join by address box. Only a full IPv4 address: .NET reads "192.168" as 192.0.0.168, which
/// would send the player to a PC they never meant.
/// </summary>
internal static class AddressInput
{
    public const string Error = "Enter an IP address like 192.168.1.23.";

    public static bool TryParse(string? text, [NotNullWhen(true)] out IPAddress? address, [NotNullWhen(false)] out string? error)
    {
        string trimmed = (text ?? "").Trim();
        if (trimmed.Split('.').Length == 4
            && IPAddress.TryParse(trimmed, out var parsed)
            && parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            address = parsed;
            error = null;
            return true;
        }
        address = null;
        error = Error;
        return false;
    }
}
```

`src/CouchLink.App/Presentation/ControlFilter.cs`:

```csharp
using CouchLink.Core.Input;

namespace CouchLink.App.Presentation;

/// <summary>The controls editor's row names and its Find box.</summary>
internal static class ControlFilter
{
    /// <summary>Under a "Left stick" or "D-pad" heading a row only needs the direction.</summary>
    public static string RowName(PadControl control) => control switch
    {
        PadControl.LeftUp or PadControl.RightUp or PadControl.DpadUp => "Up",
        PadControl.LeftDown or PadControl.RightDown or PadControl.DpadDown => "Down",
        PadControl.LeftLeft or PadControl.RightLeft or PadControl.DpadLeft => "Left",
        PadControl.LeftRight or PadControl.RightRight or PadControl.DpadRight => "Right",
        _ => KeyNames.Of(control),
    };

    /// <summary>True when the query is empty or found in the control's full name, its group or its label.</summary>
    public static bool Matches(PadControl control, string group, string? label, string? query)
    {
        string q = (query ?? "").Trim();
        if (q.Length == 0)
            return true;
        return Has(KeyNames.Of(control)) || Has(group) || (label is not null && Has(label));

        bool Has(string text) => text.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}
```

`src/CouchLink.App/Presentation/BindMessage.cs`:

```csharp
using CouchLink.Core.Input;

namespace CouchLink.App.Presentation;

/// <summary>What the controls editor's banner says after a key is pressed while a row listens.</summary>
internal static class BindMessage
{
    public static string Reserved(ushort key) => $"{KeyNames.Of(key)} is reserved. Press another key.";

    /// <summary>Null when the key wasn't on another control. "Has no key now" only when that control has none left.</summary>
    public static string? Moved(ushort key, PadControl? movedFrom, int keysLeftOnMovedFrom)
    {
        if (movedFrom is not { } from)
            return null;
        string moved = $"{KeyNames.Of(key)} moved here from {KeyNames.Of(from)}.";
        return keysLeftOnMovedFrom == 0 ? $"{moved} {KeyNames.Of(from)} has no key now." : moved;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.App/Presentation tests/CouchLink.App.Tests
git commit -m "feat(app): address check, controls filter and key-move messages"
```

---

### Task 4: Theme tokens, icons and the theme manager

**Files:**
- Create: `src/CouchLink.App/Theme/Tokens.xaml`, `HighContrast.xaml`, `Icons.xaml`, `ThemeManager.cs`, `WindowTheme.cs`, `Motion.cs`
- Create: `src/CouchLink.App/Ui/ThemeProps.cs`, `src/CouchLink.App/Ui/Glyph.cs`
- Create (temporary, replaced in Task 5): `src/CouchLink.App/Theme/Controls.xaml`
- Modify: `src/CouchLink.App/App.xaml.cs` (`OnStartup`)
- Test: `tests/CouchLink.App.Tests/RepoFiles.cs`, `ThemeManagerTests.cs`, `ResourceKeyTests.cs`

**Interfaces:**
- Produces:
  - Brush keys (Tokens.xaml and HighContrast.xaml, identical sets): `ChromeBrush BackgroundBrush InputBrush CardBrush RaisedBrush BorderBrush BorderStrongBrush TextBrush TextMutedBrush TextHintBrush OnPrimaryBrush PrimaryBrush PrimaryGradientBrush PrimaryTextBrush ListeningFillBrush LogoBrush HoverOverlayBrush HeroTileBrush DangerBrush DangerFillBrush WarningBrush SuccessBrush InfoBannerFillBrush InfoBannerBorderBrush InfoBannerTextBrush WarningBannerFillBrush WarningBannerBorderBrush WarningBannerTextBrush ErrorBannerFillBrush ErrorBannerBorderBrush ErrorBannerTextBrush LivePillFillBrush ReconnectingPillFillBrush`.
  - Icon keys (Icons.xaml, `Geometry` on a 24×24 grid): `IconMonitor IconArrowRight IconArrowLeft IconGamepad IconHelp IconInfo IconAlert IconCheck IconChevronDown IconChevronRight IconClose IconSearch IconUser`.
  - `ThemeManager.Install(Application)`, `ThemeManager.TryInstallInto(FrameworkElement) → bool`, `ThemeManager.AnimationsOn → bool`; resource key `FastDuration` (`Duration`).
  - `WindowTheme.Apply(Window)`.
  - `Motion.Enter(UIElement)`.
  - `ThemeProps` attached properties: `CornerRadius` (CornerRadius, default 8), `Icon` (Geometry), `Description` (string), `Placeholder` (string).
  - `Glyph : Viewbox` with DPs `Data` (Geometry) and `Brush` (Brush), default 18×18.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/RepoFiles.cs`:

```csharp
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
```

`tests/CouchLink.App.Tests/ThemeManagerTests.cs`:

```csharp
using System.Collections;
using System.Windows;
using System.Windows.Media;
using CouchLink.App.Theme;

namespace CouchLink.App.Tests;

public class ThemeManagerTests
{
    private static ResourceDictionary Load(string file) =>
        Wpf.Run(() => new ResourceDictionary { Source = new Uri($"pack://application:,,,/CouchLink.App;component/Theme/{file}") });

    private static HashSet<string> Keys(ResourceDictionary dictionary) =>
        Wpf.Run(() => dictionary.Keys.OfType<string>().ToHashSet());

    [Fact]
    public void High_contrast_defines_every_dark_brush_and_nothing_else()
    {
        var dark = Keys(Load("Tokens.xaml"));
        var contrast = Keys(Load("HighContrast.xaml"));
        Assert.NotEmpty(dark);
        Assert.Equal(dark.Order(), contrast.Order());
    }

    [Theory]
    [InlineData("Tokens.xaml")]
    [InlineData("HighContrast.xaml")]
    public void Every_palette_entry_is_a_brush(string file)
    {
        var dictionary = Load(file);
        Wpf.Run(() =>
        {
            foreach (DictionaryEntry entry in dictionary)
                Assert.True(entry.Value is Brush, $"{file}: {entry.Key} is {entry.Value?.GetType().Name}");
        });
    }

    [Fact]
    public void Install_puts_the_palette_icons_and_durations_in_the_app()
    {
        Wpf.Run(() =>
        {
            var app = Application.Current;
            ThemeManager.Install(app);
            ThemeManager.Install(app); // a second call changes nothing
            Assert.Equal(2, app.Resources.MergedDictionaries.Count);
            Assert.IsType<Duration>(app.Resources["FastDuration"]);
            Assert.IsAssignableFrom<Geometry>(app.FindResource("IconMonitor"));
            if (!SystemParameters.HighContrast)
                Assert.Equal(Color.FromRgb(0x7C, 0x3A, 0xED), ((SolidColorBrush)app.FindResource("PrimaryBrush")).Color);
        });
    }

    [Fact]
    public void Durations_are_zero_when_windows_animations_are_off()
    {
        Assert.Equal(TimeSpan.Zero, ThemeManager.FastDurationFor(animationsOn: false).TimeSpan);
        Assert.Equal(TimeSpan.FromMilliseconds(120), ThemeManager.FastDurationFor(animationsOn: true).TimeSpan);
    }

    [Fact]
    public void A_window_on_another_thread_can_get_its_own_copy_of_the_theme()
    {
        Wpf.Run(() =>
        {
            var element = new FrameworkElement();
            Assert.True(ThemeManager.TryInstallInto(element));
            Assert.True(element.Resources.Contains("FastDuration"));
            Assert.NotNull(element.TryFindResource("CardBrush"));
        });
    }
}
```

`tests/CouchLink.App.Tests/ResourceKeyTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CouchLink.App.Tests;

/// <summary>
/// A misspelled DynamicResource fails silently: the control just has no color. Every key used anywhere
/// in CouchLink.App must be defined in Theme/*.xaml (or be FastDuration, which ThemeManager sets).
/// </summary>
public partial class ResourceKeyTests
{
    [Fact]
    public void Every_resource_key_used_in_the_app_is_defined_by_the_theme()
    {
        var defined = Directory.GetFiles(Path.Combine(RepoFiles.AppSource, "Theme"), "*.xaml")
            .SelectMany(f => DefinedKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Append("FastDuration")
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: build FAILS: `The type or namespace name 'Theme' does not exist in the namespace 'CouchLink.App'`.

- [ ] **Step 3: Write the palette**

`src/CouchLink.App/Theme/Tokens.xaml`:

```xml
<!-- The dark palette (spec 3.1). HighContrast.xaml has the same keys; ThemeManager swaps them. -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="ChromeBrush" Color="#0B0B1A"/>
    <SolidColorBrush x:Key="BackgroundBrush" Color="#0F0F23"/>
    <SolidColorBrush x:Key="InputBrush" Color="#16152B"/>
    <SolidColorBrush x:Key="CardBrush" Color="#1E1C35"/>
    <SolidColorBrush x:Key="RaisedBrush" Color="#27273B"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#2E2B4F"/>
    <SolidColorBrush x:Key="BorderStrongBrush" Color="#3B3858"/>
    <SolidColorBrush x:Key="TextBrush" Color="#E2E8F0"/>
    <SolidColorBrush x:Key="TextMutedBrush" Color="#94A3B8"/>
    <SolidColorBrush x:Key="TextHintBrush" Color="#64748B"/>
    <SolidColorBrush x:Key="OnPrimaryBrush" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="PrimaryBrush" Color="#7C3AED"/>
    <LinearGradientBrush x:Key="PrimaryGradientBrush" StartPoint="0,0" EndPoint="1,1">
        <GradientStop Color="#7C3AED" Offset="0"/>
        <GradientStop Color="#5B21B6" Offset="1"/>
    </LinearGradientBrush>
    <SolidColorBrush x:Key="PrimaryTextBrush" Color="#A78BFA"/>
    <SolidColorBrush x:Key="ListeningFillBrush" Color="#2A1D4F"/>
    <LinearGradientBrush x:Key="LogoBrush" StartPoint="0,0" EndPoint="1,1">
        <GradientStop Color="#7C3AED" Offset="0"/>
        <GradientStop Color="#F43F5E" Offset="1"/>
    </LinearGradientBrush>
    <SolidColorBrush x:Key="HoverOverlayBrush" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="HeroTileBrush" Color="#1FFFFFFF"/>
    <SolidColorBrush x:Key="DangerBrush" Color="#FB7185"/>
    <SolidColorBrush x:Key="DangerFillBrush" Color="#F43F5E"/>
    <SolidColorBrush x:Key="WarningBrush" Color="#FBBF24"/>
    <SolidColorBrush x:Key="SuccessBrush" Color="#4ADE80"/>
    <SolidColorBrush x:Key="InfoBannerFillBrush" Color="#1E1C35"/>
    <SolidColorBrush x:Key="InfoBannerBorderBrush" Color="#2E2B4F"/>
    <SolidColorBrush x:Key="InfoBannerTextBrush" Color="#E2E8F0"/>
    <SolidColorBrush x:Key="WarningBannerFillBrush" Color="#2A1F05"/>
    <SolidColorBrush x:Key="WarningBannerBorderBrush" Color="#6B4F0A"/>
    <SolidColorBrush x:Key="WarningBannerTextBrush" Color="#FDE68A"/>
    <SolidColorBrush x:Key="ErrorBannerFillBrush" Color="#2A0F18"/>
    <SolidColorBrush x:Key="ErrorBannerBorderBrush" Color="#7F1D35"/>
    <SolidColorBrush x:Key="ErrorBannerTextBrush" Color="#FECDD3"/>
    <SolidColorBrush x:Key="LivePillFillBrush" Color="#14532D"/>
    <SolidColorBrush x:Key="ReconnectingPillFillBrush" Color="#422006"/>
</ResourceDictionary>
```

`src/CouchLink.App/Theme/HighContrast.xaml` (x:Static reads the system colors when ThemeManager loads the file, which it does again on every High Contrast change):

```xml
<!-- Windows High Contrast (spec 3.6): every Tokens.xaml key, from the user's system colors. -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <x:Static x:Key="ChromeBrush" Member="SystemColors.WindowBrush"/>
    <x:Static x:Key="BackgroundBrush" Member="SystemColors.WindowBrush"/>
    <x:Static x:Key="InputBrush" Member="SystemColors.WindowBrush"/>
    <x:Static x:Key="CardBrush" Member="SystemColors.WindowBrush"/>
    <x:Static x:Key="RaisedBrush" Member="SystemColors.WindowBrush"/>
    <x:Static x:Key="BorderBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="BorderStrongBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="TextBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="TextMutedBrush" Member="SystemColors.GrayTextBrush"/>
    <x:Static x:Key="TextHintBrush" Member="SystemColors.GrayTextBrush"/>
    <x:Static x:Key="OnPrimaryBrush" Member="SystemColors.HighlightTextBrush"/>
    <x:Static x:Key="PrimaryBrush" Member="SystemColors.HighlightBrush"/>
    <x:Static x:Key="PrimaryGradientBrush" Member="SystemColors.HighlightBrush"/>
    <x:Static x:Key="PrimaryTextBrush" Member="SystemColors.HotTrackBrush"/>
    <x:Static x:Key="ListeningFillBrush" Member="SystemColors.HighlightBrush"/>
    <x:Static x:Key="LogoBrush" Member="SystemColors.HighlightBrush"/>
    <x:Static x:Key="HoverOverlayBrush" Member="SystemColors.HighlightBrush"/>
    <x:Static x:Key="HeroTileBrush" Member="SystemColors.WindowBrush"/>
    <x:Static x:Key="DangerBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="DangerFillBrush" Member="SystemColors.HighlightBrush"/>
    <x:Static x:Key="WarningBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="SuccessBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="InfoBannerFillBrush" Member="SystemColors.WindowBrush"/>
    <x:Static x:Key="InfoBannerBorderBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="InfoBannerTextBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="WarningBannerFillBrush" Member="SystemColors.WindowBrush"/>
    <x:Static x:Key="WarningBannerBorderBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="WarningBannerTextBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="ErrorBannerFillBrush" Member="SystemColors.WindowBrush"/>
    <x:Static x:Key="ErrorBannerBorderBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="ErrorBannerTextBrush" Member="SystemColors.WindowTextBrush"/>
    <x:Static x:Key="LivePillFillBrush" Member="SystemColors.WindowBrush"/>
    <x:Static x:Key="ReconnectingPillFillBrush" Member="SystemColors.WindowBrush"/>
</ResourceDictionary>
```

- [ ] **Step 4: Write the icons**

`src/CouchLink.App/Theme/Icons.xaml` (stroke paths on a 24×24 grid; `Glyph` draws them with a 2 px round stroke; "H12.01" segments draw as dots):

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Geometry x:Key="IconMonitor">M4,4 H20 A2,2 0 0 1 22,6 V15 A2,2 0 0 1 20,17 H4 A2,2 0 0 1 2,15 V6 A2,2 0 0 1 4,4 Z M8,21 H16 M12,17 V21</Geometry>
    <Geometry x:Key="IconArrowRight">M5,12 H19 M13,6 L19,12 L13,18</Geometry>
    <Geometry x:Key="IconArrowLeft">M19,12 H5 M11,6 L5,12 L11,18</Geometry>
    <Geometry x:Key="IconGamepad">M6,11 H10 M8,9 V13 M15,12 H15.01 M18,10 H18.01 M17.32,5 H6.68 A4,4 0 0 0 2.7,8.6 L2.02,14.6 A3,3 0 0 0 5,18 C6,18 6.5,17.5 7,17 L8.41,15.59 A2,2 0 0 1 9.83,15 H14.17 A2,2 0 0 1 15.59,15.59 L17,17 C17.5,17.5 18,18 19,18 A3,3 0 0 0 21.98,14.6 L21.3,8.6 A4,4 0 0 0 17.32,5 Z</Geometry>
    <Geometry x:Key="IconHelp">M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 Z M9.1,9 A3,3 0 0 1 14.9,10 C14.9,12 12,12.5 12,14.5 M12,17.5 H12.01</Geometry>
    <Geometry x:Key="IconInfo">M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 Z M12,16 V12 M12,8 H12.01</Geometry>
    <Geometry x:Key="IconAlert">M10.3,3.9 L1.8,18 A2,2 0 0 0 3.5,21 H20.5 A2,2 0 0 0 22.2,18 L13.7,3.9 A2,2 0 0 0 10.3,3.9 Z M12,9 V13 M12,17 H12.01</Geometry>
    <Geometry x:Key="IconCheck">M20,6 L9,17 L4,12</Geometry>
    <Geometry x:Key="IconChevronDown">M6,9 L12,15 L18,9</Geometry>
    <Geometry x:Key="IconChevronRight">M9,6 L15,12 L9,18</Geometry>
    <Geometry x:Key="IconClose">M18,6 L6,18 M6,6 L18,18</Geometry>
    <Geometry x:Key="IconSearch">M3,11 A8,8 0 1 0 19,11 A8,8 0 1 0 3,11 Z M21,21 L16.65,16.65</Geometry>
    <Geometry x:Key="IconUser">M20,21 V19 A4,4 0 0 0 16,15 H8 A4,4 0 0 0 4,19 V21 M8,7 A4,4 0 1 0 16,7 A4,4 0 1 0 8,7 Z</Geometry>
</ResourceDictionary>
```

- [ ] **Step 5: Write ThemeProps and Glyph**

`src/CouchLink.App/Ui/ThemeProps.cs`:

```csharp
using System.Windows;
using System.Windows.Media;

namespace CouchLink.App.Ui;

/// <summary>Attached properties the theme's templates read: corner radius, a hero button's icon and description, a text box's placeholder.</summary>
internal static class ThemeProps
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(ThemeProps), new FrameworkPropertyMetadata(new CornerRadius(8)));

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(ThemeProps), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.RegisterAttached(
        "Description", typeof(string), typeof(ThemeProps), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(ThemeProps), new FrameworkPropertyMetadata(null));

    public static CornerRadius GetCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject o, CornerRadius value) => o.SetValue(CornerRadiusProperty, value);
    public static Geometry? GetIcon(DependencyObject o) => (Geometry?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, Geometry? value) => o.SetValue(IconProperty, value);
    public static string? GetDescription(DependencyObject o) => (string?)o.GetValue(DescriptionProperty);
    public static void SetDescription(DependencyObject o, string? value) => o.SetValue(DescriptionProperty, value);
    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? value) => o.SetValue(PlaceholderProperty, value);
}
```

`src/CouchLink.App/Ui/Glyph.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CouchLink.App.Ui;

/// <summary>A stroke icon from Theme/Icons.xaml, drawn on a 24×24 grid and scaled to Width × Height (18 by default).</summary>
internal sealed class Glyph : Viewbox
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Glyph),
        new PropertyMetadata(null, (d, e) => ((Glyph)d)._path.Data = (Geometry?)e.NewValue));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(Glyph),
        new PropertyMetadata(null, (d, e) => ((Glyph)d).UseBrush((Brush?)e.NewValue)));

    private readonly Path _path = new()
    {
        StrokeThickness = 2,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    public Glyph()
    {
        Width = 18;
        Height = 18;
        Stretch = Stretch.Uniform;
        Focusable = false;
        IsHitTestVisible = false;
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(_path);
        Child = canvas;
        _path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    private void UseBrush(Brush? brush)
    {
        if (brush is null)
            _path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
        else
            _path.Stroke = brush;
    }
}
```

- [ ] **Step 6: Write a placeholder Controls.xaml** (Task 5 replaces it; ThemeManager needs the file to exist)

`src/CouchLink.App/Theme/Controls.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Icons.xaml"/>
    </ResourceDictionary.MergedDictionaries>
</ResourceDictionary>
```

- [ ] **Step 7: Write ThemeManager, WindowTheme and Motion**

`src/CouchLink.App/Theme/ThemeManager.cs`:

```csharp
using System.Windows;

namespace CouchLink.App.Theme;

/// <summary>
/// Puts the theme into the app's resources: the palette (dark, or High Contrast system colors, and
/// swapped when Windows switches) and Controls.xaml (styles, which merges Icons.xaml).
/// FastDuration goes in first because Controls.xaml's templates read it with StaticResource.
/// </summary>
internal static class ThemeManager
{
    private static readonly Uri TokensUri = new("pack://application:,,,/CouchLink.App;component/Theme/Tokens.xaml");
    private static readonly Uri HighContrastUri = new("pack://application:,,,/CouchLink.App;component/Theme/HighContrast.xaml");
    private static readonly Uri ControlsUri = new("pack://application:,,,/CouchLink.App;component/Theme/Controls.xaml");
    private static bool _installed;

    /// <summary>Windows "Show animations in Windows". Off: hovers and screen changes are instant.</summary>
    public static bool AnimationsOn => SystemParameters.ClientAreaAnimation;

    public static Duration FastDurationFor(bool animationsOn) =>
        new(animationsOn ? TimeSpan.FromMilliseconds(120) : TimeSpan.Zero);

    public static void Install(Application app)
    {
        if (_installed)
            return;
        _installed = true;
        Fill(app.Resources);
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
                app.Dispatcher.InvokeAsync(() => app.Resources.MergedDictionaries[0] = Palette());
        };
    }

    /// <summary>
    /// For a window on its own thread (the crash dialog): its own copy of the theme, so it never
    /// reaches into the app's resources across threads. False, and the stock look, if anything fails.
    /// </summary>
    public static bool TryInstallInto(FrameworkElement element)
    {
        try
        {
            Fill(element.Resources);
            return true;
        }
        catch
        {
            element.Resources.MergedDictionaries.Clear();
            return false;
        }
    }

    private static void Fill(ResourceDictionary resources)
    {
        resources["FastDuration"] = FastDurationFor(AnimationsOn);
        resources.MergedDictionaries.Add(Palette());
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = ControlsUri });
    }

    private static ResourceDictionary Palette() =>
        new() { Source = SystemParameters.HighContrast ? HighContrastUri : TokensUri };
}
```

`src/CouchLink.App/Theme/WindowTheme.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace CouchLink.App.Theme;

/// <summary>
/// What every window needs from the theme: background, text color, font, and a dark native title bar
/// (DWM; Windows 10 20H1 and later, with the caption color on Windows 11). Call from the constructor.
/// </summary>
internal static partial class WindowTheme
{
    private const int DarkModeBefore20H1 = 19, DarkMode = 20, CaptionColor = 35;
    private const int ChromeColorRef = 0x001A0B0B; // #0B0B1A as COLORREF (0x00BBGGRR)

    public static void Apply(Window window)
    {
        window.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        window.SetResourceReference(Control.FontFamilyProperty, "BodyFont");
        window.FontSize = 14;
        window.UseLayoutRounding = true;
        window.SourceInitialized += (_, _) => DarkTitleBar(new WindowInteropHelper(window).Handle);
    }

    private static void DarkTitleBar(nint hwnd)
    {
        if (SystemParameters.HighContrast)
            return; // Windows draws High Contrast title bars itself
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, DarkMode, ref on, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(hwnd, DarkModeBefore20H1, ref on, sizeof(int));
        int color = ChromeColorRef;
        _ = DwmSetWindowAttribute(hwnd, CaptionColor, ref color, sizeof(int)); // fails quietly before Windows 11
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
```

`src/CouchLink.App/Theme/Motion.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CouchLink.App.Theme;

/// <summary>The one enter animation: fade in and slide up 8 px in 180 ms. Nothing when Windows animations are off.</summary>
internal static class Motion
{
    private static readonly Duration Enter180 = new(TimeSpan.FromMilliseconds(180));

    public static void Enter(UIElement element)
    {
        if (!ThemeManager.AnimationsOn)
            return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform(0, 8);
        element.RenderTransform = slide;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Enter180) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, Enter180) { EasingFunction = ease });
    }
}
```

- [ ] **Step 8: Install the theme at startup**

In `src/CouchLink.App/App.xaml.cs`, add `using CouchLink.App.Theme;` and call `ThemeManager.Install(this);` right after `CrashHandler.Install(this);`:

```csharp
    protected override void OnStartup(StartupEventArgs e)
    {
        CrashHandler.Install(this); // first, so even startup crashes are reported
        ThemeManager.Install(this); // before any window, so every window gets the theme
        base.OnStartup(e);
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS. If `High_contrast_defines_every_dark_brush_and_nothing_else` fails with a XAML parse error on `x:Static`, the element syntax is not accepted; replace each line with `<StaticResource x:Key="…" ResourceKey="{x:Static SystemColors.WindowBrushKey}"/>` (the `…BrushKey` form of the same member) and rerun.

- [ ] **Step 10: Commit**

```bash
git add src/CouchLink.App/Theme src/CouchLink.App/Ui src/CouchLink.App/App.xaml.cs tests/CouchLink.App.Tests
git commit -m "feat(app): theme palette, icons and theme manager"
```

---

### Task 5: Control styles

**Files:**
- Modify (replace): `src/CouchLink.App/Theme/Controls.xaml`
- Test: `tests/CouchLink.App.Tests/ThemeSmokeTests.cs`

**Interfaces:**
- Consumes: Task 4 brush and icon keys, `FastDuration`, `ThemeProps`, `Glyph`.
- Produces (keys): `BodyFont MonoFont FocusRing ButtonTemplate HeroTemplate` · text styles `DisplayText TitleText SubtitleText BodyMutedText CaptionText OverlineText MonoText` · `Card` (Border) · `Segmented` (Border) · button styles (implicit Button = Secondary) `PrimaryButton DangerButton DangerFilledButton GhostButton HeaderButton HeroButton HeroPrimaryButton HostCardButton ControlRowButton` · `ToggleSwitch` (CheckBox) · `SegmentedItem` (RadioButton) · implicit styles for `CheckBox ComboBox ComboBoxItem TextBox Slider ScrollBar Expander ToolTip ContextMenu MenuItem Separator`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/ThemeSmokeTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CouchLink.App.Theme;

namespace CouchLink.App.Tests;

/// <summary>Lays out one of every themed control, which instantiates each template and resolves its resources.</summary>
public class ThemeSmokeTests
{
    private static T Laid<T>(T element) where T : FrameworkElement
    {
        var host = new Border { Child = element };
        host.Measure(new Size(400, 600));
        host.Arrange(new Rect(0, 0, 400, 600));
        return element;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("PrimaryButton")]
    [InlineData("DangerButton")]
    [InlineData("DangerFilledButton")]
    [InlineData("GhostButton")]
    [InlineData("HeaderButton")]
    [InlineData("HeroButton")]
    [InlineData("HeroPrimaryButton")]
    [InlineData("HostCardButton")]
    [InlineData("ControlRowButton")]
    public void Every_button_style_lays_out(string? style)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var button = new Button { Content = "Go" };
            if (style is not null)
                button.Style = (Style)Application.Current.FindResource(style);
            Laid(button);
            Assert.NotNull(button.Template);
            Assert.True(VisualTreeHelper.GetChildrenCount(button) > 0);
        });
    }

    [Fact]
    public void The_implicit_button_is_the_secondary_look()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var button = Laid(new Button { Content = "Go" });
            Assert.Same(Application.Current.FindResource("CardBrush"), button.Background);
            Assert.Same(Application.Current.FindResource("ButtonTemplate"), button.Template);
        });
    }

    [Fact]
    public void Inputs_lay_out()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var app = Application.Current;
            var panel = new StackPanel();
            panel.Children.Add(new CheckBox { Content = "Plain" });
            panel.Children.Add(new CheckBox { Content = "Switch", Style = (Style)app.FindResource("ToggleSwitch"), IsChecked = true });
            panel.Children.Add(new RadioButton { Content = "Balanced", Style = (Style)app.FindResource("SegmentedItem"), IsChecked = true });
            var combo = new ComboBox();
            combo.Items.Add(new ComboBoxItem { Content = "1080p" });
            combo.SelectedIndex = 0;
            panel.Children.Add(combo);
            panel.Children.Add(new TextBox());
            panel.Children.Add(new Slider { Minimum = 1, Maximum = 10, Value = 5 });
            panel.Children.Add(new Expander { Header = "Stats", Content = new TextBlock { Text = "x" }, IsExpanded = true });
            panel.Children.Add(new ScrollViewer { Height = 50, Content = new Border { Height = 500 }, VerticalScrollBarVisibility = ScrollBarVisibility.Visible });
            Laid(panel);
            foreach (Control control in panel.Children)
                Assert.True(VisualTreeHelper.GetChildrenCount(control) > 0, control.GetType().Name);
        });
    }

    [Fact]
    public void Text_and_card_styles_exist()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            foreach (var key in new[] { "DisplayText", "TitleText", "SubtitleText", "BodyMutedText", "CaptionText", "OverlineText", "MonoText" })
                Assert.Equal(typeof(TextBlock), ((Style)Application.Current.FindResource(key)).TargetType);
            Assert.Equal(typeof(Border), ((Style)Application.Current.FindResource("Card")).TargetType);
            Assert.Equal(typeof(Border), ((Style)Application.Current.FindResource("Segmented")).TargetType);
        });
    }

    [Fact]
    public void A_text_box_shows_its_placeholder_only_while_empty()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var box = new TextBox();
            Ui.ThemeProps.SetPlaceholder(box, "192.168.1.23");
            Laid(box);
            var hint = (TextBlock)box.Template.FindName("Hint", box);
            Assert.Equal(Visibility.Visible, hint.Visibility);
            box.Text = "1";
            Assert.Equal(Visibility.Collapsed, hint.Visibility);
        });
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter ThemeSmokeTests`
Expected: FAIL with `ResourceReferenceKeyNotFoundException: 'PrimaryButton' resource not found` (and similar).

- [ ] **Step 3: Write Controls.xaml**

Replace `src/CouchLink.App/Theme/Controls.xaml` with:

```xml
<!--
  The theme's styles (spec sections 3.3-3.5 and 4). Brushes: DynamicResource, so High Contrast can swap
  them. FastDuration is put in the app's resources by ThemeManager before this file loads.
-->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:ui="clr-namespace:CouchLink.App.Ui">
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Icons.xaml"/>
    </ResourceDictionary.MergedDictionaries>

    <!-- Type -->
    <FontFamily x:Key="BodyFont">Segoe UI Variable Text, Segoe UI</FontFamily>
    <FontFamily x:Key="MonoFont">Cascadia Mono, Consolas</FontFamily>

    <Style x:Key="DisplayText" TargetType="TextBlock">
        <Setter Property="FontSize" Value="28"/>
        <Setter Property="FontWeight" Value="Bold"/>
        <Setter Property="TextWrapping" Value="Wrap"/>
    </Style>
    <Style x:Key="TitleText" TargetType="TextBlock">
        <Setter Property="FontSize" Value="20"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
    <Style x:Key="SubtitleText" TargetType="TextBlock">
        <Setter Property="FontSize" Value="15"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
    <Style x:Key="BodyMutedText" TargetType="TextBlock">
        <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
        <Setter Property="TextWrapping" Value="Wrap"/>
    </Style>
    <Style x:Key="CaptionText" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
    </Style>
    <Style x:Key="OverlineText" TargetType="TextBlock">
        <Setter Property="FontSize" Value="11"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
    </Style>
    <Style x:Key="MonoText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource MonoFont}"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
    </Style>

    <!-- Surfaces -->
    <Style x:Key="Card" TargetType="Border">
        <Setter Property="Background" Value="{DynamicResource CardBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="CornerRadius" Value="10"/>
        <Setter Property="Padding" Value="12"/>
    </Style>
    <Style x:Key="Segmented" TargetType="Border">
        <Setter Property="Background" Value="{DynamicResource BackgroundBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="CornerRadius" Value="8"/>
        <Setter Property="Padding" Value="3"/>
    </Style>

    <!-- Keyboard focus: a ring outside the control (mouse clicks don't show it) -->
    <Style x:Key="FocusRing">
        <Setter Property="Control.Template">
            <Setter.Value>
                <ControlTemplate>
                    <Rectangle Margin="-3" RadiusX="10" RadiusY="10" StrokeThickness="2" SnapsToDevicePixels="True"
                               Stroke="{DynamicResource PrimaryTextBrush}"/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Buttons: one template, hover and press as a white overlay so every variant behaves the same -->
    <ControlTemplate x:Key="ButtonTemplate" TargetType="ButtonBase">
        <Grid SnapsToDevicePixels="True">
            <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{TemplateBinding ui:ThemeProps.CornerRadius}"/>
            <Border x:Name="Hover" Background="{DynamicResource HoverOverlayBrush}" Opacity="0"
                    CornerRadius="{TemplateBinding ui:ThemeProps.CornerRadius}"/>
            <Border x:Name="Press" Background="{DynamicResource HoverOverlayBrush}" Opacity="0"
                    CornerRadius="{TemplateBinding ui:ThemeProps.CornerRadius}"/>
            <ContentPresenter Margin="{TemplateBinding Padding}" RecognizesAccessKey="True"
                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
        </Grid>
        <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Trigger.EnterActions>
                    <BeginStoryboard>
                        <Storyboard>
                            <DoubleAnimation Storyboard.TargetName="Hover" Storyboard.TargetProperty="Opacity" To="0.08" Duration="{StaticResource FastDuration}"/>
                        </Storyboard>
                    </BeginStoryboard>
                </Trigger.EnterActions>
                <Trigger.ExitActions>
                    <BeginStoryboard>
                        <Storyboard>
                            <DoubleAnimation Storyboard.TargetName="Hover" Storyboard.TargetProperty="Opacity" To="0" Duration="{StaticResource FastDuration}"/>
                        </Storyboard>
                    </BeginStoryboard>
                </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="Press" Property="Opacity" Value="0.08"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.4"/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <Style TargetType="Button">
        <Setter Property="Template" Value="{StaticResource ButtonTemplate}"/>
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
        <Setter Property="Background" Value="{DynamicResource CardBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="16,0"/>
        <Setter Property="MinHeight" Value="36"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="HorizontalContentAlignment" Value="Center"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="ui:ThemeProps.CornerRadius" Value="8"/>
    </Style>
    <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="{DynamicResource PrimaryBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource PrimaryBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource OnPrimaryBrush}"/>
    </Style>
    <Style x:Key="DangerButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderBrush" Value="{DynamicResource DangerFillBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource DangerBrush}"/>
    </Style>
    <Style x:Key="DangerFilledButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="{DynamicResource DangerFillBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource DangerFillBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource OnPrimaryBrush}"/>
    </Style>
    <Style x:Key="GhostButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}"/>
        <Setter Property="Padding" Value="8,0"/>
        <Setter Property="MinHeight" Value="32"/>
    </Style>
    <Style x:Key="HeaderButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Padding" Value="10,0"/>
        <Setter Property="MinHeight" Value="30"/>
        <Setter Property="FontWeight" Value="Normal"/>
        <Setter Property="FontSize" Value="13"/>
    </Style>
    <Style x:Key="HostCardButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Padding" Value="12"/>
        <Setter Property="MinHeight" Value="64"/>
        <Setter Property="FontWeight" Value="Normal"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Setter Property="ui:ThemeProps.CornerRadius" Value="10"/>
    </Style>
    <Style x:Key="ControlRowButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderBrush" Value="Transparent"/>
        <Setter Property="BorderThickness" Value="2"/>
        <Setter Property="Padding" Value="8,4"/>
        <Setter Property="MinHeight" Value="36"/>
        <Setter Property="FontWeight" Value="Normal"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Setter Property="ui:ThemeProps.CornerRadius" Value="7"/>
    </Style>

    <!-- Hero: icon tile, title (Content) and one line of description -->
    <ControlTemplate x:Key="HeroTemplate" TargetType="Button">
        <Grid SnapsToDevicePixels="True">
            <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="12"/>
            <Border x:Name="Hover" Background="{DynamicResource HoverOverlayBrush}" Opacity="0" CornerRadius="12"/>
            <Border x:Name="Press" Background="{DynamicResource HoverOverlayBrush}" Opacity="0" CornerRadius="12"/>
            <DockPanel Margin="{TemplateBinding Padding}">
                <Border DockPanel.Dock="Left" Width="44" Height="44" CornerRadius="10" Background="{DynamicResource HeroTileBrush}"
                        Margin="0,0,14,0" VerticalAlignment="Center">
                    <ui:Glyph Data="{TemplateBinding ui:ThemeProps.Icon}" Brush="{TemplateBinding Foreground}" Width="22" Height="22"/>
                </Border>
                <StackPanel VerticalAlignment="Center">
                    <ContentPresenter TextElement.FontSize="17" TextElement.FontWeight="SemiBold" RecognizesAccessKey="True"/>
                    <TextBlock Text="{TemplateBinding ui:ThemeProps.Description}" FontSize="12" FontWeight="Normal"
                               Opacity="0.85" TextWrapping="Wrap" Margin="0,2,0,0"/>
                </StackPanel>
            </DockPanel>
        </Grid>
        <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Trigger.EnterActions>
                    <BeginStoryboard>
                        <Storyboard>
                            <DoubleAnimation Storyboard.TargetName="Hover" Storyboard.TargetProperty="Opacity" To="0.08" Duration="{StaticResource FastDuration}"/>
                        </Storyboard>
                    </BeginStoryboard>
                </Trigger.EnterActions>
                <Trigger.ExitActions>
                    <BeginStoryboard>
                        <Storyboard>
                            <DoubleAnimation Storyboard.TargetName="Hover" Storyboard.TargetProperty="Opacity" To="0" Duration="{StaticResource FastDuration}"/>
                        </Storyboard>
                    </BeginStoryboard>
                </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="Press" Property="Opacity" Value="0.08"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.4"/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>
    <Style x:Key="HeroButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Template" Value="{StaticResource HeroTemplate}"/>
        <Setter Property="MinHeight" Value="76"/>
        <Setter Property="Padding" Value="16,12"/>
        <Setter Property="HorizontalContentAlignment" Value="Left"/>
    </Style>
    <Style x:Key="HeroPrimaryButton" TargetType="Button" BasedOn="{StaticResource HeroButton}">
        <Setter Property="Background" Value="{DynamicResource PrimaryGradientBrush}"/>
        <Setter Property="BorderBrush" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource OnPrimaryBrush}"/>
    </Style>

    <!-- Check box (plain) -->
    <Style TargetType="CheckBox">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="Background" Value="{DynamicResource InputBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderStrongBrush}"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <DockPanel Background="Transparent">
                        <Border x:Name="Box" DockPanel.Dock="Left" Width="18" Height="18" CornerRadius="4" BorderThickness="1"
                                Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" VerticalAlignment="Center">
                            <ui:Glyph x:Name="Tick" Data="{StaticResource IconCheck}" Brush="{DynamicResource OnPrimaryBrush}"
                                      Width="12" Height="12" Visibility="Collapsed"/>
                        </Border>
                        <ContentPresenter Margin="8,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                    </DockPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Box" Property="Background" Value="{DynamicResource PrimaryBrush}"/>
                            <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource PrimaryBrush}"/>
                            <Setter TargetName="Tick" Property="Visibility" Value="Visible"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Toggle switch (a CheckBox) for on/off settings -->
    <Style x:Key="ToggleSwitch" TargetType="CheckBox" BasedOn="{StaticResource {x:Type CheckBox}}">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <DockPanel Background="Transparent">
                        <Border x:Name="Track" DockPanel.Dock="Left" Width="40" Height="22" CornerRadius="11" BorderThickness="1"
                                Background="{DynamicResource RaisedBrush}" BorderBrush="{DynamicResource BorderStrongBrush}" VerticalAlignment="Center">
                            <Ellipse x:Name="Knob" Width="14" Height="14" Margin="3,0" HorizontalAlignment="Left"
                                     Fill="{DynamicResource TextMutedBrush}"/>
                        </Border>
                        <ContentPresenter Margin="10,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                    </DockPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Track" Property="Background" Value="{DynamicResource PrimaryBrush}"/>
                            <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource PrimaryBrush}"/>
                            <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right"/>
                            <Setter TargetName="Knob" Property="Fill" Value="{DynamicResource OnPrimaryBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Segmented control item (RadioButton inside a Border with the Segmented style) -->
    <Style x:Key="SegmentedItem" TargetType="RadioButton">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
        <Setter Property="MinHeight" Value="30"/>
        <Setter Property="Padding" Value="12,0"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                    <Border x:Name="Bd" CornerRadius="6" Background="Transparent" Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource PrimaryBrush}"/>
                            <Setter Property="Foreground" Value="{DynamicResource OnPrimaryBrush}"/>
                            <Setter Property="FontWeight" Value="SemiBold"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Text box, with ui:ThemeProps.Placeholder -->
    <Style TargetType="TextBox">
        <Setter Property="Background" Value="{DynamicResource InputBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="8,0"/>
        <Setter Property="MinHeight" Value="34"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="CaretBrush" Value="{DynamicResource TextBrush}"/>
        <Setter Property="SelectionBrush" Value="{DynamicResource PrimaryBrush}"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" SnapsToDevicePixels="True">
                        <Grid Margin="{TemplateBinding Padding}">
                            <TextBlock x:Name="Hint" Text="{TemplateBinding ui:ThemeProps.Placeholder}" Foreground="{DynamicResource TextHintBrush}"
                                       VerticalAlignment="{TemplateBinding VerticalContentAlignment}" Margin="2,0,0,0"
                                       IsHitTestVisible="False" Visibility="Collapsed"/>
                            <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Hidden"
                                          VerticalScrollBarVisibility="Hidden" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                        </Grid>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="Text" Value="">
                            <Setter TargetName="Hint" Property="Visibility" Value="Visible"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource BorderStrongBrush}"/>
                        </Trigger>
                        <Trigger Property="IsKeyboardFocused" Value="True">
                            <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource PrimaryTextBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Combo box -->
    <ControlTemplate x:Key="ComboToggle" TargetType="ToggleButton">
        <Border Background="Transparent"/>
    </ControlTemplate>
    <Style TargetType="ComboBox">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
        <Setter Property="Background" Value="{DynamicResource InputBrush}"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="MinHeight" Value="34"/>
        <Setter Property="Padding" Value="10,0,30,0"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBox">
                    <Grid>
                        <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6"/>
                        <ToggleButton Template="{StaticResource ComboToggle}" Focusable="False" ClickMode="Press"
                                      IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
                        <ContentPresenter Margin="{TemplateBinding Padding}" IsHitTestVisible="False"
                                          VerticalAlignment="Center" HorizontalAlignment="Left"
                                          Content="{TemplateBinding SelectionBoxItem}"
                                          ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                          ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"/>
                        <ui:Glyph Data="{StaticResource IconChevronDown}" Brush="{DynamicResource TextMutedBrush}" Width="14" Height="14"
                                  HorizontalAlignment="Right" Margin="0,0,10,0"/>
                        <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" VerticalOffset="4"
                               AllowsTransparency="True" Focusable="False" PopupAnimation="None">
                            <Border MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
                                    MaxHeight="{TemplateBinding MaxDropDownHeight}" Background="{DynamicResource CardBrush}"
                                    BorderBrush="{DynamicResource BorderStrongBrush}" BorderThickness="1" CornerRadius="8" Padding="4">
                                <ScrollViewer>
                                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource BorderStrongBrush}"/>
                        </Trigger>
                        <Trigger Property="IsDropDownOpen" Value="True">
                            <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource PrimaryTextBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="ComboBoxItem">
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="Padding" Value="10,6"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBoxItem">
                    <Border x:Name="Bd" CornerRadius="6" Padding="{TemplateBinding Padding}" Background="Transparent">
                        <ContentPresenter/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource RaisedBrush}"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}"/>
                            <Setter Property="FontWeight" Value="SemiBold"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Slider: filled track up to the thumb -->
    <Style x:Key="SliderTrackPart" TargetType="RepeatButton">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Focusable" Value="False"/>
        <Setter Property="IsTabStop" Value="False"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RepeatButton">
                    <Border Background="Transparent">
                        <Border Height="4" CornerRadius="2" Background="{TemplateBinding Background}" VerticalAlignment="Center"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="Slider">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Slider">
                    <Grid MinHeight="24" Background="Transparent">
                        <Track x:Name="PART_Track">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Style="{StaticResource SliderTrackPart}" Background="{DynamicResource PrimaryBrush}" Command="Slider.DecreaseLarge"/>
                            </Track.DecreaseRepeatButton>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Style="{StaticResource SliderTrackPart}" Background="{DynamicResource RaisedBrush}" Command="Slider.IncreaseLarge"/>
                            </Track.IncreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb Width="16" Height="16">
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Ellipse Fill="{DynamicResource OnPrimaryBrush}" Stroke="{DynamicResource PrimaryBrush}" StrokeThickness="3"/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Scroll bar: 8 px, rounded thumb, no arrows -->
    <Style x:Key="ScrollThumb" TargetType="Thumb">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="IsTabStop" Value="False"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Thumb">
                    <Border x:Name="T" CornerRadius="4" Background="{DynamicResource BorderStrongBrush}"/>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="T" Property="Background" Value="{DynamicResource TextHintBrush}"/>
                        </Trigger>
                        <Trigger Property="IsDragging" Value="True">
                            <Setter TargetName="T" Property="Background" Value="{DynamicResource TextMutedBrush}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style x:Key="ScrollPage" TargetType="RepeatButton">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Focusable" Value="False"/>
        <Setter Property="IsTabStop" Value="False"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RepeatButton">
                    <Border Background="Transparent"/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="ScrollBar">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Width" Value="8"/>
        <Setter Property="MinWidth" Value="8"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                    <Track x:Name="PART_Track" IsDirectionReversed="True">
                        <Track.DecreaseRepeatButton>
                            <RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageUpCommand"/>
                        </Track.DecreaseRepeatButton>
                        <Track.Thumb>
                            <Thumb Style="{StaticResource ScrollThumb}" Margin="1,0"/>
                        </Track.Thumb>
                        <Track.IncreaseRepeatButton>
                            <RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageDownCommand"/>
                        </Track.IncreaseRepeatButton>
                    </Track>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width" Value="Auto"/>
                <Setter Property="MinWidth" Value="0"/>
                <Setter Property="Height" Value="8"/>
                <Setter Property="MinHeight" Value="8"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ScrollBar">
                            <Track x:Name="PART_Track">
                                <Track.DecreaseRepeatButton>
                                    <RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageLeftCommand"/>
                                </Track.DecreaseRepeatButton>
                                <Track.Thumb>
                                    <Thumb Style="{StaticResource ScrollThumb}" Margin="0,1"/>
                                </Track.Thumb>
                                <Track.IncreaseRepeatButton>
                                    <RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageRightCommand"/>
                                </Track.IncreaseRepeatButton>
                            </Track>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- Expander: chevron + header, content indented -->
    <Style TargetType="Expander">
        <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Expander">
                    <DockPanel>
                        <ToggleButton DockPanel.Dock="Top" Cursor="Hand" FocusVisualStyle="{StaticResource FocusRing}"
                                      Focusable="{TemplateBinding Focusable}" Content="{TemplateBinding Header}"
                                      Foreground="{TemplateBinding Foreground}"
                                      IsChecked="{Binding IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                            <ToggleButton.Template>
                                <ControlTemplate TargetType="ToggleButton">
                                    <Border Background="Transparent" Padding="0,4">
                                        <StackPanel Orientation="Horizontal">
                                            <ui:Glyph x:Name="Chevron" Data="{StaticResource IconChevronRight}" Brush="{DynamicResource TextMutedBrush}"
                                                      Width="14" Height="14" RenderTransformOrigin="0.5,0.5"/>
                                            <ContentPresenter Margin="6,0,0,0" VerticalAlignment="Center"/>
                                        </StackPanel>
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsChecked" Value="True">
                                            <Setter TargetName="Chevron" Property="RenderTransform">
                                                <Setter.Value>
                                                    <RotateTransform Angle="90"/>
                                                </Setter.Value>
                                            </Setter>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </ToggleButton.Template>
                        </ToggleButton>
                        <ContentPresenter x:Name="Body" Margin="20,6,0,0" Visibility="Collapsed"/>
                    </DockPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsExpanded" Value="True">
                            <Setter TargetName="Body" Property="Visibility" Value="Visible"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Tool tip, context menu (the Help menu) -->
    <Style TargetType="ToolTip">
        <Setter Property="FontFamily" Value="{StaticResource BodyFont}"/>
        <Setter Property="FontSize" Value="12"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="Background" Value="{DynamicResource RaisedBrush}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource BorderStrongBrush}"/>
        <Setter Property="Padding" Value="8,4"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToolTip">
                    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1"
                            CornerRadius="6" Padding="{TemplateBinding Padding}">
                        <ContentPresenter/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="ContextMenu">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="HasDropShadow" Value="False"/>
        <Setter Property="FontFamily" Value="{StaticResource BodyFont}"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ContextMenu">
                    <Border Background="{DynamicResource CardBrush}" BorderBrush="{DynamicResource BorderStrongBrush}" BorderThickness="1"
                            CornerRadius="8" Padding="4" MinWidth="180">
                        <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="MenuItem">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="MenuItem">
                    <Border x:Name="Bd" CornerRadius="6" Padding="10,7" Background="Transparent">
                        <ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource RaisedBrush}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.4"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="Separator">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Separator">
                    <Border Height="1" Margin="4" Background="{DynamicResource BorderBrush}"/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS (including `ResourceKeyTests`).

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build -c Release`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. (The app already picks up the implicit styles: its old screens now render dark. That is expected; Tasks 7-12 rebuild them.)

- [ ] **Step 6: Commit**

```bash
git add src/CouchLink.App/Theme/Controls.xaml tests/CouchLink.App.Tests/ThemeSmokeTests.cs
git commit -m "feat(app): themed styles for buttons, inputs, menus and scroll bars"
```

---

### Task 6: Reusable UI pieces

**Files:**
- Create: `src/CouchLink.App/Ui/PlayerChip.cs`, `KeyCap.cs`, `StatusPill.cs`, `Banner.cs`, `StepTracker.cs`, `Spinner.cs`
- Create: `src/CouchLink.App/Ui/AppHeader.xaml`, `AppHeader.xaml.cs`
- Create: `src/CouchLink.App/Ui/ThemedDialog.xaml`, `ThemedDialog.xaml.cs`
- Test: `tests/CouchLink.App.Tests/UiPiecesTests.cs`

**Interfaces:**
- Consumes: `PlayerColors`, `Glyph`, `ThemeManager.AnimationsOn`, `WindowTheme.Apply`, styles from Task 5.
- Produces:
  - `PlayerChip : Border` — `byte Slot`, `bool Large`, `string Text` (read-only, "P3").
  - `KeyCap : Grid` — `string? Key` (null/empty = dashed "No key"), `string Text` (read-only).
  - `enum PillKind { Live, Reconnecting, Neutral }`; `StatusPill : Border` — `PillKind Kind`, `string Text`.
  - `enum BannerKind { Info, Warning, Error }`; `Banner : Border` — `BannerKind Kind`, `string Text`, `bool CanClose`, `event Action? CloseClicked`; internal `Button CloseButton`.
  - `StepTracker : Grid` — `int Current` (0..2); internal `IReadOnlyList<Ellipse> Dots`.
  - `Spinner : Grid` (16×16 ring that turns while visible, still when animations are off).
  - `AppHeader : UserControl` — events `ControlsClicked`, `CrashReportsClicked`, `AboutClicked`; `bool ButtonsFocusable` (set only).
  - `ThemedDialog : Window` — `static void Alert(Window? owner, string title, string message)`, `static bool Confirm(Window? owner, string title, string message, string confirmText, string cancelText, bool danger)`; internal ctor `ThemedDialog(Window? owner, string title, string message, string okText, string? cancelText, bool danger)` for tests.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/UiPiecesTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CouchLink.App.Theme;
using CouchLink.App.Ui;

namespace CouchLink.App.Tests;

public class UiPiecesTests
{
    private static object Res(string key) => Application.Current.FindResource(key);

    [Fact]
    public void Player_chip_shows_the_slot_in_its_color()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var chip = new PlayerChip { Slot = 3 };
            Assert.Equal("P3", chip.Text);
            Assert.Equal(PlayerColors.ColorFor(3), ((SolidColorBrush)chip.Background).Color);
            Assert.Equal(28, chip.Width);
            chip.Large = true;
            Assert.Equal(52, chip.Width);
        });
    }

    [Fact]
    public void Key_cap_without_a_key_says_no_key()
    {
        Wpf.Run(() =>
        {
            Assert.Equal("No key", new KeyCap().Text);
            Assert.Equal("Left Shift", new KeyCap { Key = "Left Shift" }.Text);
            Assert.Equal("No key", new KeyCap { Key = "" }.Text);
        });
    }

    [Theory]
    [InlineData(PillKind.Live, "LivePillFillBrush", "SuccessBrush")]
    [InlineData(PillKind.Reconnecting, "ReconnectingPillFillBrush", "WarningBrush")]
    [InlineData(PillKind.Neutral, "RaisedBrush", "TextMutedBrush")]
    public void Status_pill_colors_follow_its_kind(PillKind kind, string fill, string text)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var pill = new StatusPill { Kind = kind, Text = "x" };
            Assert.Same(Res(fill), pill.Background);
            Assert.Same(Res(text), ((TextBlock)pill.Child).Foreground);
        });
    }

    [Fact]
    public void Banner_close_button_shows_only_when_closable_and_raises_its_event()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var banner = new Banner { Kind = BannerKind.Error, Text = "The host ended the session." };
            Assert.Same(Res("ErrorBannerFillBrush"), banner.Background);
            Assert.Equal(Visibility.Collapsed, banner.CloseButton.Visibility);
            banner.CanClose = true;
            Assert.Equal(Visibility.Visible, banner.CloseButton.Visibility);
            bool closed = false;
            banner.CloseClicked += () => closed = true;
            banner.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(closed);
        });
    }

    [Fact]
    public void Step_tracker_marks_done_current_and_upcoming_steps()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var steps = new StepTracker { Current = 1 };
            Assert.Same(Res("PrimaryBrush"), steps.Dots[0].Fill);
            Assert.Same(Res("PrimaryTextBrush"), steps.Dots[1].Fill);
            Assert.Same(Res("BorderBrush"), steps.Dots[2].Fill);
        });
    }

    [Fact]
    public void Header_buttons_can_be_made_unfocusable_and_raise_controls()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var header = new AppHeader();
            header.ButtonsFocusable = false;
            Assert.False(header.ControlsButton.Focusable);
            Assert.False(header.HelpButton.Focusable);
            bool clicked = false;
            header.ControlsClicked += () => clicked = true;
            header.ControlsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(clicked);
        });
    }

    [Fact]
    public void A_danger_confirm_defaults_to_cancel()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var dialog = new ThemedDialog(null, "Stop hosting?", "PC-07 will be disconnected.", "Stop hosting", "Keep hosting", danger: true);
            Assert.True(dialog.CancelButton.IsDefault);
            Assert.False(dialog.OkButton.IsDefault);
            Assert.Same(Res("DangerFilledButton"), dialog.OkButton.Style);
            dialog.Close();
        });
    }

    [Fact]
    public void An_alert_has_one_button()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var dialog = new ThemedDialog(null, "Couldn't start hosting", "ViGEmBus is missing.", "OK", null, danger: false);
            Assert.Equal(Visibility.Collapsed, dialog.CancelButton.Visibility);
            Assert.True(dialog.OkButton.IsDefault);
            Assert.Equal("Couldn't start hosting", dialog.TitleText.Text);
            dialog.Close();
        });
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter UiPiecesTests`
Expected: build FAILS: `The type or namespace name 'PlayerChip' could not be found`.

- [ ] **Step 3: Write the small controls**

`src/CouchLink.App/Ui/PlayerChip.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace CouchLink.App.Ui;

/// <summary>"P3" on the player's color: small in lists, large on the session screen.</summary>
internal sealed class PlayerChip : Border
{
    private readonly TextBlock _text = new()
    {
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = PlayerColors.TextOnPlayer,
    };
    private byte _slot;
    private bool _large;

    public PlayerChip()
    {
        Child = _text;
        Resize();
        Slot = 1;
    }

    public string Text => _text.Text;

    public byte Slot
    {
        get => _slot;
        set
        {
            _slot = value;
            Background = PlayerColors.BrushFor(value);
            _text.Text = $"P{value}";
            AutomationProperties.SetName(this, $"Player {value}");
        }
    }

    public bool Large
    {
        get => _large;
        set
        {
            _large = value;
            Resize();
        }
    }

    private void Resize()
    {
        Width = Height = _large ? 52 : 28;
        CornerRadius = new CornerRadius(_large ? 14 : 8);
        _text.FontSize = _large ? 18 : 11;
    }
}
```

`src/CouchLink.App/Ui/KeyCap.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CouchLink.App.Ui;

/// <summary>A key name drawn like a key cap, or a dashed "No key" when the control has none.</summary>
internal sealed class KeyCap : Grid
{
    private readonly Border _cap = new() { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1, 1, 1, 3) };
    private readonly Rectangle _dashed = new() { RadiusX = 6, RadiusY = 6, StrokeThickness = 1, StrokeDashArray = [3, 2] };
    private readonly TextBlock _text = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 2, 8, 2) };
    private string? _key;

    public KeyCap()
    {
        MinHeight = 24;
        Margin = new Thickness(4, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        _cap.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
        _cap.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        _dashed.SetResourceReference(Shape.StrokeProperty, "BorderStrongBrush");
        _text.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        Children.Add(_cap);
        Children.Add(_dashed);
        Children.Add(_text);
        Key = null;
    }

    public string Text => _text.Text;

    public string? Key
    {
        get => _key;
        set
        {
            _key = value;
            bool none = string.IsNullOrEmpty(value);
            _text.Text = none ? "No key" : value!;
            _cap.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            _dashed.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
            _text.SetResourceReference(TextBlock.ForegroundProperty, none ? "TextMutedBrush" : "TextBrush");
        }
    }
}
```

`src/CouchLink.App/Ui/StatusPill.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace CouchLink.App.Ui;

internal enum PillKind { Live, Reconnecting, Neutral }

/// <summary>Short status text in a pill: "● Live", "Reconnecting", "Different CouchLink version".</summary>
internal sealed class StatusPill : Border
{
    private readonly TextBlock _text = new() { FontSize = 11, FontWeight = FontWeights.SemiBold };
    private PillKind _kind;

    public StatusPill()
    {
        Child = _text;
        CornerRadius = new CornerRadius(999);
        Padding = new Thickness(9, 3, 9, 3);
        VerticalAlignment = VerticalAlignment.Center;
        Kind = PillKind.Neutral;
    }

    public string Text
    {
        get => _text.Text;
        set => _text.Text = value;
    }

    public PillKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            (string fill, string text) = value switch
            {
                PillKind.Live => ("LivePillFillBrush", "SuccessBrush"),
                PillKind.Reconnecting => ("ReconnectingPillFillBrush", "WarningBrush"),
                _ => ("RaisedBrush", "TextMutedBrush"),
            };
            SetResourceReference(BackgroundProperty, fill);
            _text.SetResourceReference(TextBlock.ForegroundProperty, text);
        }
    }
}
```

`src/CouchLink.App/Ui/Banner.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace CouchLink.App.Ui;

internal enum BannerKind { Info, Warning, Error }

/// <summary>A message across the screen: info, warning (link too slow, key moved) or error (why the last session ended).</summary>
internal sealed class Banner : Border
{
    private readonly Glyph _icon = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 8, 0) };
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private BannerKind _kind;

    public Banner()
    {
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 8, 8, 8);
        CloseButton = new Button
        {
            Padding = new Thickness(4, 0, 4, 0),
            MinHeight = 24,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Content = new Glyph { Width = 14, Height = 14 },
        };
        CloseButton.SetResourceReference(StyleProperty, "GhostButton");
        AutomationProperties.SetName(CloseButton, "Dismiss");
        AutomationProperties.SetAutomationId(CloseButton, "BannerClose");
        CloseButton.Click += (_, _) => CloseClicked?.Invoke();
        ((Glyph)CloseButton.Content).SetResourceReference(Glyph.DataProperty, "IconClose");

        var row = new DockPanel();
        DockPanel.SetDock(_icon, Dock.Left);
        DockPanel.SetDock(CloseButton, Dock.Right);
        row.Children.Add(_icon);
        row.Children.Add(CloseButton);
        row.Children.Add(_text);
        Child = row;
        Kind = BannerKind.Info;
    }

    public event Action? CloseClicked;

    internal Button CloseButton { get; }

    public string Text
    {
        get => _text.Text;
        set
        {
            _text.Text = value;
            AutomationProperties.SetName(this, value);
        }
    }

    public bool CanClose
    {
        get => CloseButton.Visibility == Visibility.Visible;
        set => CloseButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public BannerKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            SetResourceReference(BackgroundProperty, $"{value}BannerFillBrush");
            SetResourceReference(BorderBrushProperty, $"{value}BannerBorderBrush");
            _text.SetResourceReference(TextBlock.ForegroundProperty, $"{value}BannerTextBrush");
            _icon.SetResourceReference(Glyph.BrushProperty, $"{value}BannerTextBrush");
            _icon.SetResourceReference(Glyph.DataProperty, value == BannerKind.Info ? "IconInfo" : "IconAlert");
            ((Glyph)CloseButton.Content).SetResourceReference(Glyph.BrushProperty, $"{value}BannerTextBrush");
        }
    }
}
```

`ResourceKeyTests` cannot see keys built with `$"{value}…"`. They are `InfoBannerFillBrush`, `WarningBannerFillBrush`, `ErrorBannerFillBrush` and the matching `…BorderBrush` and `…TextBrush`, all defined in Task 4.

`src/CouchLink.App/Ui/StepTracker.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace CouchLink.App.Ui;

/// <summary>Connect → Host lets you in → Play, on the session screen. Steps before Current are done.</summary>
internal sealed class StepTracker : Grid
{
    private static readonly string[] Names = ["Connect", "Host lets you in", "Play"];
    private readonly Ellipse[] _dots = new Ellipse[3];
    private readonly TextBlock[] _labels = new TextBlock[3];
    private readonly Border[] _lines = new Border[2];
    private int _current;

    public StepTracker()
    {
        for (int i = 0; i < 5; i++)
            ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++)
        {
            _dots[i] = new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
            _labels[i] = new TextBlock { Text = Names[i], FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var step = new StackPanel { Orientation = Orientation.Horizontal, Children = { _dots[i], _labels[i] } };
            SetColumn(step, i * 2);
            Children.Add(step);
        }
        for (int i = 0; i < 2; i++)
        {
            _lines[i] = new Border { Height = 2, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 12 };
            SetColumn(_lines[i], i * 2 + 1);
            Children.Add(_lines[i]);
        }
        Current = 0;
    }

    internal IReadOnlyList<Ellipse> Dots => _dots;

    public int Current
    {
        get => _current;
        set
        {
            _current = Math.Clamp(value, 0, 2);
            for (int i = 0; i < 3; i++)
            {
                _dots[i].SetResourceReference(Shape.FillProperty, i < _current ? "PrimaryBrush" : i == _current ? "PrimaryTextBrush" : "BorderBrush");
                _labels[i].SetResourceReference(TextBlock.ForegroundProperty, i == _current ? "TextBrush" : "TextMutedBrush");
                _labels[i].FontWeight = i == _current ? FontWeights.SemiBold : FontWeights.Normal;
            }
            for (int i = 0; i < 2; i++)
                _lines[i].SetResourceReference(Border.BackgroundProperty, i < _current ? "PrimaryBrush" : "BorderBrush");
        }
    }
}
```

`src/CouchLink.App/Ui/Spinner.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CouchLink.App.Theme;

namespace CouchLink.App.Ui;

/// <summary>A small turning ring for "looking" and "connecting". It only turns while visible, and not at all when Windows animations are off.</summary>
internal sealed class Spinner : Grid
{
    private readonly RotateTransform _turn = new();

    public Spinner()
    {
        Width = Height = 16;
        VerticalAlignment = VerticalAlignment.Center;
        var track = new Ellipse { StrokeThickness = 2 };
        track.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
        var arc = new Path
        {
            Data = Geometry.Parse("M8,1 A7,7 0 0 1 15,8"),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _turn,
            Width = 16,
            Height = 16,
        };
        arc.SetResourceReference(Shape.StrokeProperty, "PrimaryTextBrush");
        Children.Add(track);
        Children.Add(arc);
        IsVisibleChanged += (_, _) => Animate(IsVisible);
    }

    private void Animate(bool on)
    {
        if (on && ThemeManager.AnimationsOn)
            _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        else
            _turn.BeginAnimation(RotateTransform.AngleProperty, null);
    }
}
```

- [ ] **Step 4: Write AppHeader**

`src/CouchLink.App/Ui/AppHeader.xaml`:

```xml
<UserControl x:Class="CouchLink.App.Ui.AppHeader" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:CouchLink.App.Ui">
    <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1" Padding="16,10">
        <DockPanel>
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                <Button x:Name="ControlsButton" Style="{StaticResource HeaderButton}"
                        AutomationProperties.Name="Controls" AutomationProperties.AutomationId="HeaderControls">
                    <StackPanel Orientation="Horizontal">
                        <ui:Glyph Data="{StaticResource IconGamepad}" Brush="{DynamicResource PrimaryTextBrush}" Width="16" Height="16"/>
                        <TextBlock Text="Controls" Margin="6,0,0,0" VerticalAlignment="Center"/>
                    </StackPanel>
                </Button>
                <Button x:Name="HelpButton" Style="{StaticResource HeaderButton}" Margin="6,0,0,0"
                        AutomationProperties.Name="Help" AutomationProperties.AutomationId="HeaderHelp">
                    <StackPanel Orientation="Horizontal">
                        <ui:Glyph Data="{StaticResource IconHelp}" Brush="{DynamicResource TextMutedBrush}" Width="16" Height="16"/>
                        <TextBlock Text="Help" Margin="6,0,4,0" VerticalAlignment="Center"/>
                        <ui:Glyph Data="{StaticResource IconChevronDown}" Brush="{DynamicResource TextMutedBrush}" Width="12" Height="12"/>
                    </StackPanel>
                    <Button.ContextMenu>
                        <ContextMenu x:Name="HelpMenu">
                            <MenuItem x:Name="CrashReportsItem" Header="Crash reports" AutomationProperties.AutomationId="HelpCrashReports"/>
                            <MenuItem x:Name="AboutItem" Header="About CouchLink" AutomationProperties.AutomationId="HelpAbout"/>
                        </ContextMenu>
                    </Button.ContextMenu>
                </Button>
            </StackPanel>
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <Border Width="22" Height="22" CornerRadius="6" Background="{DynamicResource LogoBrush}"/>
                <TextBlock Text="CouchLink" FontWeight="Bold" FontSize="15" Margin="8,0,0,0" VerticalAlignment="Center"/>
            </StackPanel>
        </DockPanel>
    </Border>
</UserControl>
```

`src/CouchLink.App/Ui/AppHeader.xaml.cs`:

```csharp
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CouchLink.App.Ui;

/// <summary>The bar on every main-window screen: logo, Controls, and a Help menu with Crash reports and About.</summary>
internal sealed partial class AppHeader : UserControl
{
    public AppHeader()
    {
        InitializeComponent();
        ControlsButton.Click += (_, _) => ControlsClicked?.Invoke();
        HelpButton.Click += (_, _) =>
        {
            HelpMenu.PlacementTarget = HelpButton;
            HelpMenu.Placement = PlacementMode.Bottom;
            HelpMenu.IsOpen = true;
        };
        CrashReportsItem.Click += (_, _) => CrashReportsClicked?.Invoke();
        AboutItem.Click += (_, _) => AboutClicked?.Invoke();
    }

    public event Action? ControlsClicked;
    public event Action? CrashReportsClicked;
    public event Action? AboutClicked;

    /// <summary>False while playing: Raw Input keys still reach a focused button, so Space in the game would press it.</summary>
    public bool ButtonsFocusable
    {
        set
        {
            ControlsButton.Focusable = value;
            HelpButton.Focusable = value;
        }
    }
}
```

- [ ] **Step 5: Write ThemedDialog**

`src/CouchLink.App/Ui/ThemedDialog.xaml`:

```xml
<Window x:Class="CouchLink.App.Ui.ThemedDialog" x:ClassModifier="internal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="CouchLink" Width="400" SizeToContent="Height" ResizeMode="NoResize"
        ShowInTaskbar="False" WindowStartupLocation="CenterOwner">
    <StackPanel Margin="20">
        <TextBlock x:Name="TitleText" Style="{StaticResource SubtitleText}" TextWrapping="Wrap"/>
        <TextBlock x:Name="MessageText" Style="{StaticResource BodyMutedText}" Margin="0,6,0,0"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
            <Button x:Name="CancelButton" IsCancel="True" MinWidth="96" AutomationProperties.AutomationId="DialogCancel"/>
            <Button x:Name="OkButton" MinWidth="96" Margin="8,0,0,0" Style="{StaticResource PrimaryButton}"
                    AutomationProperties.AutomationId="DialogOk"/>
        </StackPanel>
    </StackPanel>
</Window>
```

`src/CouchLink.App/Ui/ThemedDialog.xaml.cs`:

```csharp
using System.Windows;
using CouchLink.App.Theme;

namespace CouchLink.App.Ui;

/// <summary>The app's message box: a title, a message, and OK or two choices. Replaces MessageBox.Show.</summary>
internal sealed partial class ThemedDialog : Window
{
    /// <summary>Internal for tests; use <see cref="Alert"/> or <see cref="Confirm"/>.</summary>
    internal ThemedDialog(Window? owner, string title, string message, string okText, string? cancelText, bool danger)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = okText;
        OkButton.Click += (_, _) => DialogResult = true;
        if (cancelText is null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            OkButton.IsDefault = true;
        }
        else
        {
            CancelButton.Content = cancelText;
            // A destructive choice is never the default: a stray Enter keeps things as they are.
            OkButton.IsDefault = !danger;
            CancelButton.IsDefault = danger;
        }
        if (danger)
            OkButton.Style = (Style)FindResource("DangerFilledButton");
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            Topmost = owner.Topmost;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        Loaded += (_, _) => (CancelButton.IsDefault ? CancelButton : OkButton).Focus();
    }

    public static void Alert(Window? owner, string title, string message) =>
        new ThemedDialog(owner, title, message, "OK", null, danger: false).ShowDialog();

    public static bool Confirm(Window? owner, string title, string message, string confirmText, string cancelText, bool danger) =>
        new ThemedDialog(owner, title, message, confirmText, cancelText, danger).ShowDialog() == true;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.App/Ui tests/CouchLink.App.Tests/UiPiecesTests.cs
git commit -m "feat(app): header, dialog, player chip, key cap, pills, banners and steps"
```

---

### Task 7: Main window, header and Start screen

**Files:**
- Modify: `src/CouchLink.App/MainWindow.xaml`, `src/CouchLink.App/MainWindow.xaml.cs`
- Modify (replace): `src/CouchLink.App/Views/StartView.xaml`, `src/CouchLink.App/Views/StartView.xaml.cs`
- Test: `tests/CouchLink.App.Tests/StartScreenTests.cs`

**Interfaces:**
- Consumes: `AppHeader`, `ThemedDialog`, `WindowTheme`, `Motion`, `AppInfo`, `PcName.ThisPc`.
- Produces: `StartView` keeps `HostClicked`, `JoinClicked`; drops `ControlsClicked` and `CrashReportsClicked` (moved to `AppHeader`). `MainWindow.Header` (generated field) and `MainWindow.Screen`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/StartScreenTests.cs`:

```csharp
using System.Windows;
using CouchLink.App.Presentation;
using CouchLink.App.Theme;
using CouchLink.App.Views;
using CouchLink.Core.Protocol;

namespace CouchLink.App.Tests;

public class StartScreenTests
{
    [Fact]
    public void Start_shows_this_pc_and_the_version()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var start = new StartView();
            Assert.Equal($"This PC: {PcName.ThisPc}", start.PcText.Text);
            Assert.Equal($"v{AppInfo.Version}", start.VersionText.Text);
            Assert.Equal("Host a game", start.HostButton.Content);
            Assert.Equal("Join a game", start.JoinButton.Content);
        });
    }

    [Fact]
    public void The_main_window_opens_on_start_with_the_header()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var window = new MainWindow();
            Assert.IsType<StartView>(window.Screen.Content);
            Assert.True(window.Header.ControlsButton.Focusable);
            Assert.Equal(540, window.Width);
            Assert.Equal(460, window.MinWidth);
            window.Close();
        });
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter StartScreenTests`
Expected: build FAILS: `'StartView' does not contain a definition for 'PcText'`.

- [ ] **Step 3: Write the Start screen**

`src/CouchLink.App/Views/StartView.xaml`:

```xml
<UserControl x:Class="CouchLink.App.Views.StartView" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:CouchLink.App.Ui">
    <DockPanel Margin="24,16">
        <DockPanel DockPanel.Dock="Bottom">
            <TextBlock x:Name="VersionText" DockPanel.Dock="Right" Style="{StaticResource CaptionText}"/>
            <TextBlock x:Name="PcText" Style="{StaticResource CaptionText}" TextTrimming="CharacterEllipsis"/>
        </DockPanel>
        <StackPanel VerticalAlignment="Center">
            <TextBlock Text="Ready to play?" Style="{StaticResource DisplayText}"/>
            <TextBlock Text="Couch co-op across the PCs on this network." Style="{StaticResource BodyMutedText}" Margin="0,4,0,24"/>
            <Button x:Name="HostButton" Style="{StaticResource HeroPrimaryButton}" Content="Host a game"
                    ui:ThemeProps.Icon="{StaticResource IconMonitor}"
                    ui:ThemeProps.Description="Run the game here. Each friend gets their own controller."
                    AutomationProperties.AutomationId="HostButton"/>
            <Button x:Name="JoinButton" Style="{StaticResource HeroButton}" Content="Join a game" Margin="0,12,0,0"
                    ui:ThemeProps.Icon="{StaticResource IconArrowRight}"
                    ui:ThemeProps.Description="Play a game running on another PC."
                    AutomationProperties.AutomationId="JoinButton"/>
        </StackPanel>
    </DockPanel>
</UserControl>
```

`src/CouchLink.App/Views/StartView.xaml.cs`:

```csharp
using System.Windows.Controls;
using CouchLink.App.Presentation;
using CouchLink.Core.Protocol;

namespace CouchLink.App.Views;

/// <summary>The first screen: Host a game and Join a game, with this PC's name and the version.</summary>
internal sealed partial class StartView : UserControl
{
    public StartView()
    {
        InitializeComponent();
        PcText.Text = $"This PC: {PcName.ThisPc}";
        VersionText.Text = $"v{AppInfo.Version}";
        HostButton.Click += (_, _) => HostClicked?.Invoke();
        JoinButton.Click += (_, _) => JoinClicked?.Invoke();
    }

    public event Action? HostClicked;
    public event Action? JoinClicked;
}
```

- [ ] **Step 4: Write the main window**

`src/CouchLink.App/MainWindow.xaml`:

```xml
<Window x:Class="CouchLink.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:CouchLink.App.Ui"
        Title="CouchLink" Width="540" Height="660" MinWidth="460" MinHeight="560">
    <DockPanel>
        <ui:AppHeader x:Name="Header" DockPanel.Dock="Top"/>
        <ContentControl x:Name="Screen" Focusable="False"/>
    </DockPanel>
</Window>
```

In `src/CouchLink.App/MainWindow.xaml.cs`:

1. Add usings: `using CouchLink.App.Presentation;`, `using CouchLink.App.Theme;`, `using CouchLink.App.Ui;`.
2. Replace the constructor, `Show`, `ShowStart`, `ShowHost`, `ShowJoinList`, `Join` and `OpenCrashReports` with:

```csharp
    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        Header.ControlsClicked += () => ControlsWindow.Open(this);
        Header.CrashReportsClicked += OpenCrashReports;
        Header.AboutClicked += () => ThemedDialog.Alert(this, "About CouchLink", AppInfo.AboutText);
        _detailsTimer.Tick += (_, _) =>
        {
            if (_sessionView is not null)
                _sessionView.Details = _play?.Describe() ?? "";
        };
        ShowStart();
    }

    /// <summary>
    /// Shows a view; the one it replaces is disposed (stopping whatever it owned). The header's buttons
    /// can't take focus on the session screen, where Raw Input keys would press them.
    /// </summary>
    private void Show(UserControl view, bool headerFocusable = true)
    {
        if (Screen.Content is IDisposable old && !ReferenceEquals(old, view))
            old.Dispose();
        Screen.Content = view;
        Header.ButtonsFocusable = headerFocusable;
        Motion.Enter(view);
    }

    private void ShowStart()
    {
        var start = new StartView();
        start.HostClicked += ShowHost;
        start.JoinClicked += () => ShowJoinList(null);
        Show(start);
        AppServices.DescribeMode = () => "Idle";
    }

    private void ShowHost()
    {
        var lobby = new HostLobbyView();
        if (!lobby.TryStart(out var error))
        {
            ThemedDialog.Alert(this, "Couldn't start hosting", error ?? "Hosting could not start.");
            return;
        }
        lobby.Stopped += ShowStart;
        Show(lobby);
    }

    private void ShowJoinList(string? message)
    {
        var list = new JoinListView();
        list.BackClicked += ShowStart;
        list.JoinRequested += Join;
        list.ShowMessage(message);
        Show(list);
    }

    private void Join(IPAddress host, string hostName)
    {
        _sessionView = new SessionView();
        _sessionView.LeaveClicked += LeaveSession;
        _sessionView.ControlsClicked += () => ControlsWindow.Open(this);
        Show(_sessionView, headerFocusable: false); // closes the join list, freeing UDP 47800
        _session = new ClientSessionService(host, hostName, Dispatcher, this);
        UpdateSessionView();
        _detailsTimer.Start();
    }
```

```csharp
    private void OpenCrashReports()
    {
        try
        {
            var directory = AppServices.CrashReports.ReportsDirectory();
            Process.Start("explorer.exe", $"\"{directory}\"");
        }
        catch (Exception ex)
        {
            ThemedDialog.Alert(this, "Couldn't open crash reports", $"Could not open the crash reports folder:\n{ex.Message}");
        }
    }
```

Everything else in `MainWindow.xaml.cs` (`StartPlaying`, `Ended`, `StateChanged`, `UpdateSessionView`, `LeaveSession`, `EndSession`, `BringToFront`, `OpenControlsOverGame`, `ReturnToGame`, `OnClosed`) stays as it is.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS. (HostLobbyView, JoinListView and SessionView still have their old XAML; they build against the new theme.)

- [ ] **Step 6: Look at it**

Run: `dotnet run --project src/CouchLink.App -- --windowed-player`
Expected: dark title bar, the header with Controls and Help, "Ready to play?", the purple Host a game card and the Join a game card, "This PC: …" and the version at the bottom. Help opens a menu with Crash reports and About CouchLink; About shows the themed dialog. Close the app.

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.App/MainWindow.xaml src/CouchLink.App/MainWindow.xaml.cs src/CouchLink.App/Views/StartView.xaml src/CouchLink.App/Views/StartView.xaml.cs tests/CouchLink.App.Tests/StartScreenTests.cs
git commit -m "feat(app): header on every screen and the new Start screen"
```

---

### Task 8: Host lobby

**Files:**
- Modify (replace): `src/CouchLink.App/Views/HostLobbyView.xaml`, `src/CouchLink.App/Views/HostLobbyView.xaml.cs`
- Test: `tests/CouchLink.App.Tests/HostLobbyTests.cs`

**Interfaces:**
- Consumes: `StopHostingPrompt.For`, `ThemedDialog.Confirm`, `PlayerChip`, `StatusPill`, `Banner`, `HostService` (unchanged: `TryStart`, `ChangeSettings`, `AllowEveryone`, `Players`, `Kick`, `Allow`, `Deny`, `PadCount`, `DescribeStreams`, `LastError`, `LinkWarning`, `Dispose`), `HostSession.Capacity`, `PlayerInfo(byte Slot, string Name, IPAddress Address, PlayerState State)`.
- Produces: `HostLobbyView` keeps `Stopped`, `TryStart(out string?)`, `IHostUi`, `Dispose()`. New internal for tests: `StreamSettings CurrentSettings()`, `void ShowPlayers(IReadOnlyList<PlayerInfo> players)`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/HostLobbyTests.cs`:

```csharp
using System.Net;
using System.Windows;
using System.Windows.Controls;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.App.Views;
using CouchLink.Core.Session;
using CouchLink.Core.Video;

namespace CouchLink.App.Tests;

public class HostLobbyTests
{
    private static HostLobbyView Lobby()
    {
        ThemeManager.Install(Application.Current);
        return new HostLobbyView();
    }

    [Fact]
    public void Quality_starts_on_the_default_and_follows_the_segmented_control()
    {
        Wpf.Run(() =>
        {
            var lobby = Lobby();
            Assert.Equal(StreamSettings.Default.Quality, lobby.CurrentSettings().Quality);
            var high = lobby.QualityGroup.Children.OfType<RadioButton>().Single(r => (StreamQuality)r.Tag == StreamQuality.High);
            high.IsChecked = true;
            Assert.Equal(StreamQuality.High, lobby.CurrentSettings().Quality);
        });
    }

    [Fact]
    public void With_no_players_the_host_row_and_a_hint_show()
    {
        Wpf.Run(() =>
        {
            var lobby = Lobby();
            lobby.ShowPlayers([]);
            Assert.Equal($"PLAYERS 1 / {HostSession.Capacity + 1}", lobby.PlayersHeading.Text);
            Assert.Contains(Descendants<TextBlock>(lobby.PlayerList), t => t.Text == "You (host)");
            Assert.Contains(Descendants<TextBlock>(lobby.PlayerList), t => t.Text == "No one has joined yet. Players appear here when they join.");
        });
    }

    [Fact]
    public void Players_get_their_chip_and_reconnecting_players_a_pill()
    {
        Wpf.Run(() =>
        {
            var lobby = Lobby();
            lobby.ShowPlayers(
            [
                new PlayerInfo(2, "PC-07", IPAddress.Loopback, PlayerState.Active),
                new PlayerInfo(3, "PC-11", IPAddress.Loopback, PlayerState.Reserved),
            ]);
            Assert.Equal($"PLAYERS 3 / {HostSession.Capacity + 1}", lobby.PlayersHeading.Text);
            Assert.Equal(new[] { 1, 2, 3 }, Descendants<PlayerChip>(lobby.PlayerList).Select(c => (int)c.Slot));
            Assert.Single(Descendants<StatusPill>(lobby.PlayerList), p => p.Text == "Reconnecting");
            Assert.Equal(2, Descendants<Button>(lobby.PlayerList).Count(b => (string)b.Content == "Kick"));
        });
    }

    private static IEnumerable<T> Descendants<T>(Panel panel) where T : DependencyObject =>
        panel.Children.OfType<DependencyObject>().SelectMany(Walk).OfType<T>();

    private static IEnumerable<DependencyObject> Walk(DependencyObject node)
    {
        yield return node;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var d in Walk(child))
                yield return d;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter HostLobbyTests`
Expected: build FAILS: `'HostLobbyView' does not contain a definition for 'QualityGroup'`.

- [ ] **Step 3: Write the view**

`src/CouchLink.App/Views/HostLobbyView.xaml`:

```xml
<UserControl x:Class="CouchLink.App.Views.HostLobbyView" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:CouchLink.App.Ui">
    <DockPanel>
        <Border DockPanel.Dock="Bottom" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,1,0,0" Padding="16,12">
            <Button x:Name="StopButton" Style="{StaticResource DangerButton}" Content="Stop hosting" MinHeight="40"
                    AutomationProperties.AutomationId="StopHosting"/>
        </Border>
        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
            <StackPanel Margin="16">
                <DockPanel>
                    <ui:StatusPill DockPanel.Dock="Right" Kind="Live" Text="● Live" Margin="8,0,0,0"/>
                    <TextBlock x:Name="Heading" Style="{StaticResource TitleText}" TextTrimming="CharacterEllipsis"/>
                </DockPanel>
                <TextBlock Style="{StaticResource BodyMutedText}" Margin="0,4,0,0"
                           Text="Minimize this and start the game. Friends pick this PC under Join."/>

                <Border Style="{StaticResource Card}" Margin="0,16,0,0">
                    <StackPanel>
                        <TextBlock Text="STREAM" Style="{StaticResource OverlineText}"/>
                        <Grid Margin="0,8,0,0">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition/>
                                <ColumnDefinition Width="8"/>
                                <ColumnDefinition/>
                            </Grid.ColumnDefinitions>
                            <ComboBox x:Name="ResolutionBox" AutomationProperties.Name="Resolution" AutomationProperties.AutomationId="Resolution"/>
                            <ComboBox x:Name="FrameRateBox" Grid.Column="2" AutomationProperties.Name="Frame rate" AutomationProperties.AutomationId="FrameRate"/>
                        </Grid>
                        <Border Style="{StaticResource Segmented}" Margin="0,8,0,0">
                            <UniformGrid x:Name="QualityGroup" Rows="1" AutomationProperties.Name="Quality"/>
                        </Border>
                    </StackPanel>
                </Border>
                <ui:Banner x:Name="BudgetWarning" Kind="Warning" Margin="0,12,0,0" Visibility="Collapsed"
                           AutomationProperties.AutomationId="LinkWarning"/>

                <Border Style="{StaticResource Card}" Margin="0,12,0,0">
                    <StackPanel>
                        <TextBlock x:Name="PlayersHeading" Style="{StaticResource OverlineText}"/>
                        <StackPanel x:Name="PlayerList" Margin="0,8,0,0"/>
                        <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,1,0,0" Margin="0,10,0,0" Padding="0,10,0,0">
                            <CheckBox x:Name="AllowEveryoneBox" Style="{StaticResource ToggleSwitch}"
                                      AutomationProperties.Name="Let everyone in" AutomationProperties.AutomationId="LetEveryoneIn">
                                <StackPanel>
                                    <TextBlock Text="Let everyone in"/>
                                    <TextBlock Text="No popup when someone joins" Style="{StaticResource CaptionText}"/>
                                </StackPanel>
                            </CheckBox>
                        </Border>
                    </StackPanel>
                </Border>

                <Expander Header="Stream stats" Margin="0,12,0,0" AutomationProperties.AutomationId="StreamStats">
                    <TextBlock x:Name="DetailsText" Style="{StaticResource MonoText}" TextWrapping="Wrap"/>
                </Expander>
            </StackPanel>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`src/CouchLink.App/Views/HostLobbyView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using CouchLink.App.Presentation;
using CouchLink.App.Ui;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App.Views;

/// <summary>
/// "Hosting on PC-03": stream settings (resolution, frame rate, quality) with a warning when the
/// network link is too slow, Let everyone in, the players with Kick, Stop hosting (which asks first
/// when players are in), and Stream stats. Owns the <see cref="HostService"/> and the approval popups.
/// </summary>
internal sealed partial class HostLobbyView : UserControl, IHostUi, IDisposable
{
    private readonly DispatcherTimer _details = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Dictionary<int, ApprovalPopup> _asks = [];
    private HostService? _host;

    public HostLobbyView()
    {
        InitializeComponent();
        Heading.Text = $"Hosting on {PcName.ThisPc}";
        foreach (var resolution in StreamSettings.Resolutions)
            ResolutionBox.Items.Add(new ComboBoxItem { Content = StreamSettings.Label(resolution), Tag = resolution });
        ResolutionBox.SelectedIndex = StreamSettings.Resolutions.ToList().IndexOf(StreamSettings.Default.Resolution);
        foreach (int rate in StreamSettings.FrameRatesFor(DisplayInfo.PrimaryRefreshRate()))
            FrameRateBox.Items.Add(new ComboBoxItem { Content = $"{rate} fps", Tag = rate });
        FrameRateBox.SelectedIndex = 0; // 60
        foreach (var quality in StreamQualities.All)
        {
            var item = new RadioButton
            {
                Content = StreamQualities.Label(quality),
                Tag = quality,
                GroupName = "Quality",
                Style = (Style)FindResource("SegmentedItem"),
                IsChecked = quality == StreamSettings.Default.Quality,
            };
            AutomationProperties.SetAutomationId(item, $"Quality{quality}");
            QualityGroup.Children.Add(item);
        }
        _details.Tick += (_, _) => UpdateDetails();
    }

    /// <summary>The host clicked Stop hosting; the view has already cleaned up.</summary>
    public event Action? Stopped;

    public bool TryStart(out string? error)
    {
        if (!HostService.TryStart(CurrentSettings(), this, out _host, out error))
            return false;
        ResolutionBox.SelectionChanged += (_, _) => _host?.ChangeSettings(CurrentSettings());
        FrameRateBox.SelectionChanged += (_, _) => _host?.ChangeSettings(CurrentSettings());
        foreach (var item in QualityGroup.Children.OfType<RadioButton>())
            item.Checked += (_, _) => _host?.ChangeSettings(CurrentSettings());
        AllowEveryoneBox.Click += (_, _) =>
        {
            if (_host is not null)
                _host.AllowEveryone = AllowEveryoneBox.IsChecked == true;
        };
        StopButton.Click += (_, _) => Stop();
        AppServices.DescribeMode = () => $"Host (virtual pads: {_host?.PadCount ?? 0})";
        AppServices.Log.Write("Hosting started");
        RefreshPlayers();
        UpdateDetails();
        _details.Start();
        return true;
    }

    internal StreamSettings CurrentSettings() => new(
        (StreamResolution)((ComboBoxItem)ResolutionBox.SelectedItem).Tag,
        (int)((ComboBoxItem)FrameRateBox.SelectedItem).Tag,
        (StreamQuality)QualityGroup.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Tag);

    private void Stop()
    {
        var names = _host?.Players.Select(p => p.Name).ToList() ?? [];
        if (StopHostingPrompt.For(names) is { } message
            && !ThemedDialog.Confirm(Window.GetWindow(this), "Stop hosting?", message, "Stop hosting", "Keep hosting", danger: true))
            return;
        Dispose();
        Stopped?.Invoke();
    }

    void IHostUi.PlayersChanged() => Dispatcher.InvokeAsync(RefreshPlayers);

    void IHostUi.AskHost(int connection, string name) => Dispatcher.InvokeAsync(() => Ask(connection, name));

    void IHostUi.CloseAsk(int connection) => Dispatcher.InvokeAsync(() =>
    {
        if (_asks.Remove(connection, out var popup))
            popup.CloseByHost();
    });

    private void Ask(int connection, string name)
    {
        if (_host is not { } host)
            return;
        var popup = new ApprovalPopup(name, _asks.Count, () => host.Allow(connection), () => host.Deny(connection));
        _asks[connection] = popup;
        popup.Closed += (_, _) => _asks.Remove(connection);
        popup.Show();
    }

    private void RefreshPlayers()
    {
        if (_host is not null)
            ShowPlayers(_host.Players);
    }

    internal void ShowPlayers(IReadOnlyList<PlayerInfo> players)
    {
        PlayersHeading.Text = $"PLAYERS {players.Count + 1} / {HostSession.Capacity + 1}";
        PlayerList.Children.Clear();
        PlayerList.Children.Add(PlayerRow(1, "You (host)", reconnecting: false, kickable: false));
        if (players.Count == 0)
        {
            PlayerList.Children.Add(new TextBlock
            {
                Text = "No one has joined yet. Players appear here when they join.",
                Style = (Style)FindResource("CaptionText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
        foreach (var player in players)
            PlayerList.Children.Add(PlayerRow(player.Slot, player.Name, player.State == PlayerState.Reserved, kickable: true));
    }

    private DockPanel PlayerRow(byte slot, string name, bool reconnecting, bool kickable)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        if (kickable)
        {
            var kick = new Button { Content = "Kick", Style = (Style)FindResource("GhostButton") };
            AutomationProperties.SetName(kick, $"Kick {name}");
            AutomationProperties.SetAutomationId(kick, $"Kick{slot}");
            kick.Click += (_, _) => _host?.Kick(slot);
            DockPanel.SetDock(kick, Dock.Right);
            row.Children.Add(kick);
        }
        if (reconnecting)
        {
            var pill = new StatusPill { Kind = PillKind.Reconnecting, Text = "Reconnecting", Margin = new Thickness(8, 0, 4, 0) };
            DockPanel.SetDock(pill, Dock.Right);
            row.Children.Add(pill);
        }
        var chip = new PlayerChip { Slot = slot };
        DockPanel.SetDock(chip, Dock.Left);
        row.Children.Add(chip);
        row.Children.Add(new TextBlock
        {
            Text = name,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return row;
    }

    private void UpdateDetails()
    {
        if (_host is null)
            return;
        DetailsText.Text = $"Virtual pads: {_host.PadCount}\n{_host.DescribeStreams()}" +
            (_host.LastError is { } error ? $"\nLast error: {error}" : "");
        var warning = _host.LinkWarning();
        BudgetWarning.Text = warning ?? "";
        BudgetWarning.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Dispose()
    {
        if (_host is not { } host)
            return;
        _host = null;
        _details.Stop();
        foreach (var popup in _asks.Values.ToList())
            popup.CloseByHost();
        _asks.Clear();
        host.Dispose(); // tells every client the session ended
        AppServices.DescribeMode = () => "Idle";
        AppServices.Log.Write("Hosting stopped");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.App/Views/HostLobbyView.xaml src/CouchLink.App/Views/HostLobbyView.xaml.cs tests/CouchLink.App.Tests/HostLobbyTests.cs
git commit -m "feat(app): host lobby with stream card, player chips and stop confirm"
```

---

### Task 9: Join list

**Files:**
- Modify (replace): `src/CouchLink.App/Views/JoinListView.xaml`, `src/CouchLink.App/Views/JoinListView.xaml.cs`
- Test: `tests/CouchLink.App.Tests/JoinListTests.cs`

**Interfaces:**
- Consumes: `AddressInput.TryParse`, `Banner`, `Spinner`, `StatusPill`, `Glyph`, `PlayerColors.BrushFor`, `HostList`, `FoundHost(string Name, IPAddress Address, int Players, int Capacity, bool Compatible)`, `DiscoveryListener`, `Ports.Discovery`.
- Produces: `JoinListView` keeps `BackClicked`, `JoinRequested(IPAddress, string)`, `ShowMessage(string?)`, `Dispose()`. New internal for tests: `void JoinByAddress()`, `void ShowHosts(IReadOnlyList<FoundHost> hosts)`, `void ShowNoHostsHelp()`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/JoinListTests.cs`:

```csharp
using System.Net;
using System.Windows;
using System.Windows.Controls;
using CouchLink.App.Theme;
using CouchLink.App.Views;
using CouchLink.Core.Session;

namespace CouchLink.App.Tests;

public class JoinListTests
{
    private static JoinListView List()
    {
        ThemeManager.Install(Application.Current);
        return new JoinListView();
    }

    [Fact]
    public void A_message_shows_in_the_error_banner_and_closes()
    {
        Wpf.Run(() =>
        {
            var list = List();
            list.ShowMessage("The host ended the session.");
            Assert.Equal(Visibility.Visible, list.MessageBar.Visibility);
            Assert.Equal("The host ended the session.", list.MessageBar.Text);
            list.MessageBar.CloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Collapsed, list.MessageBar.Visibility);
        });
    }

    [Fact]
    public void A_bad_address_shows_the_error_under_the_field_and_does_not_join()
    {
        Wpf.Run(() =>
        {
            var list = List();
            bool joined = false;
            list.JoinRequested += (_, _) => joined = true;
            list.AddressBox.Text = "192.168";
            list.JoinByAddress();
            Assert.False(joined);
            Assert.Equal(Visibility.Visible, list.AddressError.Visibility);
            Assert.Equal("Enter an IP address like 192.168.1.23.", list.AddressError.Text);
        });
    }

    [Fact]
    public void A_good_address_joins_and_clears_the_error()
    {
        Wpf.Run(() =>
        {
            var list = List();
            IPAddress? joined = null;
            list.JoinRequested += (address, _) => joined = address;
            list.AddressBox.Text = "1";
            list.JoinByAddress();
            list.AddressBox.Text = " 192.168.1.23 ";
            list.JoinByAddress();
            Assert.Equal(IPAddress.Parse("192.168.1.23"), joined);
            Assert.Equal(Visibility.Collapsed, list.AddressError.Visibility);
        });
    }

    [Fact]
    public void Hosts_become_cards_and_incompatible_ones_cannot_be_clicked()
    {
        Wpf.Run(() =>
        {
            var list = List();
            list.ShowHosts(
            [
                new FoundHost("PORTAL-SERVER", IPAddress.Parse("192.168.1.10"), 2, 9, true),
                new FoundHost("PC-09", IPAddress.Parse("192.168.1.19"), 0, 9, false),
            ]);
            var cards = list.HostButtons.Children.OfType<Button>().ToList();
            Assert.Equal(2, cards.Count);
            Assert.True(cards[0].IsHitTestVisible);
            Assert.False(cards[1].IsHitTestVisible);
            Assert.False(cards[1].Focusable);
            Assert.Equal("PORTAL-SERVER, 3 of 10 players", System.Windows.Automation.AutomationProperties.GetName(cards[0]));
        });
    }

    [Fact]
    public void The_help_card_shows_only_while_no_host_was_found()
    {
        Wpf.Run(() =>
        {
            var list = List();
            list.ShowNoHostsHelp();
            Assert.Equal(Visibility.Visible, list.HelpCard.Visibility);
            list.ShowHosts([new FoundHost("PC-02", IPAddress.Loopback, 0, 9, true)]);
            Assert.Equal(Visibility.Collapsed, list.HelpCard.Visibility);
            list.ShowNoHostsHelp();
            Assert.Equal(Visibility.Collapsed, list.HelpCard.Visibility);
        });
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter JoinListTests`
Expected: build FAILS: `'JoinListView' does not contain a definition for 'AddressError'`.

- [ ] **Step 3: Write the view**

`src/CouchLink.App/Views/JoinListView.xaml`:

```xml
<UserControl x:Class="CouchLink.App.Views.JoinListView" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:CouchLink.App.Ui">
    <DockPanel>
        <Border DockPanel.Dock="Bottom" Padding="16,0,16,16">
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock Text="JOIN BY ADDRESS" Style="{StaticResource OverlineText}"/>
                    <DockPanel Margin="0,8,0,0">
                        <Button x:Name="AddressJoinButton" DockPanel.Dock="Right" Content="Join" Style="{StaticResource PrimaryButton}"
                                Margin="8,0,0,0" AutomationProperties.AutomationId="AddressJoin"/>
                        <TextBox x:Name="AddressBox" ui:ThemeProps.Placeholder="192.168.1.23"
                                 AutomationProperties.Name="Host address" AutomationProperties.AutomationId="AddressBox"/>
                    </DockPanel>
                    <TextBlock x:Name="AddressError" Foreground="{DynamicResource DangerBrush}" FontSize="12" Margin="0,6,0,0"
                               TextWrapping="Wrap" Visibility="Collapsed" AutomationProperties.AutomationId="AddressError"/>
                </StackPanel>
            </Border>
        </Border>
        <StackPanel DockPanel.Dock="Top" Margin="16,16,16,0">
            <StackPanel Orientation="Horizontal">
                <Button x:Name="BackButton" Style="{StaticResource GhostButton}" Padding="6,0"
                        AutomationProperties.Name="Back" AutomationProperties.AutomationId="Back">
                    <ui:Glyph Data="{StaticResource IconArrowLeft}" Brush="{DynamicResource PrimaryTextBrush}"/>
                </Button>
                <TextBlock Text="Join a game" Style="{StaticResource TitleText}" Margin="6,0,0,0" VerticalAlignment="Center"/>
            </StackPanel>
            <ui:Banner x:Name="MessageBar" Kind="Error" CanClose="True" Margin="0,12,0,0" Visibility="Collapsed"
                       AutomationProperties.AutomationId="EndMessage"/>
            <ui:Banner x:Name="DiscoveryError" Kind="Warning" Margin="0,12,0,0" Visibility="Collapsed"
                       AutomationProperties.AutomationId="DiscoveryError"/>
            <StackPanel x:Name="ScanningRow" Orientation="Horizontal" Margin="2,14,0,0">
                <ui:Spinner/>
                <TextBlock Text="Looking for hosts on this network…" Style="{StaticResource BodyMutedText}" Margin="8,0,0,0" VerticalAlignment="Center"/>
            </StackPanel>
            <Border x:Name="HelpCard" Style="{StaticResource Card}" Margin="0,12,0,0" Visibility="Collapsed"
                    AutomationProperties.AutomationId="NoHostsHelp">
                <TextBlock Style="{StaticResource BodyMutedText}"
                           Text="No hosts yet. Check that the host clicked Host a game, that both PCs are on the same network, and that the firewall allows CouchLink. You can also join by address below."/>
            </Border>
        </StackPanel>
        <ScrollViewer VerticalScrollBarVisibility="Auto" Margin="16,12,16,12" Focusable="False">
            <StackPanel x:Name="HostButtons"/>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`src/CouchLink.App/Views/JoinListView.xaml.cs`:

```csharp
using System.Net;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CouchLink.App.Presentation;
using CouchLink.App.Ui;
using CouchLink.Core.Net;
using CouchLink.Core.Session;

namespace CouchLink.App.Views;

/// <summary>
/// The hosts on the LAN as cards ("PC-03 · 4/10 players"), heard on UDP 47800 while this view is shown,
/// plus Join by address for when broadcasts don't get through. An error banner says why the last
/// session ended; after 10 s with no host, a card says what to check.
/// </summary>
internal sealed partial class JoinListView : UserControl, IDisposable
{
    private readonly HostList _hosts = new(TimeProvider.System);
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _noHosts = new() { Interval = TimeSpan.FromSeconds(10) };
    private DiscoveryListener? _listener;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<FoundHost> _shown = [];

    public JoinListView()
    {
        InitializeComponent();
        BackButton.Click += (_, _) => BackClicked?.Invoke();
        MessageBar.CloseClicked += () => ShowMessage(null);
        AddressJoinButton.Click += (_, _) => JoinByAddress();
        AddressBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                JoinByAddress();
        };
        _refresh.Tick += (_, _) => Refresh();
        _noHosts.Tick += (_, _) =>
        {
            _noHosts.Stop();
            ShowNoHostsHelp();
        };
        Loaded += (_, _) => StartListening();
        Unloaded += (_, _) => Dispose();
    }

    public event Action? BackClicked;
    public event Action<IPAddress, string>? JoinRequested;

    public void ShowMessage(string? message)
    {
        MessageBar.Text = message ?? "";
        MessageBar.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StartListening()
    {
        if (_listener is not null)
            return;
        if (!DiscoveryListener.TryCreate(Ports.Discovery, out _listener, out var error))
        {
            ScanningRow.Visibility = Visibility.Collapsed;
            DiscoveryError.Text = $"{error} Join by address still works.";
            DiscoveryError.Visibility = Visibility.Visible;
            return;
        }
        _cts = new CancellationTokenSource();
        _ = _listener!.RunAsync((announce, from) => _hosts.Seen(announce, from), _cts.Token,
            e => AppServices.Log.Write($"Discovery error: {e}"));
        _refresh.Start();
        _noHosts.Start();
    }

    private void Refresh()
    {
        var hosts = _hosts.Current();
        if (!hosts.SequenceEqual(_shown))
            ShowHosts(hosts);
    }

    internal void ShowHosts(IReadOnlyList<FoundHost> hosts)
    {
        _shown = hosts;
        if (hosts.Count > 0)
            HelpCard.Visibility = Visibility.Collapsed;
        HostButtons.Children.Clear();
        foreach (var host in hosts)
            HostButtons.Children.Add(HostCard(host));
    }

    internal void ShowNoHostsHelp()
    {
        if (_shown.Count == 0)
            HelpCard.Visibility = Visibility.Visible;
    }

    private Button HostCard(FoundHost host)
    {
        int players = host.Players + 1, capacity = host.Capacity + 1;
        var card = new Button { Style = (Style)FindResource("HostCardButton"), Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(card, $"Host_{host.Name}");
        AutomationProperties.SetName(card, host.Compatible
            ? $"{host.Name}, {players} of {capacity} players"
            : $"{host.Name}, different CouchLink version");

        var tile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9) };
        tile.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
        var monitor = new Glyph();
        monitor.SetResourceReference(Glyph.DataProperty, "IconMonitor");
        tile.Child = monitor;
        DockPanel.SetDock(tile, Dock.Left);

        UIElement end;
        if (host.Compatible)
        {
            var chevron = new Glyph { Width = 16, Height = 16 };
            chevron.SetResourceReference(Glyph.DataProperty, "IconChevronRight");
            chevron.SetResourceReference(Glyph.BrushProperty, "PrimaryTextBrush");
            end = chevron;
        }
        else
        {
            end = new StatusPill { Text = "Different CouchLink version" };
        }
        DockPanel.SetDock(end, Dock.Right);

        var name = new TextBlock { Text = host.Name, FontWeight = FontWeights.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
        var info = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        if (host.Compatible)
        {
            // Discovery sends a count, not slots: the dots show how full the game is, not who is in it.
            for (int slot = 1; slot <= Math.Min(players, 10); slot++)
                info.Children.Add(new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 0, 3, 0),
                    Background = PlayerColors.BrushFor((byte)slot),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            var count = new TextBlock { Text = $"{players} / {capacity} players", FontSize = 12, Margin = new Thickness(4, 0, 0, 0) };
            count.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            info.Children.Add(count);
        }
        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);
        if (host.Compatible)
            text.Children.Add(info);

        var row = new DockPanel();
        row.Children.Add(tile);
        row.Children.Add(end);
        row.Children.Add(text);
        card.Content = row;

        if (host.Compatible)
        {
            var chosen = host;
            card.Click += (_, _) => JoinRequested?.Invoke(chosen.Address, chosen.Name);
        }
        else
        {
            card.IsHitTestVisible = false;
            card.Focusable = false;
            card.Opacity = 0.6;
        }
        return card;
    }

    internal void JoinByAddress()
    {
        if (AddressInput.TryParse(AddressBox.Text, out var address, out var error))
        {
            AddressError.Visibility = Visibility.Collapsed;
            JoinRequested?.Invoke(address, address.ToString());
        }
        else
        {
            AddressError.Text = error;
            AddressError.Visibility = Visibility.Visible;
        }
    }

    public void Dispose()
    {
        _refresh.Stop();
        _noHosts.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _listener?.Dispose(); // frees UDP 47800 for the next list
        _listener = null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.App/Views/JoinListView.xaml src/CouchLink.App/Views/JoinListView.xaml.cs tests/CouchLink.App.Tests/JoinListTests.cs
git commit -m "feat(app): join list with host cards, scanning row and address check"
```

---

### Task 10: Session screen

**Files:**
- Modify (replace): `src/CouchLink.App/Views/SessionView.xaml`, `src/CouchLink.App/Views/SessionView.xaml.cs`
- Test: `tests/CouchLink.App.Tests/SessionScreenTests.cs`

**Interfaces:**
- Consumes: `SessionText.For`, `StepTracker`, `PlayerChip`, `Spinner`, `StatusPill`, `KeyCap`.
- Produces: `SessionView` keeps `LeaveClicked`, `ControlsClicked`, `Details` (set), `Show(ClientState, string host, byte slot)`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/SessionScreenTests.cs`:

```csharp
using System.Windows;
using CouchLink.App.Theme;
using CouchLink.App.Views;
using CouchLink.Core.Session;

namespace CouchLink.App.Tests;

public class SessionScreenTests
{
    private static SessionView View()
    {
        ThemeManager.Install(Application.Current);
        return new SessionView();
    }

    [Fact]
    public void Waiting_shows_step_two_a_spinner_and_cancel()
    {
        Wpf.Run(() =>
        {
            var view = View();
            view.Show(ClientState.Waiting, "PORTAL-SERVER", 0);
            Assert.Equal(1, view.Steps.Current);
            Assert.Equal("Waiting for PORTAL-SERVER to let you in", view.Heading.Text);
            Assert.Equal("Cancel", view.LeaveButton.Content);
            Assert.Equal(Visibility.Visible, view.Busy.Visibility);
            Assert.Equal(Visibility.Collapsed, view.Chip.Visibility);
            Assert.Equal(Visibility.Collapsed, view.Shortcuts.Visibility);
        });
    }

    [Fact]
    public void Playing_shows_the_player_chip_and_the_shortcuts()
    {
        Wpf.Run(() =>
        {
            var view = View();
            view.Show(ClientState.Playing, "PORTAL-SERVER", 3);
            Assert.Equal(2, view.Steps.Current);
            Assert.Equal("You're P3", view.Heading.Text);
            Assert.Equal("Leave", view.LeaveButton.Content);
            Assert.Equal(Visibility.Visible, view.Chip.Visibility);
            Assert.Equal(3, view.Chip.Slot);
            Assert.Equal(Visibility.Collapsed, view.Busy.Visibility);
            Assert.Equal(Visibility.Visible, view.Shortcuts.Visibility);
            Assert.Equal(Visibility.Collapsed, view.ReconnectingPill.Visibility);
        });
    }

    [Fact]
    public void Reconnecting_shows_the_pill()
    {
        Wpf.Run(() =>
        {
            var view = View();
            view.Show(ClientState.Reconnecting, "PORTAL-SERVER", 3);
            Assert.Equal(Visibility.Visible, view.ReconnectingPill.Visibility);
            Assert.Equal("Leave", view.LeaveButton.Content);
        });
    }

    [Fact]
    public void Ended_keeps_what_is_shown()
    {
        Wpf.Run(() =>
        {
            var view = View();
            view.Show(ClientState.Playing, "PORTAL-SERVER", 3);
            view.Show(ClientState.Ended, "PORTAL-SERVER", 3);
            Assert.Equal("You're P3", view.Heading.Text);
        });
    }

    [Fact]
    public void Nothing_on_the_screen_can_take_keyboard_focus()
    {
        Wpf.Run(() =>
        {
            var view = View();
            Assert.False(view.LeaveButton.Focusable);
            Assert.False(view.ControlsButton.Focusable);
            Assert.False(view.DetailsExpander.Focusable);
        });
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter SessionScreenTests`
Expected: build FAILS: `'SessionView' does not contain a definition for 'Steps'`.

- [ ] **Step 3: Write the view**

`src/CouchLink.App/Views/SessionView.xaml`:

```xml
<UserControl x:Class="CouchLink.App.Views.SessionView" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:CouchLink.App.Ui">
    <!-- Nothing here is focusable: Raw Input keys still reach a focused control, so Space or Enter in the game must not press it. -->
    <DockPanel>
        <Border DockPanel.Dock="Bottom" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,1,0,0" Padding="16,12">
            <UniformGrid Rows="1">
                <Button x:Name="ControlsButton" Content="Controls" Focusable="False" MinHeight="40" Margin="0,0,4,0"
                        AutomationProperties.AutomationId="SessionControls"/>
                <Button x:Name="LeaveButton" Style="{StaticResource DangerButton}" Focusable="False" MinHeight="40" Margin="4,0,0,0"
                        AutomationProperties.AutomationId="SessionLeave"/>
            </UniformGrid>
        </Border>
        <StackPanel Margin="24,16" VerticalAlignment="Center">
            <ui:StepTracker x:Name="Steps"/>
            <DockPanel Margin="0,28,0,0">
                <ui:PlayerChip x:Name="Chip" Large="True" DockPanel.Dock="Left" Margin="0,0,16,0" Visibility="Collapsed"/>
                <ui:Spinner x:Name="Busy" DockPanel.Dock="Left" Margin="0,0,12,0"/>
                <StackPanel VerticalAlignment="Center">
                    <TextBlock x:Name="Heading" Style="{StaticResource TitleText}" TextWrapping="Wrap"/>
                    <TextBlock x:Name="Hint" Style="{StaticResource BodyMutedText}" Margin="0,4,0,0"/>
                    <ui:StatusPill x:Name="ReconnectingPill" Kind="Reconnecting" Text="Reconnecting" HorizontalAlignment="Left"
                                   Margin="0,8,0,0" Visibility="Collapsed"/>
                </StackPanel>
            </DockPanel>
            <Border x:Name="Shortcuts" Style="{StaticResource Card}" Margin="0,20,0,0" Visibility="Collapsed">
                <StackPanel>
                    <DockPanel>
                        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                            <ui:KeyCap Key="Ctrl"/><ui:KeyCap Key="Alt"/><ui:KeyCap Key="Q"/>
                        </StackPanel>
                        <TextBlock Text="Leave the game" VerticalAlignment="Center"/>
                    </DockPanel>
                    <DockPanel Margin="0,8,0,0">
                        <ui:KeyCap DockPanel.Dock="Right" Key="F1"/>
                        <TextBlock Text="Show the keys" VerticalAlignment="Center"/>
                    </DockPanel>
                    <DockPanel Margin="0,8,0,0">
                        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                            <ui:KeyCap Key="Ctrl"/><ui:KeyCap Key="Alt"/><ui:KeyCap Key="C"/>
                        </StackPanel>
                        <TextBlock Text="Change keys" VerticalAlignment="Center"/>
                    </DockPanel>
                    <DockPanel Margin="0,8,0,0">
                        <ui:KeyCap DockPanel.Dock="Right" Key="F2"/>
                        <TextBlock Text="Stream stats" VerticalAlignment="Center"/>
                    </DockPanel>
                </StackPanel>
            </Border>
            <Expander x:Name="DetailsExpander" Header="Connection details" Margin="0,16,0,0" Focusable="False"
                      AutomationProperties.AutomationId="ConnectionDetails">
                <TextBlock x:Name="DetailsText" Style="{StaticResource MonoText}" TextWrapping="Wrap"/>
            </Expander>
        </StackPanel>
    </DockPanel>
</UserControl>
```

`src/CouchLink.App/Views/SessionView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using CouchLink.App.Presentation;
using CouchLink.Core.Session;

namespace CouchLink.App.Views;

/// <summary>The main window while joining or playing: the steps, what is happening, the shortcuts, and Cancel or Leave.</summary>
internal sealed partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
        LeaveButton.Click += (_, _) => LeaveClicked?.Invoke();
        ControlsButton.Click += (_, _) => ControlsClicked?.Invoke();
    }

    public event Action? LeaveClicked;
    public event Action? ControlsClicked;

    public string Details
    {
        set => DetailsText.Text = value;
    }

    public void Show(ClientState state, string host, byte slot)
    {
        if (SessionText.For(state, host, slot) is not { } text)
            return;
        Steps.Current = text.Step;
        Heading.Text = text.Heading;
        Hint.Text = text.Hint;
        Hint.Visibility = text.Hint.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        LeaveButton.Content = text.LeaveText;
        Chip.Slot = slot;
        Chip.Visibility = text.Playing ? Visibility.Visible : Visibility.Collapsed;
        Busy.Visibility = text.Playing ? Visibility.Collapsed : Visibility.Visible;
        Shortcuts.Visibility = text.Playing ? Visibility.Visible : Visibility.Collapsed;
        ReconnectingPill.Visibility = text.Reconnecting ? Visibility.Visible : Visibility.Collapsed;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.App/Views/SessionView.xaml src/CouchLink.App/Views/SessionView.xaml.cs tests/CouchLink.App.Tests/SessionScreenTests.cs
git commit -m "feat(app): session screen with steps, player chip and shortcuts"
```

---

### Task 11: Controls editor

**Files:**
- Delete: `src/CouchLink.App/ControlsWindow.cs`
- Create: `src/CouchLink.App/ControlsWindow.xaml`, `src/CouchLink.App/ControlsWindow.xaml.cs`
- Test: `tests/CouchLink.App.Tests/ControlsEditorTests.cs`

**Interfaces:**
- Consumes: `ControlFilter.RowName/Matches`, `BindMessage.Reserved/Moved`, `KeyCap`, `Banner`, `AppServices.Controls` (`ControlSettings`: `Bind(PadControl, ushort) → BindResult(bool Bound, PadControl? MovedFrom)`, `Layout.KeysFor(PadControl) → IReadOnlyList<ushort>`, `LabelFor`, `SetLabel`, `SensitivityStep`, `SetSensitivityStep`, `InvertY`, `SetInvertY`, `ResetToDefault`, `Apply`, `ToProfile`, `ProfileName`, `ProfileGame`, `ProfileChanged`, `Changed`), `ProfileStore`, `KeyNames.Groups`.
- Produces: `ControlsWindow.Open(Window? owner, bool overGame = false, Action? closed = null)` unchanged. Internal for tests: ctor `ControlsWindow()`, `void Listen(PadControl)`, `void Bind(PadControl, ushort)`, `bool IsRowVisible(PadControl)`, `bool IsGroupVisible(string group)`, `PadControl? Moved`, `Banner MessageBanner` (generated), `TextBox FindBox` (generated).

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/ControlsEditorTests.cs`:

```csharp
using System.Windows;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.Core.Input;

namespace CouchLink.App.Tests;

/// <summary>The editor edits the app-wide AppServices.Controls, so each test resets it.</summary>
public class ControlsEditorTests
{
    // Default layout (KeyLayout.Defaults): Cross = K only, Square = J and left click. Num 5 is free.
    private const ushort K = 0x4B, J = 0x4A, Num5 = 0x65, Esc = 0x1B;

    private static void WithEditor(Action<ControlsWindow> test)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            AppServices.Controls.ResetToDefault();
            var editor = new ControlsWindow();
            try
            {
                test(editor);
            }
            finally
            {
                editor.Close();
                AppServices.Controls.ResetToDefault();
            }
        });
    }

    [Fact]
    public void Find_hides_rows_and_groups_that_do_not_match()
    {
        WithEditor(editor =>
        {
            editor.FindBox.Text = "square";
            Assert.True(editor.IsRowVisible(PadControl.Square));
            Assert.False(editor.IsRowVisible(PadControl.Cross));
            Assert.True(editor.IsGroupVisible("Buttons"));
            Assert.False(editor.IsGroupVisible("Left stick"));
            editor.FindBox.Text = "";
            Assert.True(editor.IsRowVisible(PadControl.Cross));
            Assert.True(editor.IsGroupVisible("Left stick"));
        });
    }

    [Fact]
    public void Find_matches_labels()
    {
        WithEditor(editor =>
        {
            AppServices.Controls.SetLabel(PadControl.Square, "Shoot");
            editor.FindBox.Text = "shoot";
            Assert.True(editor.IsRowVisible(PadControl.Square));
            Assert.False(editor.IsRowVisible(PadControl.Circle));
        });
    }

    [Fact]
    public void Moving_a_key_marks_the_row_that_lost_it_and_says_so()
    {
        WithEditor(editor =>
        {
            editor.Listen(PadControl.Circle);
            editor.Bind(PadControl.Circle, K); // K is Cross's only key
            Assert.Equal(PadControl.Cross, editor.Moved);
            Assert.Equal(BannerKind.Warning, editor.MessageBanner.Kind);
            Assert.Equal("K moved here from Cross. Cross has no key now.", editor.MessageBanner.Text);
            Assert.Equal(Visibility.Visible, editor.MessageBanner.Visibility);
        });
    }

    [Fact]
    public void A_control_that_keeps_another_key_is_not_called_keyless()
    {
        WithEditor(editor =>
        {
            editor.Listen(PadControl.Circle);
            editor.Bind(PadControl.Circle, J); // Square keeps left click
            Assert.Equal(PadControl.Square, editor.Moved);
            Assert.Equal("J moved here from Square.", editor.MessageBanner.Text);
        });
    }

    [Fact]
    public void A_reserved_key_keeps_listening_and_says_why()
    {
        WithEditor(editor =>
        {
            editor.Listen(PadControl.Square);
            editor.Bind(PadControl.Square, Esc);
            Assert.Equal(PadControl.Square, editor.Listening);
            Assert.Equal("Esc is reserved. Press another key.", editor.MessageBanner.Text);
        });
    }

    [Fact]
    public void A_free_key_binds_without_a_warning()
    {
        WithEditor(editor =>
        {
            editor.Listen(PadControl.Square);
            editor.Bind(PadControl.Square, Num5);
            Assert.Null(editor.Moved);
            Assert.Null(editor.Listening);
            Assert.Equal(new ushort[] { Num5 }, AppServices.Controls.Layout.KeysFor(PadControl.Square)); // Bind replaces the row's keys
        });
    }
}
```

(In the real window Esc cancels listening before `Bind` is called; the reserved test calls `Bind` directly because `KeyLayout` also reserves Esc, F1, F2 and both Windows keys, and the message must be right for all of them.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter ControlsEditorTests`
Expected: build FAILS: `'ControlsWindow.ControlsWindow()' is inaccessible due to its protection level` (and missing `FindBox`).

- [ ] **Step 3: Write the window XAML**

Delete `src/CouchLink.App/ControlsWindow.cs`. Create `src/CouchLink.App/ControlsWindow.xaml`:

```xml
<Window x:Class="CouchLink.App.ControlsWindow" x:ClassModifier="internal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:CouchLink.App.Ui"
        Title="Controls" Width="460" SizeToContent="Height" ResizeMode="NoResize" WindowStartupLocation="CenterScreen">
    <DockPanel Margin="16">
        <StackPanel DockPanel.Dock="Top">
            <DockPanel>
                <Button x:Name="SaveAsButton" DockPanel.Dock="Right" Content="Save as…" Margin="8,0,0,0" AutomationProperties.AutomationId="SaveAs"/>
                <Button x:Name="BrowseButton" DockPanel.Dock="Right" Content="Browse…" Margin="8,0,0,0" AutomationProperties.AutomationId="Browse"/>
                <ComboBox x:Name="ProfileBox" AutomationProperties.Name="Profile" AutomationProperties.AutomationId="Profile"/>
            </DockPanel>
            <DockPanel Margin="0,10,0,0">
                <CheckBox x:Name="LabelsToggle" DockPanel.Dock="Right" Style="{StaticResource ToggleSwitch}" Content="Labels"
                          Margin="12,0,0,0" VerticalAlignment="Center" AutomationProperties.AutomationId="ShowLabels"
                          ToolTip="Name what each button does in the game, e.g. Shoot"/>
                <TextBox x:Name="FindBox" ui:ThemeProps.Placeholder="Find a control or label"
                         AutomationProperties.Name="Find a control or label" AutomationProperties.AutomationId="Find"/>
            </DockPanel>
            <ui:Banner x:Name="MessageBanner" Margin="0,10,0,0" Visibility="Collapsed" AutomationProperties.AutomationId="EditorMessage"/>
        </StackPanel>
        <StackPanel DockPanel.Dock="Bottom">
            <Border Style="{StaticResource Card}" Margin="0,12,0,0">
                <StackPanel>
                    <TextBlock Text="MOUSE (RIGHT STICK)" Style="{StaticResource OverlineText}"/>
                    <DockPanel Margin="0,10,0,0">
                        <TextBlock Text="Sensitivity" DockPanel.Dock="Left" Width="84" VerticalAlignment="Center"/>
                        <TextBlock x:Name="SensitivityValue" DockPanel.Dock="Right" Style="{StaticResource MonoText}" Width="24"
                                   TextAlignment="Right" VerticalAlignment="Center"/>
                        <Slider x:Name="SensitivitySlider" TickFrequency="1" IsSnapToTickEnabled="True"
                                AutomationProperties.Name="Mouse sensitivity" AutomationProperties.AutomationId="Sensitivity"/>
                    </DockPanel>
                    <CheckBox x:Name="InvertYToggle" Style="{StaticResource ToggleSwitch}" Margin="0,12,0,0"
                              AutomationProperties.Name="Invert Y" AutomationProperties.AutomationId="InvertY">
                        <StackPanel>
                            <TextBlock Text="Invert Y"/>
                            <TextBlock Text="Mouse toward you pushes the stick up" Style="{StaticResource CaptionText}"/>
                        </StackPanel>
                    </CheckBox>
                </StackPanel>
            </Border>
            <DockPanel Margin="0,12,0,0">
                <Button x:Name="DoneButton" DockPanel.Dock="Right" Content="Done" Style="{StaticResource PrimaryButton}" MinWidth="96"
                        AutomationProperties.AutomationId="Done"/>
                <Button x:Name="ResetButton" Content="Reset to default" Style="{StaticResource GhostButton}" HorizontalAlignment="Left"
                        AutomationProperties.AutomationId="Reset"/>
            </DockPanel>
        </StackPanel>
        <ScrollViewer x:Name="ListScroller" Margin="0,4,0,0" VerticalScrollBarVisibility="Auto" Focusable="False">
            <StackPanel x:Name="GroupList"/>
        </ScrollViewer>
    </DockPanel>
</Window>
```

- [ ] **Step 4: Write the code-behind**

`src/CouchLink.App/ControlsWindow.xaml.cs` (profile handling is moved over from the old file unchanged; rows, messages and Find are new):

```csharp
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using CouchLink.App.Presentation;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.Core.Input;
using Microsoft.Win32;

namespace CouchLink.App;

/// <summary>
/// Controls: every DS4 control in grouped cards with its keys. Click a row, then press a key or mouse
/// button to bind it (Esc cancels); when the key came from another control, that row lights up and the
/// banner says so. A profile bar loads a profile from the profiles folder or a file and saves the
/// current controls as one; Find filters the rows; Labels adds a box per row for the game's name of
/// each button. Mouse sensitivity, Invert Y and Reset to default. Edits go straight into
/// <see cref="AppServices.Controls"/>, so they count mid-game. One window at a time.
/// </summary>
internal sealed partial class ControlsWindow : Window
{
    private const string ListeningText = "Press a key or mouse button · Esc cancels";
    private const string FileFilter = "CouchLink profile (*.json)|*.json";
    private const double NarrowWidth = 460, WideWidth = 600;
    private static ControlsWindow? _open;

    /// <summary>The profile file loaded last this run. <see cref="ControlSettings.ProfileName"/> says whether it still is.</summary>
    private static ProfileChoice? _loaded;

    private readonly ProfileStore _store = new(ProfileStore.DefaultFolder, AppServices.Log.Write);
    private readonly ControlSettings _settings = AppServices.Controls;
    private readonly Dictionary<PadControl, Button> _rows = [];
    private readonly Dictionary<PadControl, DockPanel> _lines = [];
    private readonly Dictionary<PadControl, TextBox> _labelBoxes = [];
    private readonly Dictionary<PadControl, string> _groupOf = [];
    private readonly List<(string Name, TextBlock Heading, Border Card, PadControl[] Controls)> _groups = [];
    private readonly DispatcherTimer _messageTimer = new();
    private readonly DispatcherTimer _movedTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private IReadOnlyList<ProfileEntry> _entries = [];
    private bool _updatingProfiles;

    private sealed record ProfileChoice(string Text, string? Path)
    {
        public static readonly ProfileChoice Default = new("Default layout", null);
        public override string ToString() => Text;
    }

    /// <summary>Shows the editor, or brings the open one forward. <paramref name="overGame"/>: opened with Ctrl+Alt+C over the fullscreen player.</summary>
    public static void Open(Window? owner, bool overGame = false, Action? closed = null)
    {
        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }
        var window = new ControlsWindow { Topmost = overGame };
        if (owner is { IsVisible: true, WindowState: not WindowState.Minimized } && !overGame)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        window.Closed += (_, _) =>
        {
            _open = null;
            closed?.Invoke();
        };
        _open = window;
        window.Show();
        window.Activate();
    }

    /// <summary>Internal for tests; use <see cref="Open"/>.</summary>
    internal ControlsWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        MaxHeight = SystemParameters.WorkArea.Height * 0.85;
        BuildRows();

        SensitivitySlider.Minimum = ControlSettings.MinStep;
        SensitivitySlider.Maximum = ControlSettings.MaxStep;
        SensitivitySlider.ValueChanged += (_, e) => _settings.SetSensitivityStep((int)Math.Round(e.NewValue));
        InvertYToggle.Click += (_, _) => _settings.SetInvertY(InvertYToggle.IsChecked == true);
        ResetButton.Click += (_, _) =>
        {
            Listening = null;
            _settings.ResetToDefault();
            ShowMessage("Back to the default controls.", BannerKind.Info);
        };
        DoneButton.Click += (_, _) => Close();
        BrowseButton.Click += (_, _) => Browse();
        SaveAsButton.Click += (_, _) => SaveAs();
        ProfileBox.SelectionChanged += (_, _) => OnProfileChosen();
        // Checked/Unchecked, not Click: screen readers and other accessibility tools tick it without a click
        LabelsToggle.Checked += (_, _) => ShowLabels(true);
        LabelsToggle.Unchecked += (_, _) => ShowLabels(false);
        FindBox.TextChanged += (_, _) => ApplyFilter();
        FindBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && FindBox.Text.Length > 0)
            {
                FindBox.Text = "";
                e.Handled = true;
            }
        };

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            MessageBanner.Visibility = Visibility.Collapsed;
        };
        _movedTimer.Tick += (_, _) =>
        {
            _movedTimer.Stop();
            Moved = null;
            Refresh();
        };
        _settings.Changed += OnSettingsChanged;
        Closing += (_, _) =>
        {
            foreach (var control in _labelBoxes.Keys)
                CommitLabel(control); // a label typed just before closing is kept
        };
        Closed += (_, _) =>
        {
            _settings.Changed -= OnSettingsChanged;
            _messageTimer.Stop();
            _movedTimer.Stop();
        };
        _entries = _store.List();
        Refresh();
    }

    /// <summary>The row waiting for a key, or null.</summary>
    internal PadControl? Listening { get; private set; }

    /// <summary>The row that just lost its key to another one; tinted for 4 s.</summary>
    internal PadControl? Moved { get; private set; }

    internal bool IsRowVisible(PadControl control) => _lines[control].Visibility == Visibility.Visible;

    internal bool IsGroupVisible(string group) => _groups.First(g => g.Name == group).Card.Visibility == Visibility.Visible;

    private void BuildRows()
    {
        foreach (var (group, controls) in KeyNames.Groups)
        {
            var heading = new TextBlock { Text = group.ToUpperInvariant(), Style = (Style)FindResource("OverlineText"), Margin = new Thickness(4, 12, 0, 6) };
            var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(4) };
            var rows = new StackPanel();
            foreach (var control in controls)
            {
                var c = control;
                var row = new Button { Style = (Style)FindResource("ControlRowButton") };
                AutomationProperties.SetAutomationId(row, $"Row_{control}");
                AutomationProperties.SetName(row, KeyNames.Of(control));
                row.Click += (_, _) => Listen(c);
                _rows[control] = row;

                var label = new TextBox
                {
                    MaxLength = ProfileFile.MaxLabelLength,
                    Width = 140,
                    Margin = new Thickness(6, 2, 2, 2),
                    Visibility = Visibility.Collapsed,
                    ToolTip = "What this button does in the game, e.g. Shoot",
                };
                ThemeProps.SetPlaceholder(label, "Label");
                AutomationProperties.SetName(label, $"Label for {KeyNames.Of(control)}");
                AutomationProperties.SetAutomationId(label, $"Label_{control}");
                label.LostFocus += (_, _) => CommitLabel(c);
                label.KeyDown += (_, e) =>
                {
                    if (e.Key != Key.Enter)
                        return;
                    CommitLabel(c);
                    e.Handled = true;
                };
                _labelBoxes[control] = label;

                var line = new DockPanel();
                DockPanel.SetDock(label, Dock.Right);
                line.Children.Add(label);
                line.Children.Add(row);
                _lines[control] = line;
                _groupOf[control] = group;
                rows.Children.Add(line);
            }
            card.Child = rows;
            GroupList.Children.Add(heading);
            GroupList.Children.Add(card);
            _groups.Add((group, heading, card, controls));
        }
    }

    private void OnSettingsChanged() => Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        foreach (var (control, row) in _rows)
        {
            row.Content = RowContent(control);
            PaintRow(control, row);
        }
        foreach (var (control, box) in _labelBoxes)
            if (!box.IsKeyboardFocusWithin) // don't overwrite what is being typed
                box.Text = _settings.LabelFor(control) ?? "";
        SensitivitySlider.Value = _settings.SensitivityStep;
        SensitivityValue.Text = _settings.SensitivityStep.ToString();
        InvertYToggle.IsChecked = _settings.InvertY;
        RefreshProfiles();
        ApplyFilter();
    }

    private DockPanel RowContent(PadControl control)
    {
        var panel = new DockPanel();
        if (Listening == control)
        {
            var hint = new TextBlock { Text = ListeningText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
            DockPanel.SetDock(hint, Dock.Right);
            panel.Children.Add(hint);
        }
        else
        {
            var keys = new StackPanel { Orientation = Orientation.Horizontal };
            var bound = _settings.Layout.KeysFor(control);
            if (bound.Count == 0)
                keys.Children.Add(new KeyCap());
            foreach (var key in bound)
                keys.Children.Add(new KeyCap { Key = KeyNames.Of(key) });
            DockPanel.SetDock(keys, Dock.Right);
            panel.Children.Add(keys);
        }
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Inlines.Add(new Run(ControlFilter.RowName(control)));
        if (LabelsToggle.IsChecked != true && _settings.LabelFor(control) is { } label)
        {
            var run = new Run($"  ·  {label}");
            run.SetResourceReference(TextElement.ForegroundProperty, "TextMutedBrush");
            name.Inlines.Add(run);
        }
        panel.Children.Add(name);
        return panel;
    }

    private void PaintRow(PadControl control, Button row)
    {
        if (Listening == control)
        {
            row.SetResourceReference(BackgroundProperty, "ListeningFillBrush");
            row.SetResourceReference(BorderBrushProperty, "PrimaryTextBrush");
        }
        else if (Moved == control)
        {
            row.SetResourceReference(BackgroundProperty, "WarningBannerFillBrush");
            row.SetResourceReference(BorderBrushProperty, "WarningBannerBorderBrush");
        }
        else
        {
            row.ClearValue(BackgroundProperty);
            row.ClearValue(BorderBrushProperty);
        }
    }

    private void ApplyFilter()
    {
        string query = FindBox.Text;
        foreach (var (name, heading, card, controls) in _groups)
        {
            bool any = false;
            foreach (var control in controls)
            {
                bool match = ControlFilter.Matches(control, name, _settings.LabelFor(control), query);
                _lines[control].Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                any |= match;
            }
            heading.Visibility = card.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Rebuilds the dropdown: Default layout, the folder's profiles, and the loaded file if it is elsewhere.</summary>
    private void RefreshProfiles()
    {
        var choices = new List<ProfileChoice> { ProfileChoice.Default };
        choices.AddRange(_entries.Select(e => new ProfileChoice(e.DisplayName, e.Path)));
        int selected = 0;
        if (_settings.ProfileName is not null && _loaded is { Path: { } loadedPath } loaded)
        {
            selected = choices.FindIndex(c => c.Path is not null && SamePath(c.Path, loadedPath));
            if (selected < 0)
            {
                choices.Add(loaded);
                selected = choices.Count - 1;
            }
            if (_settings.ProfileChanged)
                choices[selected] = choices[selected] with { Text = choices[selected].Text + " (changed)" };
        }
        _updatingProfiles = true;
        try
        {
            ProfileBox.ItemsSource = choices;
            ProfileBox.SelectedIndex = selected;
        }
        finally
        {
            _updatingProfiles = false;
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void OnProfileChosen()
    {
        if (_updatingProfiles || ProfileBox.SelectedItem is not ProfileChoice choice)
            return;
        Listening = null;
        if (choice.Path is null)
        {
            _settings.ResetToDefault();
            ShowMessage("Back to the default controls.", BannerKind.Info);
            return;
        }
        Load(choice.Path);
    }

    /// <summary>Reads the file again (it may have changed since the list was made) and applies it, or says why not.</summary>
    private void Load(string path)
    {
        var result = ProfileStore.Read(path);
        if (result.Profile is not { } profile)
        {
            AppServices.Log.Write($"profile not loaded: {path}: {result.Error}");
            ShowError(result.Error!);
            RefreshProfiles(); // the dropdown goes back to what is really loaded
            return;
        }
        _loaded = new ProfileChoice(profile.Name, path);
        _settings.Apply(profile);
        ShowMessage($"Loaded {profile.Name}.", BannerKind.Info);
    }

    private void Browse()
    {
        Listening = null;
        Refresh();
        var dialog = new OpenFileDialog { Title = "Load a controller profile", Filter = FileFilter };
        if (Directory.Exists(_store.Folder))
            dialog.InitialDirectory = _store.Folder;
        if (dialog.ShowDialog(this) == true)
            Load(dialog.FileName);
    }

    private void SaveAs()
    {
        Listening = null;
        Refresh();
        var details = new SaveProfileDialog(_settings.ProfileName, _settings.ProfileGame) { Owner = this, Topmost = Topmost };
        if (details.ShowDialog() != true)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "Save controller profile",
            Filter = FileFilter,
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = ProfileStore.SuggestedFileName(details.ProfileName),
            InitialDirectory = _store.TryCreateFolder()
                ? _store.Folder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var profile = _settings.ToProfile(details.ProfileName, details.Game);
        if (_store.Write(dialog.FileName, profile) is { } error)
        {
            ShowError(error);
            return;
        }
        _entries = _store.List();
        _loaded = new ProfileChoice(profile.Name, dialog.FileName);
        _settings.Apply(profile); // the saved file is now the loaded profile, unchanged
        ShowMessage($"Saved {Path.GetFileName(dialog.FileName)}.", BannerKind.Info);
    }

    private void ShowLabels(bool show)
    {
        foreach (var box in _labelBoxes.Values)
            box.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Width = show ? WideWidth : NarrowWidth;
        Refresh(); // the name column drops or shows the label text
    }

    private void CommitLabel(PadControl control) => _settings.SetLabel(control, _labelBoxes[control].Text);

    internal void Listen(PadControl control)
    {
        Listening = control;
        Refresh();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Listening is not { } control)
            return;
        e.Handled = true; // Space, Enter and Tab must not press a button here
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            Listening = null;
            Refresh();
            return;
        }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk is > 0 and <= 0xFF)
            Bind(control, (ushort)vk);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Listening is not { } control)
            return;
        e.Handled = true;
        Bind(control, e.ChangedButton switch
        {
            MouseButton.Left => VirtualKeys.LButton,
            MouseButton.Right => VirtualKeys.RButton,
            MouseButton.Middle => VirtualKeys.MButton,
            MouseButton.XButton1 => VirtualKeys.XButton1,
            _ => VirtualKeys.XButton2,
        });
    }

    internal void Bind(PadControl control, ushort key)
    {
        var result = _settings.Bind(control, key);
        if (!result.Bound)
        {
            ShowMessage(BindMessage.Reserved(key), BannerKind.Warning);
            return; // keep listening
        }
        Listening = null;
        Moved = result.MovedFrom;
        if (result.MovedFrom is { } from)
        {
            int left = _settings.Layout.KeysFor(from).Count;
            ShowMessage(BindMessage.Moved(key, from, left)!, BannerKind.Warning, seconds: 6);
            _movedTimer.Stop();
            _movedTimer.Start();
        }
        Refresh();
        if (result.MovedFrom is { } moved)
            _rows[moved].BringIntoView();
    }

    private void ShowMessage(string text, BannerKind kind, double seconds = 3)
    {
        MessageBanner.Kind = kind;
        MessageBanner.Text = text;
        MessageBanner.Visibility = Visibility.Visible;
        _messageTimer.Stop();
        _messageTimer.Interval = TimeSpan.FromSeconds(seconds);
        _messageTimer.Start();
    }

    /// <summary>Errors stay long enough to read a file problem such as "Square": unknown key "Spcae".</summary>
    private void ShowError(string text) => ShowMessage(text, BannerKind.Error, 10);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS.

- [ ] **Step 6: Look at it**

Run: `dotnet run --project src/CouchLink.App -- --windowed-player`, click **Controls** in the header.
Expected: profile bar, Find box with placeholder, Labels toggle, grouped cards with key caps, the mouse card and Done. Click Square, press Space: Square shows Space, Cross turns amber with a dashed "No key", the banner says "Space moved here from Cross. Cross has no key now." Type "shoot" in Find with no labels: every group hides. Esc clears Find. Close the app.

- [ ] **Step 7: Commit**

```bash
git add -A src/CouchLink.App/ControlsWindow.cs src/CouchLink.App/ControlsWindow.xaml src/CouchLink.App/ControlsWindow.xaml.cs tests/CouchLink.App.Tests/ControlsEditorTests.cs
git commit -m "feat(app): controls editor with grouped cards, find and moved-key highlight"
```

---

### Task 12: Approval toast, Save profile and crash dialog

**Files:**
- Delete: `src/CouchLink.App/ApprovalPopup.cs`, `src/CouchLink.App/SaveProfileDialog.cs`, `src/CouchLink.App/Diagnostics/CrashDialog.cs`
- Create: `src/CouchLink.App/ApprovalPopup.xaml(.cs)`, `src/CouchLink.App/SaveProfileDialog.xaml(.cs)`, `src/CouchLink.App/Diagnostics/CrashDialog.xaml(.cs)`
- Test: `tests/CouchLink.App.Tests/PopupAndDialogTests.cs`

**Interfaces:**
- Consumes: `AskCountdown.For`, `HostSession.AskTimeout`, `ThemeManager.TryInstallInto`, `WindowTheme.Apply`, `Motion.Enter`, `ProfileFile.MaxNameLength/MaxGameLength`, `ProjectLinks.NewIssue`.
- Produces: unchanged public surface — `ApprovalPopup(string name, int stackIndex, Action allow, Action deny)`, `ApprovalPopup.CloseByHost()`; `SaveProfileDialog(string? name, string? game)`, `ProfileName`, `Game`; `CrashDialog.ShowAndWait(string heading, string? reportPath, string? saveError)`. New internal: `ApprovalPopup.UpdateRemaining(TimeSpan elapsed)`, `CrashDialog(string heading, string? reportPath, string? saveError)` ctor.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/PopupAndDialogTests.cs`:

```csharp
using System.Windows;
using CouchLink.App.Diagnostics;
using CouchLink.App.Theme;

namespace CouchLink.App.Tests;

public class PopupAndDialogTests
{
    [Fact]
    public void The_toast_names_the_pc_and_counts_down()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var toast = new ApprovalPopup("PC-11", 0, () => { }, () => { });
            Assert.Equal("PC-11 wants to join", toast.Heading.Text);
            Assert.False(toast.ShowActivated);
            Assert.True(toast.Topmost);
            toast.UpdateRemaining(TimeSpan.FromSeconds(8));
            Assert.Equal("Denied automatically in 22 s", toast.Remaining.Text);
            toast.CloseByHost();
        });
    }

    [Fact]
    public void Save_is_disabled_until_the_profile_has_a_name()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var dialog = new SaveProfileDialog(null, null);
            Assert.False(dialog.SaveButton.IsEnabled);
            dialog.NameBox.Text = "  NBA 2K22 (my keys) ";
            Assert.True(dialog.SaveButton.IsEnabled);
            Assert.Equal("NBA 2K22 (my keys)", dialog.ProfileName);
            Assert.Null(dialog.Game);
            dialog.Close();
        });
    }

    [Fact]
    public void The_crash_dialog_shows_the_path_or_the_save_error()
    {
        Wpf.Run(() =>
        {
            var saved = new CrashDialog("CouchLink crashed.", @"C:\reports\crash.txt", null);
            Assert.Equal(@"C:\reports\crash.txt", saved.PathBox.Text);
            Assert.Equal(Visibility.Visible, saved.SavedPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, saved.SaveErrorText.Visibility);
            Assert.True(saved.OpenFolderButton.IsEnabled);
            saved.Close();

            var failed = new CrashDialog("CouchLink crashed.", null, "disk full");
            Assert.Equal(Visibility.Collapsed, failed.SavedPanel.Visibility);
            Assert.Equal("The crash report could not be saved: disk full", failed.SaveErrorText.Text);
            Assert.False(failed.OpenFolderButton.IsEnabled);
            Assert.False(failed.CopyPathButton.IsEnabled);
            failed.Close();
        });
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter PopupAndDialogTests`
Expected: build FAILS: `'ApprovalPopup' does not contain a definition for 'Heading'`.

- [ ] **Step 3: Write the approval toast**

Delete `src/CouchLink.App/ApprovalPopup.cs`. Create `src/CouchLink.App/ApprovalPopup.xaml`:

```xml
<Window x:Class="CouchLink.App.ApprovalPopup" x:ClassModifier="internal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:CouchLink.App.Ui"
        Title="CouchLink" WindowStyle="None" AllowsTransparency="True" Background="Transparent" ResizeMode="NoResize"
        SizeToContent="WidthAndHeight" Topmost="True" ShowActivated="False" ShowInTaskbar="False"
        AutomationProperties.AutomationId="ApprovalToast">
    <Border x:Name="Card" Margin="12" Width="340" Style="{StaticResource Card}" Padding="16">
        <Border.Effect>
            <DropShadowEffect BlurRadius="18" ShadowDepth="4" Opacity="0.45" Color="Black"/>
        </Border.Effect>
        <StackPanel>
            <DockPanel>
                <Button x:Name="CloseButton" DockPanel.Dock="Right" Style="{StaticResource GhostButton}" Padding="4,0" MinHeight="24"
                        VerticalAlignment="Top" AutomationProperties.Name="Deny and close" AutomationProperties.AutomationId="ToastClose">
                    <ui:Glyph Data="{StaticResource IconClose}" Brush="{DynamicResource TextMutedBrush}" Width="14" Height="14"/>
                </Button>
                <Border DockPanel.Dock="Left" Width="36" Height="36" CornerRadius="9" Background="{DynamicResource RaisedBrush}" Margin="0,0,12,0">
                    <ui:Glyph Data="{StaticResource IconUser}" Brush="{DynamicResource PrimaryTextBrush}"/>
                </Border>
                <StackPanel VerticalAlignment="Center">
                    <TextBlock x:Name="Heading" FontSize="15" FontWeight="SemiBold" TextWrapping="Wrap"/>
                    <TextBlock x:Name="Remaining" Style="{StaticResource CaptionText}" Margin="0,2,0,0"/>
                </StackPanel>
            </DockPanel>
            <Border x:Name="CountdownTrack" Height="3" CornerRadius="2" Background="{DynamicResource RaisedBrush}" Margin="0,12">
                <Border x:Name="CountdownBar" HorizontalAlignment="Left" CornerRadius="2" Background="{DynamicResource PrimaryTextBrush}"/>
            </Border>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button x:Name="DenyButton" Content="Deny" MinWidth="88" AutomationProperties.AutomationId="Deny"/>
                <Button x:Name="AllowButton" Content="Allow" MinWidth="88" Margin="8,0,0,0" Style="{StaticResource PrimaryButton}"
                        AutomationProperties.AutomationId="Allow"/>
            </StackPanel>
        </StackPanel>
    </Border>
</Window>
```

`src/CouchLink.App/ApprovalPopup.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CouchLink.App.Presentation;
using CouchLink.App.Theme;
using CouchLink.Core.Session;

namespace CouchLink.App;

/// <summary>
/// "PC-07 wants to join. Allow / Deny", a toast in the bottom-right corner over the game, without
/// taking the keyboard from it; the main window's taskbar button flashes. Several stack upwards. The
/// close button denies. The session denies by itself after 30 s and closes it with <see cref="CloseByHost"/>.
/// </summary>
internal sealed partial class ApprovalPopup : Window
{
    private const uint FLASHW_ALL = 3, FLASHW_TIMERNOFG = 12;

    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Stopwatch _shown = Stopwatch.StartNew();
    private bool _answered;

    public ApprovalPopup(string name, int stackIndex, Action allow, Action deny)
    {
        InitializeComponent();
        SetResourceReference(FontFamilyProperty, "BodyFont");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontSize = 14;
        Heading.Text = $"{name} wants to join";
        AllowButton.Click += (_, _) => Answer(allow);
        DenyButton.Click += (_, _) => Answer(deny);
        CloseButton.Click += (_, _) => Answer(deny);

        UpdateRemaining(TimeSpan.Zero);
        _countdown.Tick += (_, _) => UpdateRemaining(_shown.Elapsed);
        _countdown.Start();
        Loaded += (_, _) =>
        {
            PlaceAt(stackIndex);
            Motion.Enter(Card);
        };
        SourceInitialized += (_, _) => Flash();
        Closed += (_, _) =>
        {
            _countdown.Stop();
            if (!_answered)
                deny();
        };
    }

    /// <summary>The session already answered (timed out, or the client gave up): close without denying again.</summary>
    public void CloseByHost()
    {
        _answered = true;
        Close();
    }

    internal void UpdateRemaining(TimeSpan elapsed)
    {
        var countdown = AskCountdown.For(elapsed, HostSession.AskTimeout);
        Remaining.Text = countdown.Text;
        CountdownBar.Width = Math.Max(0, CountdownTrack.ActualWidth * countdown.Remaining);
    }

    private void Answer(Action answer)
    {
        _answered = true;
        answer();
        Close();
    }

    private void PlaceAt(int stackIndex)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 4;
        Top = Math.Max(area.Top, area.Bottom - ActualHeight * (stackIndex + 1));
    }

    /// <summary>The toast has no taskbar button of its own, so the main window's flashes.</summary>
    private static void Flash()
    {
        if (Application.Current?.MainWindow is not { } main)
            return;
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Hwnd = new WindowInteropHelper(main).Handle,
            Flags = FLASHW_ALL | FLASHW_TIMERNOFG,
        };
        if (info.Hwnd != 0)
            FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);
}
```

(The window's 12 px margin holds the shadow, so stacked toasts sit 24 px apart, as close as the old 8 px gap plus title bars.)

- [ ] **Step 4: Write the Save profile dialog**

Delete `src/CouchLink.App/SaveProfileDialog.cs`. Create `src/CouchLink.App/SaveProfileDialog.xaml`:

```xml
<Window x:Class="CouchLink.App.SaveProfileDialog" x:ClassModifier="internal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Save profile" Width="380" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False">
    <StackPanel Margin="20">
        <TextBlock Text="Name" FontWeight="SemiBold"/>
        <TextBox x:Name="NameBox" Margin="0,6,0,0" AutomationProperties.Name="Name" AutomationProperties.AutomationId="ProfileName"/>
        <TextBlock Text="Shown in the profile list" Style="{StaticResource CaptionText}" Margin="0,4,0,0"/>
        <TextBlock Margin="0,14,0,0">
            <Run Text="Game" FontWeight="SemiBold"/><Run Text=" · optional" Foreground="{DynamicResource TextMutedBrush}"/>
        </TextBlock>
        <TextBox x:Name="GameBox" Margin="0,6,0,0" AutomationProperties.Name="Game" AutomationProperties.AutomationId="ProfileGame"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
            <Button x:Name="CancelButton" Content="Cancel" IsCancel="True" MinWidth="88" AutomationProperties.AutomationId="Cancel"/>
            <Button x:Name="SaveButton" Content="Save" IsDefault="True" MinWidth="88" Margin="8,0,0,0" Style="{StaticResource PrimaryButton}"
                    AutomationProperties.AutomationId="Save"/>
        </StackPanel>
    </StackPanel>
</Window>
```

`src/CouchLink.App/SaveProfileDialog.xaml.cs`:

```csharp
using System.Windows;
using CouchLink.App.Theme;
using CouchLink.Core.Input;

namespace CouchLink.App;

/// <summary>Save as…: the profile's name (shown in every PC's list) and, optionally, the game it is for.</summary>
internal sealed partial class SaveProfileDialog : Window
{
    public SaveProfileDialog(string? name, string? game)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        NameBox.MaxLength = ProfileFile.MaxNameLength;
        GameBox.MaxLength = ProfileFile.MaxGameLength;
        NameBox.Text = name ?? "";
        GameBox.Text = game ?? "";
        SaveButton.IsEnabled = ProfileName.Length > 0;
        NameBox.TextChanged += (_, _) => SaveButton.IsEnabled = ProfileName.Length > 0;
        SaveButton.Click += (_, _) => DialogResult = true;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ProfileName => NameBox.Text.Trim();

    public string? Game => string.IsNullOrWhiteSpace(GameBox.Text) ? null : GameBox.Text.Trim();
}
```

- [ ] **Step 5: Write the crash dialog**

Delete `src/CouchLink.App/Diagnostics/CrashDialog.cs`. Create `src/CouchLink.App/Diagnostics/CrashDialog.xaml` (only `DynamicResource`, see Global Constraints):

```xml
<Window x:Class="CouchLink.App.Diagnostics.CrashDialog" x:ClassModifier="internal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:CouchLink.App.Ui"
        Title="CouchLink crashed" Width="560" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen" Topmost="True">
    <StackPanel Margin="20">
        <DockPanel>
            <ui:Glyph DockPanel.Dock="Left" Data="{DynamicResource IconAlert}" Brush="{DynamicResource DangerBrush}"
                      Width="22" Height="22" Margin="0,0,10,0" VerticalAlignment="Top"/>
            <TextBlock x:Name="HeadingText" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap"/>
        </DockPanel>
        <StackPanel x:Name="SavedPanel">
            <TextBlock Text="A report was saved to:" Margin="0,12,0,6"/>
            <TextBox x:Name="PathBox" IsReadOnly="True" TextWrapping="Wrap" FontFamily="{DynamicResource MonoFont}"
                     AutomationProperties.AutomationId="ReportPath"/>
            <TextBlock Text="Please attach this file to a new issue so we can fix it." Margin="0,10,0,0" TextWrapping="Wrap"
                       Foreground="{DynamicResource TextMutedBrush}"/>
        </StackPanel>
        <TextBlock x:Name="SaveErrorText" Margin="0,12,0,0" TextWrapping="Wrap" Foreground="{DynamicResource DangerBrush}"
                   Visibility="Collapsed" AutomationProperties.AutomationId="SaveError"/>
        <WrapPanel HorizontalAlignment="Right" Margin="0,20,0,0">
            <Button x:Name="OpenFolderButton" Content="Open folder" MinWidth="110" Margin="8,0,0,0" AutomationProperties.AutomationId="OpenFolderButton"/>
            <Button x:Name="CopyPathButton" Content="Copy path" MinWidth="110" Margin="8,0,0,0" AutomationProperties.AutomationId="CopyPathButton"/>
            <Button x:Name="ReportButton" Content="Report on GitHub" MinWidth="110" Margin="8,0,0,0" Style="{DynamicResource PrimaryButton}"
                    AutomationProperties.AutomationId="ReportButton"/>
            <Button x:Name="CloseButton" Content="Close" MinWidth="110" Margin="8,0,0,0" AutomationProperties.AutomationId="CloseButton"/>
        </WrapPanel>
    </StackPanel>
</Window>
```

`src/CouchLink.App/Diagnostics/CrashDialog.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CouchLink.App.Theme;
using CouchLink.Core.Diagnostics;

namespace CouchLink.App.Diagnostics;

/// <summary>
/// Tells the user where the crash report is and how to send it. Runs on its own thread with its own
/// copy of the theme; if the theme can't load it still opens, in the stock look.
/// </summary>
internal sealed partial class CrashDialog : Window
{
    /// <summary>Internal for tests; use <see cref="ShowAndWait"/>.</summary>
    internal CrashDialog(string heading, string? reportPath, string? saveError)
    {
        ThemeManager.TryInstallInto(this);
        InitializeComponent();
        WindowTheme.Apply(this);
        HeadingText.Text = heading;
        if (reportPath is not null)
        {
            PathBox.Text = reportPath;
        }
        else
        {
            SavedPanel.Visibility = Visibility.Collapsed;
            SaveErrorText.Text = $"The crash report could not be saved: {saveError}";
            SaveErrorText.Visibility = Visibility.Visible;
        }
        OpenFolderButton.IsEnabled = CopyPathButton.IsEnabled = reportPath is not null;
        OnClick(OpenFolderButton, () => Process.Start("explorer.exe", $"/select,\"{reportPath}\""));
        OnClick(CopyPathButton, () => Clipboard.SetText(reportPath!));
        OnClick(ReportButton, () => Process.Start(new ProcessStartInfo(ProjectLinks.NewIssue) { UseShellExecute = true }));
        OnClick(CloseButton, Close);
    }

    /// <summary>Shows the dialog modally on its own STA thread and waits. Never throws.</summary>
    public static void ShowAndWait(string heading, string? reportPath, string? saveError)
    {
        var thread = new Thread(() =>
        {
            try
            {
                new CrashDialog(heading, reportPath, saveError).ShowDialog();
            }
            catch
            {
                // Nothing left to tell the user with; the report file is already written.
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static void OnClick(Button button, Action action) => button.Click += (_, _) =>
    {
        try
        {
            action();
        }
        catch
        {
            // e.g. clipboard busy or no browser; the path is still on screen.
        }
    };
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.App.Tests`
Expected: PASS.

- [ ] **Step 7: Check that no MessageBox is left**

Run: `git grep -n "MessageBox" -- src/CouchLink.App`
Expected: no output.

- [ ] **Step 8: Check the crash dialog by hand**

Run: `dotnet run --project src/CouchLink.App -- --crash-test=ui`
Expected: the themed crash dialog (dark, alert icon, path in mono, purple Report on GitHub). Close it.

- [ ] **Step 9: Commit**

```bash
git add -A src/CouchLink.App/ApprovalPopup.cs src/CouchLink.App/ApprovalPopup.xaml src/CouchLink.App/ApprovalPopup.xaml.cs src/CouchLink.App/SaveProfileDialog.cs src/CouchLink.App/SaveProfileDialog.xaml src/CouchLink.App/SaveProfileDialog.xaml.cs src/CouchLink.App/Diagnostics tests/CouchLink.App.Tests/PopupAndDialogTests.cs
git commit -m "feat(app): approval toast with countdown, themed save and crash dialogs"
```

---

### Task 13: Screenshots, docs and manual checks

**Files:**
- Create: `eng/screenshots.ps1`
- Replace: `docs/images/start.png`, `host-lobby.png`, `host-lobby-quality.png`, `host-lobby-details.png`, `join-list.png`, `controls.png`, `controls-profile.png`
- Create: `docs/images/session-waiting.png`, `approval.png`, `stop-confirm.png`
- Modify: `README.md`, `docs/cafe-setup-guide.md`, `docs/gate-results.md`

**Interfaces:**
- Consumes: the automation IDs from Tasks 6-12 (`HostButton`, `JoinButton`, `Back`, `HeaderControls`, `Profile`, `ShowLabels`, `Done`, `StreamStats`, `AddressBox`, `AddressJoin`, `StopHosting`, `DialogOk`, window `ApprovalToast`).

- [ ] **Step 1: Write the screenshot script**

`eng/screenshots.ps1`:

```powershell
<#
.SYNOPSIS
  Captures the README and guide screenshots from the built app with UI Automation.
.DESCRIPTION
  Starts a host copy (--test-pattern --windowed-player skips the single-instance check) and a client
  copy (--windowed-player) on this PC. The host needs ViGEmBus. Saves PNGs into docs/images.
#>
param(
    [string]$Exe = "src/CouchLink.App/bin/Debug/net10.0-windows/CouchLink.App.exe",
    [string]$Out = "docs/images"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Native {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
}
"@
$AE = [System.Windows.Automation.AutomationElement]
$Root = $AE::RootElement
$IdProp = $AE::AutomationIdProperty
$NameProp = $AE::NameProperty
$PidProp = $AE::ProcessIdProperty
$Scope = [System.Windows.Automation.TreeScope]

function Prop($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

function Wait-Element($parent, $condition, [string]$what, [int]$ms = 15000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) {
        $found = $parent.FindFirst($Scope::Descendants, $condition)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for $what"
}

function Wait-Window([int]$processId, [string]$name) {
    $condition = New-Object System.Windows.Automation.AndCondition((Prop $PidProp $processId), (Prop $NameProp $name))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 15000) {
        $w = $Root.FindFirst($Scope::Children, $condition)
        if ($w) { return $w }
        Start-Sleep -Milliseconds 200
    }
    throw "Window '$name' of process $processId did not appear"
}

function Id($window, [string]$id) { Wait-Element $window (Prop $IdProp $id) $id }

function Invoke-Id($window, [string]$id) {
    (Id $window $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 600
}

function Save-Shot($window, [string]$name) {
    $hwnd = [IntPtr]$window.Current.NativeWindowHandle
    [Native]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 500
    $r = New-Object Native+RECT
    [Native]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16) | Out-Null # DWMWA_EXTENDED_FRAME_BOUNDS
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save((Join-Path (Resolve-Path $Out) "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "saved $name.png"
}

$host1 = Start-Process $Exe -ArgumentList '--test-pattern', '--windowed-player' -PassThru
$client = $null
try {
    $main = Wait-Window $host1.Id 'CouchLink'
    Start-Sleep -Seconds 1
    Save-Shot $main 'start'

    Invoke-Id $main 'HeaderControls'
    $controls = Wait-Window $host1.Id 'Controls'
    Save-Shot $controls 'controls'
    $profile = Id $controls 'Profile'
    $profile.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 400
    $nba = Wait-Element $Root (Prop $NameProp 'NBA 2K22') 'NBA 2K22 profile'
    $nba.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    (Id $controls 'ShowLabels').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 600
    Save-Shot $controls 'controls-profile'
    Invoke-Id $controls 'Reset'
    (Id $controls 'ShowLabels').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Invoke-Id $controls 'Done'

    Invoke-Id $main 'JoinButton'
    Start-Sleep -Seconds 2
    Save-Shot $main 'join-list'
    Invoke-Id $main 'Back'

    Invoke-Id $main 'HostButton'
    Start-Sleep -Seconds 2
    Save-Shot $main 'host-lobby'
    Save-Shot $main 'host-lobby-quality'

    $client = Start-Process $Exe -ArgumentList '--windowed-player' -PassThru
    $clientMain = Wait-Window $client.Id 'CouchLink'
    Invoke-Id $clientMain 'JoinButton'
    (Id $clientMain 'AddressBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('127.0.0.1')
    Invoke-Id $clientMain 'AddressJoin'
    $toast = Wait-Element $Root (Prop $IdProp 'ApprovalToast') 'approval toast'
    Start-Sleep -Seconds 1
    Save-Shot $clientMain 'session-waiting'
    Save-Shot $toast 'approval'
    Invoke-Id $toast 'Allow'
    Start-Sleep -Seconds 3

    $stats = Id $main 'StreamStats'
    $stats.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Seconds 1
    Save-Shot $main 'host-lobby-details'

    $stop = Id $main 'StopHosting'
    $stop.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() # returns once the dialog shows
    $confirm = Wait-Element $Root (Prop $IdProp 'DialogOk') 'stop confirm'
    $dialog = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($confirm)
    while ($dialog.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) {
        $dialog = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($dialog)
    }
    Save-Shot $dialog 'stop-confirm'
    $confirm.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
finally {
    if ($client -and -not $client.HasExited) { $client.Kill() }
    if (-not $host1.HasExited) { $host1.Kill() }
}
```

- [ ] **Step 2: Build and capture**

Run:

```
dotnet build src/CouchLink.App
pwsh -File eng/screenshots.ps1
```

Expected: ten `saved …png` lines and no error. Open each PNG and check it shows the right screen fully drawn (no half-faded screen; the script waits 500 ms before each capture, so raise the waits if one is caught mid-fade). If the host can't start (no ViGEmBus), the script stops at `host-lobby`; run it on a PC with ViGEmBus.

- [ ] **Step 3: Update the README**

In `README.md`:
- Replace the three image lines with:

```html
  <img src="docs/images/start.png" width="260" alt="CouchLink's Start screen with Host a game and Join a game">
  <img src="docs/images/host-lobby.png" width="260" alt="The host lobby with the stream card and the player list">
  <img src="docs/images/controls.png" width="220" alt="The controls editor with grouped controls and their keys">
```

- Line 144: replace "**Crash reports** on the Start screen" with "**Help → Crash reports** in the header".

- [ ] **Step 4: Update the café setup guide**

In `docs/cafe-setup-guide.md`, section 5 and later:
- "click **Host**." → "click **Host a game**."
- "click **Join**." → "click **Join a game**."
- "Hosts on your network appear in the list as \"PC name · players/10 players\". Click the host." → "Hosts on your network appear as cards with the PC name and \"3 / 10 players\". Click the host. A host running a different version of CouchLink is greyed out."
- "If the host doesn't appear, click **Join by address...** and type the host's IP address" → "If the host doesn't appear, type the host's IP address in **Join by address** at the bottom of the list and click **Join**"; add after it: "After 10 seconds without a host, the list shows what to check."
- Approving players: replace "a small popup appears in the bottom-right corner of the host's screen with **Allow** and **Deny**." with "a card appears in the bottom-right corner of the host's screen with **Allow** and **Deny** and a bar counting down."; add `![The approval popup](images/approval.png)` after the paragraph.
- "Tick **Allow everyone (no popup when someone joins)**" → "Turn on **Let everyone in**".
- Managing players: "**Stop hosting** ends the session for everyone (\"Host ended the session.\")." → "**Stop hosting** ends the session for everyone (\"Host ended the session.\"). With players connected it asks first; Enter keeps hosting." and add `![Stop hosting asks first](images/stop-confirm.png)`. "**Details** shows the stream" → "**Stream stats** shows the stream".
- Section 6 (On a joining PC), after its first paragraph, add: `![The session screen while the host decides](images/session-waiting.png)`.
- Changing keys: "Open the editor with **⚙ Controls** on the Start screen or the session screen" → "Open the editor with **Controls** in the header or on the session screen"; "and the editor says where it came from (for example \"Space moved from Cross.\"); that control is then left without a key until you give it one." → "the control that lost it lights up, and the editor says where it came from (for example \"Space moved here from Cross. Cross has no key now.\"). **Find** at the top filters the list by control or label."
- "**Mouse sensitivity (right stick)**" → "**Sensitivity** under **Mouse (right stick)**".
- Profiles: alt text "…and Show labels ticked" → "…and Labels turned on"; "tick **Show labels**" → "turn on **Labels**".
- Section 8: "the lobby shows an orange warning under the Stream row" → "the lobby shows an amber warning under the Stream card"; every "**Details**" → "**Stream stats**".
- Troubleshooting row: "Use **Join by address...** with the host's IP." → "Use **Join by address** with the host's IP."; "or tick **Allow everyone**" → "or turn on **Let everyone in**".
- Crash reports: "Click **Crash reports** on the Start screen" → "Click **Help → Crash reports** in the header".

Then check nothing old is left:

Run: `git grep -n -e "Allow everyone" -e "Join by address\.\.\." -e "⚙" -e "Show labels" -e "on the Start screen" -- README.md docs/cafe-setup-guide.md`
Expected: no output.

- [ ] **Step 5: Add the manual checks to gate-results**

Append to `docs/gate-results.md`:

```markdown
## UI/UX revamp (Plan 12)

Date: <fill in>
PCs / Windows builds: <fill in>

| Check | Result |
|---|---|
| Windows 10 and 11: every window has the dark title bar; Windows 11 shows the navy caption color | <pass/fail> |
| Windows High Contrast on while CouchLink runs: every screen switches to system colors at once and stays readable; off again: back to the dark theme | <pass/fail> |
| "Show animations in Windows" off: screen changes, toasts and button hovers are instant; the spinners stand still | <pass/fail> |
| Keyboard only: Tab reaches Host a game, Join a game, Controls, Help; the purple focus ring shows; Enter presses; the Help menu opens and works with arrows | <pass/fail> |
| Keyboard only in the Controls editor: Find, Labels, each row, the slider (arrows), Invert Y, Reset, Done | <pass/fail> |
| While playing, Space and Enter in the game never press Leave, Controls or a header button on the client's main window | <pass/fail> |
| Ctrl+Alt+C over the game opens the new editor on top; Done returns to the game | <pass/fail> |
| Two players join at once: two toasts stack in the bottom-right, both count down, the host's taskbar button flashes, the game keeps the keyboard | <pass/fail> |
| Stop hosting with two players in: the confirm names both; Enter keeps hosting; Stop hosting ends both sessions | <pass/fail> |
| Join by address with "192.168": the error shows under the box and nothing connects | <pass/fail> |
| `--crash-test=ui` and `--crash-test=startup`: the themed crash dialog opens with its four buttons working | <pass/fail> |
| 125% and 150% display scaling: nothing clipped on Start, the lobby with 9 players, the join list, the editor with Labels on | <pass/fail> |
```

- [ ] **Step 6: Run everything**

Run: `dotnet build -c Release; dotnet test -c Release --no-build`
Expected: `Build succeeded. 0 Warning(s)`, and every test project passes.

- [ ] **Step 7: Commit**

```bash
git add eng/screenshots.ps1 docs/images README.md docs/cafe-setup-guide.md docs/gate-results.md
git commit -m "docs: new screenshots and guide text for the redesigned app"
```
