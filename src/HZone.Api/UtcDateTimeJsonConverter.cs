using System.Text.Json;
using System.Text.Json.Serialization;

namespace HZone.Api;

/// <summary>
/// DateTime 序列化统一带 Z 后缀（UTC）。
/// EF Core SQLite 读回的 DateTime 是 DateTimeKind.Unspecified，System.Text.Json
/// 对 Unspecified 不写时区标记，而创建接口返回的内存实体（Kind=Utc）带 Z——
/// 同一字段两条路径不一致；前端 new Date(无 Z 串) 按浏览器本地时间解析，
/// 非UTC时区用户看到的全部时间偏移时区差（UTC+8 实测偏 8 小时）。
/// 全库写入口径为 DateTime.UtcNow（无 DateTime.Now），故 Unspecified 一律按
/// UTC 矫正；Local 理论不存在，出现也归一到 UTC 保证响应格式恒定 "…Z"。
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 读侧保持 System.Text.Json 默认行为（带 Z/偏移解析正确，无标记保持 Unspecified）
        return reader.GetDateTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(
            value.Kind switch
            {
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => value
            });
    }
}
