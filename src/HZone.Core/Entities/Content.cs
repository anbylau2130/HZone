namespace HZone.Core.Entities;

public abstract class Content
{
    public Guid Id { get; set; }

    public Guid AuthorId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // 天气快照（homepage-v2 spec：保存即快照，客户端采集，服务端不代查）。
    public string? WeatherCity { get; set; }

    public string? WeatherCondition { get; set; }

    public double? WeatherTempC { get; set; }

    // 农历三字段：保存时由 LunarCalendar 计算；旧内容为空，读取时惰性推导。
    public string? LunarGanzhiYear { get; set; }

    public string? LunarMonthDay { get; set; }

    public string? LunarTerm { get; set; }

    public ICollection<ContentRecipient> Recipients { get; set; } = new List<ContentRecipient>();

    // 标签（整体替换语义，保存时传全量数组；跨时间主题检索用）
    public ICollection<ContentTag> Tags { get; set; } = new List<ContentTag>();
}
