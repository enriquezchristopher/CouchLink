using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CouchLink.Core.Diagnostics;
using Microsoft.Win32;

namespace CouchLink.App.Diagnostics;

/// <summary>Collects crash-report facts. Every probe is guarded: a failure becomes "unknown".</summary>
internal static class SystemInfo
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static CrashContext Collect(string mode) => new(
        AppVersion: Probe(AppVersion),
        Time: DateTimeOffset.Now,
        Uptime: DateTime.Now - AppServices.Started,
        Windows: Probe(() => RuntimeInformation.OSDescription),
        Runtime: Probe(() => RuntimeInformation.FrameworkDescription),
        Cpu: Probe(Cpu),
        RamBytes: GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        Gpu: Probe(Gpu),
        ViGEmBus: Probe(ViGEmBus),
        Mode: Probe(() => mode));

    private static string AppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SystemInfo).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }

    private static string Cpu()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        var name = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "unknown CPU";
        return $"{name} ({Environment.ProcessorCount} threads)";
    }

    private static string Gpu()
    {
        using var display = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (display is null)
            return "unknown";
        var adapters = new List<string>();
        foreach (var sub in display.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
        {
            using var adapter = display.OpenSubKey(sub);
            if (adapter?.GetValue("DriverDesc") is string desc)
                adapters.Add($"{desc} (driver {adapter.GetValue("DriverVersion") ?? "?"})");
        }
        return adapters.Count > 0 ? string.Join("; ", adapters.Distinct()) : "unknown";
    }

    private static string ViGEmBus()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "ViGEmBus.sys");
        return File.Exists(path)
            ? $"installed, version {FileVersionInfo.GetVersionInfo(path).FileVersion ?? "?"}"
            : "not installed";
    }

    private static string Probe(Func<string> probe)
    {
        try
        {
            return probe();
        }
        catch (Exception e)
        {
            return $"unknown ({e.GetType().Name})";
        }
    }
}
