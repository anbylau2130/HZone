namespace HZone.Core.Entities;

public class Say : Content
{
    public string Text { get; set; } = null!;

    /// <summary>心情档位 1~5（1 最低 5 最高；随记可选标记，null = 未标）。心情追踪×天气分析用。</summary>
    public int? MoodLevel { get; set; }
}
