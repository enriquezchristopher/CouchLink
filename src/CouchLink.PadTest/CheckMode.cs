using System.Diagnostics;
using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using CouchLink.Pads;
using HidSharp;

namespace CouchLink.PadTest;

/// <summary>
/// Game-free isolation check: creates virtual DS4 pads, reads them back through
/// Windows HID like a game would, and verifies each pad is a separate controller.
/// </summary>
internal static class CheckMode
{
    private const int SonyVendorId = 0x054C;
    private const int Ds4ProductId = 0x05C4;
    private static readonly PadState Pressed = PadState.Neutral with { Buttons = PadButtons.Cross, LX = 255 };

    public static int Run(int count)
    {
        if (!ViGEmPadFactory.TryCreate(out var factory, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        using (factory)
        {
            var before = Ds4Paths();
            var pads = Enumerable.Range(0, count).Select(_ => factory!.Create()).ToList();
            try
            {
                var devices = WaitForNewDevices(before, count);
                if (devices.Count != count)
                {
                    Console.WriteLine($"FAIL: created {count} pads but Windows shows {devices.Count} new DS4 devices.");
                    return 1;
                }
                Console.WriteLine($"Windows shows {count} new DS4 devices.");

                var streams = devices.Select(Open).ToList();
                try
                {
                    var readers = streams.Select(s => new LatestReader(s)).ToList();
                    bool simultaneous = SimultaneousTest(pads, readers);
                    bool oneAtATime = OneAtATimeTest(pads, readers, out var deviceOfPad);
                    bool everyAction = oneAtATime && EveryActionTest(pads, readers, deviceOfPad);
                    bool pass = simultaneous && oneAtATime && everyAction;
                    Console.WriteLine();
                    Console.WriteLine(pass
                        ? $"PASS: all {count} pads are separate controllers, every control works on every pad, and no input leaked between them."
                        : "FAIL: see the tables above.");
                    return pass ? 0 : 1;
                }
                finally
                {
                    foreach (var s in streams)
                        s.Dispose();
                }
            }
            finally
            {
                foreach (var pad in pads)
                    pad.Dispose();
            }
        }
    }

    private static bool SimultaneousTest(List<IVirtualPad> pads, List<LatestReader> readers)
    {
        Console.WriteLine();
        Console.WriteLine("Test 1: every pad holds a different state at the same time");
        var sent = pads.Select((_, i) => IsolationCheck.SignatureFor(i)).ToList();
        for (int i = 0; i < pads.Count; i++)
            pads[i].Apply(sent[i]);
        var read = ReadAll(readers);

        var matches = sent.Select(s => IsolationCheck.FindExactlyOne(s, read)).ToList();
        Console.WriteLine($"  {"Pad",-4} {"Sent",-28} {"Device",-7} Result");
        for (int i = 0; i < pads.Count; i++)
            Console.WriteLine($"  P{i + 2,-3} {$"{sent[i].Buttons}, LX={sent[i].LX}",-28}{(matches[i] is { } d ? $"#{d + 1}" : "-"),-7} {(matches[i] is null ? "FAIL" : "ok")}");

        bool pass = IsolationCheck.AllDistinct(matches);
        Console.WriteLine(pass ? "  Test 1 PASS" : "  Test 1 FAIL");
        return pass;
    }

    private static bool OneAtATimeTest(List<IVirtualPad> pads, List<LatestReader> readers, out List<int?> deviceOfPad)
    {
        Console.WriteLine();
        Console.WriteLine("Test 2: one pad presses Cross + stick right, all others neutral");
        var matches = new List<int?>();
        bool othersStayedNeutral = true;
        for (int i = 0; i < pads.Count; i++)
        {
            for (int j = 0; j < pads.Count; j++)
                pads[j].Apply(j == i ? Pressed : PadState.Neutral);
            var read = ReadAll(readers);

            int? device = IsolationCheck.FindExactlyOne(Pressed, read);
            int neutral = read.Count(r => r == PadState.Neutral);
            bool ok = device is not null && neutral == pads.Count - 1;
            othersStayedNeutral &= ok;
            matches.Add(device);
            Console.WriteLine($"  P{i + 2,-3} -> device {(device is { } d ? $"#{d + 1}" : "-"),-4} others neutral: {neutral}/{pads.Count - 1}  {(ok ? "ok" : "FAIL")}");
        }

        bool pass = othersStayedNeutral && IsolationCheck.AllDistinct(matches);
        Console.WriteLine(pass ? "  Test 2 PASS" : "  Test 2 FAIL");
        deviceOfPad = matches;
        return pass;
    }

    private static bool EveryActionTest(List<IVirtualPad> pads, List<LatestReader> readers, List<int?> deviceOfPad)
    {
        Console.WriteLine();
        Console.WriteLine($"Test 3: every control ({ControlActions.All.Count} actions) on every pad, others stay neutral");
        foreach (var pad in pads)
            pad.Apply(PadState.Neutral);

        bool pass = true;
        for (int i = 0; i < pads.Count; i++)
        {
            var reader = readers[deviceOfPad[i]!.Value];
            var failures = new List<string>();
            foreach (var action in ControlActions.All)
            {
                pads[i].Apply(action.State);
                var read = reader.ReadLatest();
                if (read != action.State)
                    failures.Add($"{action.Name} -> read {(read is { } r ? Describe(r) : "nothing")}");
            }
            pads[i].Apply(PadState.Neutral);
            if (reader.ReadLatest() != PadState.Neutral)
                failures.Add("did not return to neutral");

            int othersNeutral = readers
                .Where((_, d) => d != deviceOfPad[i])
                .Count(r => r.ReadLatest() == PadState.Neutral);
            if (othersNeutral != pads.Count - 1)
                failures.Add($"only {othersNeutral}/{pads.Count - 1} other pads stayed neutral");

            int ok = ControlActions.All.Count - failures.Count(f => f.Contains(" -> "));
            Console.WriteLine($"  P{i + 2,-3} {ok}/{ControlActions.All.Count} actions ok, others neutral {othersNeutral}/{pads.Count - 1}  {(failures.Count == 0 ? "ok" : "FAIL")}");
            foreach (var f in failures)
                Console.WriteLine($"         {f}");
            pass &= failures.Count == 0;
        }

        Console.WriteLine(pass ? "  Test 3 PASS" : "  Test 3 FAIL");
        return pass;
    }

    private static List<PadState?> ReadAll(List<LatestReader> readers) =>
        readers.Select(r => r.ReadLatest()).ToList();

    private static string Describe(PadState s) =>
        $"{s.Buttons}, LX={s.LX} LY={s.LY} RX={s.RX} RY={s.RY} L2={s.L2} R2={s.R2}";

    private static HashSet<string> Ds4Paths() =>
        DeviceList.Local.GetHidDevices(SonyVendorId, Ds4ProductId).Select(d => d.DevicePath).ToHashSet();

    private static List<HidDevice> WaitForNewDevices(HashSet<string> before, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        List<HidDevice> found;
        do
        {
            Thread.Sleep(250);
            found = DeviceList.Local.GetHidDevices(SonyVendorId, Ds4ProductId)
                .Where(d => !before.Contains(d.DevicePath))
                .ToList();
        } while (found.Count < count && DateTime.UtcNow < deadline);
        return found;
    }

    private static HidStream Open(HidDevice device)
    {
        if (!device.TryOpen(out var stream))
            throw new InvalidOperationException($"Could not open {device.DevicePath}");
        return stream;
    }

    /// <summary>
    /// Returns the device's current state. Virtual DS4 pads send a report about
    /// every 15 ms nonstop, and Windows queues older ones. Reads that return
    /// instantly come from that queue; reads that had to wait are live. After a
    /// few live reads in a row, the newest one reflects the last Apply.
    /// </summary>
    private sealed class LatestReader(HidStream stream)
    {
        private const int LiveReadsNeeded = 3;
        private static readonly TimeSpan LiveThreshold = TimeSpan.FromMilliseconds(4);
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

        private readonly byte[] _buffer = new byte[stream.Device.GetMaxInputReportLength()];

        public PadState? ReadLatest()
        {
            stream.ReadTimeout = 500;
            PadState? latest = null;
            int live = 0;
            var clock = Stopwatch.StartNew();
            while (live < LiveReadsNeeded && clock.Elapsed < Budget)
            {
                var started = clock.Elapsed;
                int n;
                try
                {
                    n = stream.Read(_buffer, 0, _buffer.Length);
                }
                catch (TimeoutException)
                {
                    return latest; // device went quiet; keep what we have
                }
                live = clock.Elapsed - started >= LiveThreshold ? live + 1 : 0;
                if (Ds4Report.TryDecode(_buffer.AsSpan(0, n), out var state))
                    latest = state;
            }
            return latest;
        }
    }
}
