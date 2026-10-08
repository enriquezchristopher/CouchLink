using Vortice.DXGI;

namespace CouchLink.Video;

/// <summary>
/// A decoded picture. <see cref="Frame"/> is the decoder's <c>AVFrame*</c>, valid until that decoder
/// returns a newer picture or is disposed (a decode that fails or returns nothing keeps it).
/// <see cref="OnGpu"/>: a D3D11 texture (data[0]) and array slice (data[1]); otherwise YUV 4:2:0
/// planes in memory.
/// </summary>
public readonly record struct DecodedPicture(int Width, int Height, nint Frame, bool OnGpu, ColorSpaceType Color);

/// <summary>Turns one frame's H.264 into a picture. Throws <see cref="FfmpegException"/> on bad data.</summary>
public interface IFrameDecoder : IDisposable
{
    string Name { get; }
    bool IsHardware { get; }

    /// <summary>False when the decoder needs more data before it has a picture.</summary>
    bool Decode(byte[] data, out DecodedPicture picture);
}

/// <summary>
/// Shows a picture (or black, when null) with optional text: status centred, controls top-left,
/// stats top-right, hint bottom-left.
/// </summary>
public interface IFramePresenter : IDisposable
{
    void Present(DecodedPicture? picture, string? status, string? stats, string? controls, string? hint);
}
