using HZone.Core.Entities;

namespace HZone.Core.Media;

/// <summary>
/// 媒体上传处理动作。
/// </summary>
public enum MediaCompressAction
{
    /// <summary>限内直存（视频/音频）。</summary>
    Store,

    /// <summary>图片管线：重编码 + 缩略图（ImageSharp），压缩后仍超限 → 413。</summary>
    ImageSharpCompress,

    /// <summary>视频/音频超限且 ffmpeg 可用：单次转码，产物仍超限 → 413。</summary>
    FFmpegTranscode,

    /// <summary>超限且无法压缩 → 413。</summary>
    Reject,
}

/// <summary>
/// 媒体压缩决策（纯逻辑，可单测）：该不该压/能不能压，不含进程调用。
/// 图片永远走 ImageSharp 管线（缩略图必须生成），ffmpeg 只服务视频/音频。
/// </summary>
public static class FFmpegCompressDecision
{
    public static MediaCompressAction Decide(MediaKind kind, long sizeBytes, long limitBytes, bool ffmpegAvailable)
    {
        if (sizeBytes <= limitBytes)
        {
            // 图片即使限内也走 ImageSharp（缩略图必须生成）；视频/音频限内直存。
            return kind == MediaKind.Image ? MediaCompressAction.ImageSharpCompress : MediaCompressAction.Store;
        }

        return kind switch
        {
            MediaKind.Image => MediaCompressAction.ImageSharpCompress,
            MediaKind.Video or MediaKind.Audio => ffmpegAvailable
                ? MediaCompressAction.FFmpegTranscode
                : MediaCompressAction.Reject,
            _ => MediaCompressAction.Reject,
        };
    }
}
