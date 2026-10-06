using System.Diagnostics;
using CouchLink.Pads;

namespace CouchLink.PadTest;

/// <summary>
/// Leak check: plugs in and removes a virtual pad many times (like players being
/// kicked and rejoining). Each removed pad knowingly keeps one stuck ViGEm thread
/// and a few handles (see ViGEmPad.Dispose); this fails if a pad costs more than
/// that, or if removing pads crashes the process.
/// </summary>
internal static class ChurnMode
{
    private const double AllowedHandlesPerPad = 4;
    private const double AllowedThreadsPerPad = 1;

    public static int Run(int cycles)
    {
        if (!ViGEmPadFactory.TryCreate(out var factory, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        using (factory)
        {
            for (int i = 0; i < 5; i++) // warm-up: first pads load drivers, threads, caches
                factory!.Create().Dispose();
            var (threadsBefore, handlesBefore) = Snapshot();

            for (int i = 0; i < cycles; i++)
                factory!.Create().Dispose();
            var (threadsAfter, handlesAfter) = Snapshot();

            double handlesPerPad = (double)(handlesAfter - handlesBefore) / cycles;
            double threadsPerPad = (double)(threadsAfter - threadsBefore) / cycles;
            bool ok = handlesPerPad <= AllowedHandlesPerPad && threadsPerPad <= AllowedThreadsPerPad;
            Console.WriteLine($"{cycles} pads created and removed. " +
                $"Handles: {handlesBefore} -> {handlesAfter} ({handlesPerPad:0.0}/pad, limit {AllowedHandlesPerPad}). " +
                $"Threads: {threadsBefore} -> {threadsAfter} ({threadsPerPad:0.00}/pad, limit {AllowedThreadsPerPad}). " +
                (ok ? "ok" : "FAIL: removed pads leak more than expected"));
            return ok ? 0 : 1;
        }
    }

    private static (int Threads, int Handles) Snapshot()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var self = Process.GetCurrentProcess();
        return (self.Threads.Count, self.HandleCount);
    }
}
