/**
 * 农历展示（homepage-v2）：农历推导（干支年 + 月日 + 节气）由后端统一计算——
 * 保存时用 .NET ChineseLunisolarCalendar 落库，读取时惰性填充（ContentEndpoints.LunarOf，
 * 落库缺失时按 createdAt 推导），前端不重复实现算法，只把 DTO 的 lunar 字段格式化为展示文案。
 * 与后端口径一致：ganzhiYear 形如「乙巳年」，monthDay 形如「腊月初一」，term 为节气名或空串。
 */

export interface LunarInfo {
  ganzhiYear: string
  monthDay: string
  term: string
}

/**
 * 格式化为展示文案：`乙巳年 腊月初一 · 大雪`（无节气省略段）。
 * 无 lunar 字段或 ganzhiYear 为空 → null（上层隐藏农历段）。
 */
export function formatLunar(lunar?: LunarInfo | null): string | null {
  if (!lunar?.ganzhiYear) return null
  const base = `${lunar.ganzhiYear} ${lunar.monthDay}`
  return lunar.term ? `${base} · ${lunar.term}` : base
}
