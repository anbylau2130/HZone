using System.ComponentModel;
using System.Diagnostics;

namespace HZone.Api.Services;

/// <summary>
/// ffmpeg 可用性与转码能力（进程调用出口；决策逻辑在 FFmpegCompressDecision，可单测）。
/// </summary>
public interface IFFmpegCompressor
{
    /// <summary>探测 PATH 中的 ffmpeg（结果缓存）。</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct);

    /// <summary>视频单次转码：H.264 crf28 + 最长边 1280 + AAC。失败/超时返回 null。</summary>
    Task<byte[]?> TranscodeVideoAsync(byte[] input, CancellationToken ct);

    /// <summary>音频单次转码：AAC 64k。失败/超时返回 null。</summary>
    Task<byte[]?> TranscodeAudioAsync(byte[] input, CancellationToken ct);
}

/// <summary>
/// System.Diagnostics.Process 封装：探测超时 5s、转码超时 120s（超时 kill）。
/// HZone:FFmpeg:Available = true/false 可强制指定（e2e 确定性），缺省走探测。
/// </summary>
public sealed class FFmpegCompressor : IFFmpegCompressor
{
    public const string AvailableSettingKey = "HZone:FFmpeg:Available";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TranscodeTimeout = TimeSpan.FromSeconds(120);

    private readonly IConfiguration _config;
    private Task<bool>? _available;

    public FFmpegCompressor(IConfiguration config) => _config = config;

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        // 配置强制指定时不探测，e2e 可确定性模拟无 ffmpeg 环境。
        if (bool.TryParse(_config[AvailableSettingKey], out var forced))
        {
            _available = Task.FromResult(forced);
            return forced;
        }

        _available ??= ProbeAsync();
        try
        {
            return await _available;
        }
        catch (Exception)
        {
            // 探测失败重置缓存，下次请求可重试。
            _available = null;
            return false;
        }
    }

    private static async Task<bool> ProbeAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                },
            };
            if (!process.Start())
            {
                return false;
            }

            using var timeoutCts = new CancellationTokenSource(ProbeTimeout);
            await process.WaitForExitAsync(timeoutCts.Token);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or OperationCanceledException)
        {
            return false;
        }
    }

    public Task<byte[]?> TranscodeVideoAsync(byte[] input, CancellationToken ct) =>
        RunFfmpegAsync(
            "-vf \"scale='min(1280,iw)':'min(1280,ih)':force_original_aspect_ratio=decrease\" " +
            "-c:v libx264 -crf 28 -preset veryfast -c:a aac -b:a 128k -movflags +faststart",
            input, ".mp4", ct);

    public Task<byte[]?> TranscodeAudioAsync(byte[] input, CancellationToken ct) =>
        RunFfmpegAsync("-vn -c:a aac -b:a 64k", input, ".m4a", ct);

    private static async Task<byte[]?> RunFfmpegAsync(
        string arguments, byte[] input, string inputExtension, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hzone-ffmpeg", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var inPath = Path.Combine(tempDir, "input" + inputExtension);
            var outPath = Path.Combine(tempDir, "output.mp4");
            await File.WriteAllBytesAsync(inPath, input, ct);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -hide_banner -loglevel error -i \"{inPath}\" {arguments} \"{outPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                },
            };
            if (!process.Start())
            {
                return null;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TranscodeTimeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 转码超时（非请求取消）：kill 进程树，按失败处理。
                try { process.Kill(entireProcessTree: true); } catch { /* 尽力清理 */ }
                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            return await File.ReadAllBytesAsync(outPath, ct);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* 尽力清理 */ }
        }
    }
}
