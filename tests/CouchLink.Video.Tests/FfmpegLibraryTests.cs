using CouchLink.Video;
using FFmpeg.AutoGen;

namespace CouchLink.Video.Tests;

public class FfmpegLibraryTests
{
    [Fact]
    public void Missing_FFmpeg_gives_a_clear_message()
    {
        var empty = Directory.CreateTempSubdirectory("couchlink-no-ffmpeg").FullName;
        try
        {
            Assert.False(FfmpegLibrary.TryLoad(out var error, empty));
            Assert.Contains("FFmpeg 9", error);
            Assert.Contains("get-ffmpeg.ps1", error);
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    [Fact]
    public void A_missing_FFmpeg_DLL_gives_a_clear_message()
    {
        var partial = Directory.CreateTempSubdirectory("couchlink-partial-ffmpeg").FullName;
        try
        {
            File.Copy(Path.Combine(FfmpegLibrary.DefaultDirectory, FfmpegLibrary.AvcodecDll),
                Path.Combine(partial, FfmpegLibrary.AvcodecDll));

            Assert.False(FfmpegLibrary.TryLoad(out var error, partial));
            Assert.Contains("avutil-61.dll", error);
            Assert.Contains("get-ffmpeg.ps1", error);
        }
        finally
        {
            Directory.Delete(partial, recursive: true);
        }
    }

    [Fact]
    public void FFmpeg_9_loads_from_the_app_folder_and_has_the_encoders()
    {
        Assert.True(FfmpegLibrary.TryLoad(out var error), error);
        Assert.Equal(FfmpegLibrary.AvcodecMajor, (int)(ffmpeg.avcodec_version() >> 16));
        foreach (var name in new[] { "h264_amf", "h264_nvenc", "libx264" })
            Assert.True(FfmpegLibrary.HasEncoder(name), name);
    }

    [Fact]
    public void Errors_are_readable()
    {
        Assert.True(FfmpegLibrary.TryLoad(out var error), error);
        var e = Assert.Throws<FfmpegException>(() => FfmpegLibrary.Check(ffmpeg.AVERROR(40) /* ENOSYS */, "Opening h264_nvenc"));
        Assert.Equal("Opening h264_nvenc failed: Function not implemented", e.Message);
    }
}
