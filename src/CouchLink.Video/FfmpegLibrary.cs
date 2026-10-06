using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace CouchLink.Video;

/// <summary>An FFmpeg call failed; the message says which and why.</summary>
public sealed class FfmpegException(string message) : Exception(message);

/// <summary>
/// Loads FFmpeg 9 (avcodec-63 and friends) from the app's <c>ffmpeg</c> folder, put there
/// from <c>third_party/ffmpeg</c> by the build (see eng/get-ffmpeg.ps1).
/// </summary>
public static unsafe class FfmpegLibrary
{
    public const string AvcodecDll = "avcodec-63.dll";
    public const int AvcodecMajor = 63;

    /// <summary>The FFmpeg DLLs the app ships and loads (avcodec needs the other three).</summary>
    public static readonly IReadOnlyList<string> RequiredDlls = [AvcodecDll, "avutil-61.dll", "swscale-10.dll", "swresample-7.dll"];

    private static readonly Lock Gate = new();
    private static bool _loaded;

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    public static bool TryLoad(out string? error, string? directory = null)
    {
        lock (Gate)
        {
            error = null;
            directory ??= DefaultDirectory;
            if (!File.Exists(Path.Combine(directory, AvcodecDll)))
            {
                error = $"FFmpeg 9 was not found in {directory}. Run eng/get-ffmpeg.ps1 and build again.";
                return false;
            }
            var missing = RequiredDlls.Where(dll => !File.Exists(Path.Combine(directory, dll))).ToList();
            if (missing.Count > 0)
            {
                error = $"FFmpeg in {directory} is missing {string.Join(", ", missing)}. Run eng/get-ffmpeg.ps1 and build again.";
                return false;
            }
            if (_loaded)
                return true; // FFmpeg can only be loaded once per process
            try
            {
                ffmpeg.RootPath = directory;
                DynamicallyLoadedBindings.Initialize();
                int major = (int)(ffmpeg.avcodec_version() >> 16);
                if (major != AvcodecMajor)
                {
                    error = $"FFmpeg in {directory} is avcodec {major}; CouchLink needs {AvcodecMajor} (FFmpeg 9).";
                    return false;
                }
            }
            // AutoGen reports a library it can't load as NotSupportedException on the first call
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or NotSupportedException)
            {
                error = $"FFmpeg in {directory} could not be loaded: {e.Message}";
                return false;
            }
            _loaded = true;
            return true;
        }
    }

    public static bool HasEncoder(string name) => ffmpeg.avcodec_find_encoder_by_name(name) != null;

    public static string ErrorText(int code)
    {
        const int size = 256;
        byte* buffer = stackalloc byte[size];
        ffmpeg.av_strerror(code, buffer, size);
        return Marshal.PtrToStringUTF8((nint)buffer) ?? $"error {code}";
    }

    public static void Check(int result, string what)
    {
        if (result < 0)
            throw new FfmpegException($"{what} failed: {ErrorText(result)}");
    }
}
