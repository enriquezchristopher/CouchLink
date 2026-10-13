using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using CouchLink.Core.Input;
using CouchLink.Core.Net;
using CouchLink.Pads;
using HidSharp;

namespace CouchLink.PadTest;

/// <summary>
/// Mouse-to-right-stick check: moves a simulated mouse at steady speeds, runs it through the client's
/// input loop (1 ms ticks, send on change or every 8 ms) into a virtual DS4, and reads the right stick
/// back through Windows HID like a game would. Runs the old and the new stick so the two can be compared.
/// </summary>
internal static class MouseMode
{
    private const int SonyVendorId = 0x054C;
    private const int Ds4ProductId = 0x05C4;
    private static readonly TimeSpan MoveFor = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan StillFor = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan SettleFor = TimeSpan.FromMilliseconds(300); // skipped when measuring steadiness

    private sealed record Scenario(string Stick, int Hz, int CountsPerSecond);

    private sealed record Lateness(double MaxMs, int Over4Ms, double LongestTickMs);

    private sealed record Sample(double Ms, byte RX);

    private interface IStick
    {
        void Move(int dx, int dy);
        PadState Tick(double dtSeconds);
    }

    private sealed class NewStick : IStick
    {
        private readonly InputMapper _mapper = new(KeyLayout.CreateDefault(), new MouseStick());
        public void Move(int dx, int dy) => _mapper.MouseMove(dx, dy);
        public PadState Tick(double dtSeconds) => _mapper.Tick(dtSeconds);
    }

    private sealed class OldStick : IStick
    {
        private readonly LegacyMouseStick _stick = new();
        private readonly Lock _gate = new();
        public void Move(int dx, int dy) { lock (_gate) _stick.AddDelta(dx, dy); }

        public PadState Tick(double dtSeconds)
        {
            lock (_gate)
            {
                var (rx, ry) = _stick.Update(dtSeconds);
                return PadState.Neutral with { RX = rx, RY = ry };
            }
        }
    }

