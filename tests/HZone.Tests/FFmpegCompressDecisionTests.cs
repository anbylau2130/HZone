using HZone.Core.Entities;
using HZone.Core.Media;

namespace HZone.Tests;

/// <summary>
/// 压缩决策纯逻辑单测：不含进程调用，ffmpeg 可用性作为输入参数。
/// </summary>
public class FFmpegCompressDecisionTests
{
    [Fact]
    public void Over_limit_video_without_ffmpeg_rejects()
    {
        var action = FFmpegCompressDecision.Decide(
            MediaKind.Video, sizeBytes: 2048, limitBytes: 1024, ffmpegAvailable: false);
        Assert.Equal(MediaCompressAction.Reject, action);
    }

    [Fact]
    public void Over_limit_video_with_ffmpeg_transcodes()
    {
        var action = FFmpegCompressDecision.Decide(
            MediaKind.Video, sizeBytes: 2048, limitBytes: 1024, ffmpegAvailable: true);
        Assert.Equal(MediaCompressAction.FFmpegTranscode, action);
    }

    [Fact]
    public void Over_limit_audio_without_ffmpeg_rejects()
    {
        var action = FFmpegCompressDecision.Decide(
            MediaKind.Audio, sizeBytes: 2048, limitBytes: 1024, ffmpegAvailable: false);
        Assert.Equal(MediaCompressAction.Reject, action);
    }

    [Fact]
    public void Over_limit_audio_with_ffmpeg_transcodes()
    {
        var action = FFmpegCompressDecision.Decide(
            MediaKind.Audio, sizeBytes: 2048, limitBytes: 1024, ffmpegAvailable: true);
        Assert.Equal(MediaCompressAction.FFmpegTranscode, action);
    }

    [Fact]
    public void Within_limit_media_stores_directly()
    {
        Assert.Equal(MediaCompressAction.Store,
            FFmpegCompressDecision.Decide(MediaKind.Video, 1024, 2048, ffmpegAvailable: true));
        Assert.Equal(MediaCompressAction.Store,
            FFmpegCompressDecision.Decide(MediaKind.Audio, 1024, 2048, ffmpegAvailable: false));
    }

    [Fact]
    public void Image_always_uses_image_sharp_pipeline()
    {
        Assert.Equal(MediaCompressAction.ImageSharpCompress,
            FFmpegCompressDecision.Decide(MediaKind.Image, 1024, 2048, ffmpegAvailable: false));
        Assert.Equal(MediaCompressAction.ImageSharpCompress,
            FFmpegCompressDecision.Decide(MediaKind.Image, 2048, 1024, ffmpegAvailable: false));
        Assert.Equal(MediaCompressAction.ImageSharpCompress,
            FFmpegCompressDecision.Decide(MediaKind.Image, 2048, 1024, ffmpegAvailable: true));
    }
}
