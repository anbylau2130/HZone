using System.Globalization;

namespace HZone.Core.Lunar;

/// <summary>
/// 农历三字段纯逻辑计算（homepage-v2）：干支年 + 农历月日 + 节气。
/// 输入任意 DateTimeOffset，转中国标准时间（UTC+8）后取日期。
/// 节气口径：简化"通式寿星公式" D = int(Y*0.2422 + C) - L，Y = 公历年 mod 100，
/// L = (Y-1)/4，C 为各节气 21 世纪常数表；与实际节气日允许 ±1 天误差。
/// 节气字段 = 该日期最近一个已过的节气名（节气期而非仅节气当天）。
/// </summary>
public static class LunarCalendar
{
    private static readonly ChineseLunisolarCalendar Calendar = new();

    private static readonly string[] Stems = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };

    private static readonly string[] Branches = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };

    private static readonly string[] MonthNames =
    {
        "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月",
    };

    // 农历日名：初一..初十 / 十一..十九 / 二十 / 廿一..廿九 / 三十。
    private static readonly string[] DayNames = BuildDayNames();

    // (公历月, 名, 21 世纪常数 C)，顺序即一年中先后。
    private static readonly (int Month, string Name, double C)[] Terms =
    {
        (1, "小寒", 5.4055),
        (1, "大寒", 20.12),
        (2, "立春", 3.87),
        (2, "雨水", 18.73),
        (3, "惊蛰", 5.63),
        (3, "春分", 20.646),
        (4, "清明", 4.81),
        (4, "谷雨", 20.1),
        (5, "立夏", 5.52),
        (5, "小满", 21.04),
        (6, "芒种", 5.678),
        (6, "夏至", 21.37),
        (7, "小暑", 7.108),
        (7, "大暑", 22.83),
        (8, "立秋", 7.5),
        (8, "处暑", 23.13),
        (9, "白露", 7.646),
        (9, "秋分", 23.042),
        (10, "寒露", 8.318),
        (10, "霜降", 23.438),
        (11, "立冬", 7.438),
        (11, "小雪", 22.36),
        (12, "大雪", 7.18),
        (12, "冬至", 21.94),
    };

    /// <summary>计算农历三字段。</summary>
    public static (string GanzhiYear, string MonthDay, string Term) Compute(DateTimeOffset timestamp)
    {
        var china = TimeZoneInfo.ConvertTime(timestamp, ChinaZone());
        var date = china.DateTime.Date;
        return (GanzhiYear(Calendar.GetYear(date)), LunarMonthDay(date), CurrentTerm(date));
    }

    private static TimeZoneInfo ChinaZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (Exception)
        {
            return TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        }
    }

    private static string GanzhiYear(int lunarYear)
    {
        return Stems[(lunarYear - 4) % 10] + Branches[(lunarYear - 4) % 12] + "年";
    }

    private static string LunarMonthDay(DateTime date)
    {
        var lunarYear = Calendar.GetYear(date);
        var lunarMonth = Calendar.GetMonth(date);
        var lunarDay = Calendar.GetDayOfMonth(date);
        var leapMonth = Calendar.GetLeapMonth(lunarYear);
        // GetMonth 计入闰月（闰 X 月 = X+1），换算为展示月序。
        var displayMonth = leapMonth == 0 ? lunarMonth : lunarMonth <= leapMonth ? lunarMonth : lunarMonth - 1;
        var monthName = MonthNames[displayMonth - 1];
        return monthName + DayNames[lunarDay - 1];
    }

    private static string CurrentTerm(DateTime date)
    {
        string? current = null;
        DateTime latest = default;
        foreach (var year in new[] { date.Year - 1, date.Year })
        {
            foreach (var (month, name, c) in Terms)
            {
                var termDate = new DateTime(year, month, TermDay(year, c));
                if (termDate <= date && (current is null || termDate > latest))
                {
                    current = name;
                    latest = termDate;
                }
            }
        }

        return current ?? string.Empty;
    }

    private static int TermDay(int year, double c)
    {
        var y = year % 100;
        return (int)(y * 0.2422 + c) - (y - 1) / 4;
    }

    private static string[] BuildDayNames()
    {
        var tens = new[] { "初", "十", "廿", "卅" };
        var days = new string[30];
        for (var day = 1; day <= 30; day++)
        {
            days[day - 1] = day <= 10
                ? tens[0] + DayTens(day)
                : day <= 19
                    ? "十" + DayTens(day - 10)
                    : day == 20 ? "二十" : day == 30 ? "三十" : tens[2] + DayTens(day - 20);
        }

        return days;
    }

    private static string DayTens(int n)
    {
        return n switch
        {
            1 => "一",
            2 => "二",
            3 => "三",
            4 => "四",
            5 => "五",
            6 => "六",
            7 => "七",
            8 => "八",
            9 => "九",
            _ => "十",
        };
    }
}