    public static int Run(string csvPath)
    {
        if (!ViGEmPadFactory.TryCreate(out var factory, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        using (factory)
        {
            var before = DeviceList.Local.GetHidDevices(SonyVendorId, Ds4ProductId).Select(d => d.DevicePath).ToHashSet();
            using var pad = factory!.Create();
            var device = WaitForNewDevice(before);
            if (device is null || !device.TryOpen(out var stream))
            {
                Console.Error.WriteLine("FAIL: the virtual DS4 never showed up as a HID device.");
                return 1;
            }

            using (stream)
            {
                var clock = Stopwatch.StartNew();
                var samples = new List<Sample>();
                using var stop = new CancellationTokenSource();
                var reader = new Thread(() => ReadReports(stream, clock, samples, stop.Token)) { IsBackground = true, Name = "HID reader" };
                reader.Start();

                timeBeginPeriod(1);
                var results = new List<(Scenario Scenario, double StartMs, Lateness Late)>();
                try
                {
                    foreach (var stick in new[] { "old", "new" })
                        foreach (int hz in new[] { 125, 1000 })
                            foreach (int speed in new[] { 300, 1000, 3000 })
                            {
                                var scenario = new Scenario(stick, hz, speed);
                                Console.Write($"\r{stick} stick, {hz} Hz mouse, {speed} counts/s...      ");
                                double startMs = clock.Elapsed.TotalMilliseconds;
                                var late = RunScenario(stick == "old" ? new OldStick() : new NewStick(), pad, clock, hz, speed);
                                results.Add((scenario, startMs, late));
                            }
                }
                finally
                {
                    timeEndPeriod(1);
                    stop.Cancel();
                    reader.Join();
                }
                Console.WriteLine("\r" + new string(' ', 60));

                List<Sample> all;
                lock (samples)
                    all = [.. samples];
                Report(results, all, csvPath);
            }
        }
        return 0;
    }

    /// <summary>The mouse on its own thread (raw input arrives on the UI thread), the input loop on this one.</summary>
    private static Lateness RunScenario(IStick stick, CouchLink.Core.Pads.IVirtualPad pad, Stopwatch clock, int hz, int countsPerSecond)
    {
        var start = clock.Elapsed;
        var moveEnd = start + MoveFor;
        var end = moveEnd + StillFor;
        double maxLate = 0;
        int over4 = 0;
        var mouse = new Thread(() =>
        {
            var period = TimeSpan.FromSeconds(1.0 / hz);
            var next = clock.Elapsed;
            double owed = 0;
            while (next < moveEnd)
            {
                WaitUntil(clock, next);
                double late = (clock.Elapsed - next).TotalMilliseconds;
                maxLate = Math.Max(maxLate, late);
                if (late > 4)
                    over4++;
                owed += countsPerSecond / (double)hz;
                int counts = (int)owed;
                owed -= counts;
                if (counts != 0)
                    stick.Move(counts, 0);
                next += period;
            }
        }) { IsBackground = true, Name = "Mouse", Priority = ThreadPriority.Highest };
        mouse.Start();

        var policy = new SendPolicy();
        var lastTick = clock.Elapsed;
        double longestTick = 0;
        while (clock.Elapsed < end)
        {
            var now = clock.Elapsed;
            longestTick = Math.Max(longestTick, (now - lastTick).TotalMilliseconds);
            var state = stick.Tick((now - lastTick).TotalSeconds);
            lastTick = now;
            if (policy.ShouldSend(state, now))
                pad.Apply(state);
            Thread.Sleep(1);
        }
        mouse.Join();
        pad.Apply(PadState.Neutral);
        return new Lateness(maxLate, over4, longestTick);
    }

    private static void WaitUntil(Stopwatch clock, TimeSpan when)
    {
        while (clock.Elapsed < when)
        {
            if (when - clock.Elapsed > TimeSpan.FromMilliseconds(2))
                Thread.Sleep(1);
            else
                Thread.SpinWait(20);
        }
    }

    private static void ReadReports(HidStream stream, Stopwatch clock, List<Sample> samples, CancellationToken stop)
    {
        stream.ReadTimeout = 200;
        var buffer = new byte[stream.Device.GetMaxInputReportLength()];
        while (!stop.IsCancellationRequested)
        {
            int n;
            try
            {
                n = stream.Read(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                continue;
            }
            if (Ds4Report.TryDecode(buffer.AsSpan(0, n), out var state))
                lock (samples)
                    samples.Add(new Sample(clock.Elapsed.TotalMilliseconds, state.RX));
        }
    }

    private static void Report(List<(Scenario Scenario, double StartMs, Lateness Late)> results, List<Sample> all, string csvPath)
    {
        using var csv = new StreamWriter(csvPath);
        csv.WriteLine("stick,hz,counts_per_s,ms,rx");

        Console.WriteLine("What a game reads on the right stick (X), from the virtual DS4 over HID.");
        Console.WriteLine("Tilt is 0-1 of full. 'Centred' counts reports at dead centre while the mouse was moving.");
        Console.WriteLine();
        Console.WriteLine($"  {"Stick",-6}{"Mouse",-9}{"Speed",-12}{"Avg tilt",-10}{"Min",-7}{"Max",-7}{"Swing",-8}{"Centred",-9}{"Back to centre",-15}{"Reports/s",-11}{"Mouse late (max, >4 ms)",-25}Longest tick");
        foreach (var (scenario, startMs, late) in results)
        {
            double moveEndMs = startMs + MoveFor.TotalMilliseconds;
            double endMs = moveEndMs + StillFor.TotalMilliseconds;
            var run = all.Where(s => s.Ms >= startMs && s.Ms < endMs).ToList();
            foreach (var s in run)
                csv.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{scenario.Stick},{scenario.Hz},{scenario.CountsPerSecond},{s.Ms - startMs:F2},{s.RX}"));

            var steady = run.Where(s => s.Ms >= startMs + SettleFor.TotalMilliseconds && s.Ms < moveEndMs)
                .Select(s => (s.RX - 128) / 127.0).ToList();
            if (steady.Count == 0)
            {
                Console.WriteLine($"  {scenario.Stick,-6}{scenario.Hz + " Hz",-9}no reports");
                continue;
            }
            double centred = steady.Count(t => t == 0) / (double)steady.Count;
            var lastOff = run.LastOrDefault(s => s.RX != 128);
            string back = lastOff is null ? "-" : $"{Math.Max(lastOff.Ms - moveEndMs, 0):F0} ms";
            double seconds = (endMs - startMs) / 1000;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {scenario.Stick,-6}{scenario.Hz + " Hz",-9}{scenario.CountsPerSecond + "/s",-12}{steady.Average(),-10:F2}{steady.Min(),-7:F2}{steady.Max(),-7:F2}{steady.Max() - steady.Min(),-8:F2}{centred,-9:P0}{back,-15}{run.Count / seconds,-11:F0}{$"{late.MaxMs:F1} ms, {late.Over4Ms}x",-25}{late.LongestTickMs:F0} ms"));
        }
        Console.WriteLine();
        Console.WriteLine($"Every report: {Path.GetFullPath(csvPath)}");
    }

    private static HidDevice? WaitForNewDevice(HashSet<string> before)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        do
        {
            Thread.Sleep(250);
            var found = DeviceList.Local.GetHidDevices(SonyVendorId, Ds4ProductId).FirstOrDefault(d => !before.Contains(d.DevicePath));
            if (found is not null)
                return found;
        } while (DateTime.UtcNow < deadline);
        return null;
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint milliseconds);
}
