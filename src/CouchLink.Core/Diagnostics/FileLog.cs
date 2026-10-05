using System.Globalization;

namespace CouchLink.Core.Diagnostics;

/// <summary>
/// Rolling text log (couchlink.log, couchlink.1.log ... couchlink.N.log) plus an
/// in-memory tail for crash reports. Never throws: if the disk fails, the tail
/// keeps working.
/// </summary>
public sealed class FileLog
{
    private const string BaseName = "couchlink";

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly int _tailLines;
    private readonly Queue<string> _tail = new();
    private readonly Lock _gate = new();

    public FileLog(string directory, TimeProvider? time = null, long maxBytes = 1_000_000, int maxFiles = 5, int tailLines = 200)
    {
        _directory = directory;
        _time = time ?? TimeProvider.System;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        _tailLines = tailLines;
        FilePath = Path.Combine(directory, BaseName + ".log");
    }

    public string FilePath { get; }

    public void Write(string message)
    {
        var line = $"{_time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {message}";
        lock (_gate)
        {
            _tail.Enqueue(line);
            while (_tail.Count > _tailLines)
                _tail.Dequeue();

            try
            {
                Directory.CreateDirectory(_directory);
                RollIfNeeded();
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Disk problems must never take the app down; the tail still has the line.
            }
        }
    }

    public IReadOnlyList<string> Tail()
    {
        lock (_gate)
            return _tail.ToArray();
    }

    private void RollIfNeeded()
    {
        var current = new FileInfo(FilePath);
        if (!current.Exists || current.Length < _maxBytes)
            return;

        var oldest = Numbered(_maxFiles - 1);
        if (File.Exists(oldest))
            File.Delete(oldest);
        for (int i = _maxFiles - 2; i >= 1; i--)
        {
            var source = Numbered(i);
            if (File.Exists(source))
                File.Move(source, Numbered(i + 1));
        }
        File.Move(FilePath, Numbered(1));
    }

    private string Numbered(int index) => Path.Combine(_directory, $"{BaseName}.{index}.log");
}
