using HZone.Core.Lunar;

namespace HZone.Tests;

/// <summary>
/// 农历纯单测（homepage-v2 ticket 01）。节气口径：通式寿星公式（21 世纪常数表），
/// 与实际节气日允许 ±1 天；断言采用 2024-02-04 立春（公式与实际一致）等锚点。
/// </summary>
public class LunarCalendarTests
{
    [Fact]
    public void Cny_anchors_resolve_ganzhi_and_monthday()
    {
        // 春节锚点：干支年随农历年切换（2024-02-04 尚属癸卯年）。
        Assert.Equal(("癸卯年", "腊月廿五", "立春"), LunarCalendar.Compute(China(2024, 2, 4)));
        Assert.Equal(("甲辰年", "正月初一", "立春"), LunarCalendar.Compute(China(2024, 2, 10)));
        Assert.Equal(("乙巳年", "正月初一", "大寒"), LunarCalendar.Compute(China(2025, 1, 29, 12)));
        Assert.Equal(("丙午年", "正月初一", "立春"), LunarCalendar.Compute(China(2026, 2, 17)));
    }

    [Fact]
    public void Mid_autumn_and_term_days()
    {
        Assert.Equal(("丙午年", "八月十五", "秋分"), LunarCalendar.Compute(China(2026, 9, 25)));
        Assert.Equal(("丙午年", "九月十一", "寒露"), LunarCalendar.Compute(China(2026, 10, 20)));
        // 节气当天：2024 立春（2/4）与 2026 寒露（10/8，公式日）。
        Assert.Equal("立春", LunarCalendar.Compute(China(2024, 2, 4)).Term);
        Assert.Equal("寒露", LunarCalendar.Compute(China(2026, 10, 8)).Term);
    }

    [Fact]
    public void New_year_eve_boundary_29_vs_30()
    {
        // 2023 有年三十（2023-01-21 = 腊月三十）；2022 除夕为腊月廿九（2022-01-31）。
        Assert.Equal(("壬寅年", "腊月三十", "大寒"), LunarCalendar.Compute(China(2023, 1, 21)));
        Assert.Equal(("辛丑年", "腊月廿九", "大寒"), LunarCalendar.Compute(China(2022, 1, 31)));
    }

    private static DateTimeOffset China(int y, int m, int d, int hour = 10) =>
        new DateTimeOffset(new DateTime(y, m, d, hour, 0, 0), TimeSpan.FromHours(8));
}
