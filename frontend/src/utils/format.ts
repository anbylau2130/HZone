import { i18n } from '../locale'

/** ISO 时间 → 本地 "YYYY-MM-DD HH:mm"。 */
export function formatDateTime(iso: string): string {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** ISO 时间 → 相对时间（刚刚 / n 分钟前 / n 小时前 / 昨天 / MM-DD / YYYY-MM-DD）。 */
export function formatRelativeTime(iso: string): string {
  const d = new Date(iso)
  const now = Date.now()
  const diffMin = Math.floor((now - d.getTime()) / 60000)
  if (diffMin < 1) return '刚刚'
  if (diffMin < 60) return `${diffMin} 分钟前`
  const diffHour = Math.floor(diffMin / 60)
  if (diffHour < 24) return `${diffHour} 小时前`
  const pad = (n: number) => String(n).padStart(2, '0')
  const sameYear = d.getFullYear() === new Date().getFullYear()
  const md = `${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
  return sameYear ? md : `${d.getFullYear()}-${md}`
}

// ---------- 中国区日期分组（moments-wechat 票 02） ----------

export interface DateGroup<T> {
  /** 中国区 yyyy-MM-dd。 */
  key: string
  /** 组头标签：今天/昨天/M月/yyyy年M月（随界面语言）。 */
  label: string
  /** 该日内容，新→旧。 */
  items: T[]
}

const CN_OFFSET_MS = 8 * 3_600_000

/** 毫秒时间戳 → 中国区（UTC+8）yyyy-MM-dd。 */
function cnDayKey(ms: number): string {
  return new Date(ms + CN_OFFSET_MS).toISOString().slice(0, 10)
}

/**
 * 内容按中国区日期分组、组间倒序（组内也新→旧）。
 * 与后端 MeStats/LunarCalendar 的 UTC+8 口径一致，而非浏览器本地时区。
 */
export function groupByCnDate<T extends { createdAt: string }>(list: T[]): DateGroup<T>[] {
  const todayKey = cnDayKey(Date.now())
  const yestKey = cnDayKey(Date.now() - 86_400_000)
  const map = new Map<string, T[]>()
  for (const it of list) {
    const key = cnDayKey(new Date(it.createdAt).getTime())
    const bucket = map.get(key)
    if (bucket) bucket.push(it)
    else map.set(key, [it])
  }
  return [...map.keys()]
    .sort()
    .reverse()
    .map((key) => ({
      key,
      label: cnDayLabel(key, todayKey, yestKey),
      items: (map.get(key) ?? []).sort(
        (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
      )
    }))
}

/** 组头标签：今天/昨天；同年 M月；往年 yyyy年M月。 */
function cnDayLabel(key: string, todayKey: string, yestKey: string): string {
  if (key === todayKey) return i18n.global.t('moments.today')
  if (key === yestKey) return i18n.global.t('moments.yesterday')
  const [y, m, d] = key.split('-')
  const mm = String(Number(m))
  const dd = String(Number(d))
  return y === todayKey.slice(0, 4)
    ? i18n.global.t('moments.dayMonth', { m: mm, d: dd })
    : i18n.global.t('moments.yearMonth', { y, m: mm, d: dd })
}

/** Markdown 摘要：去掉标题井号/强调符号/链接语法等标记，只留纯文本（列表项换行→空格）。 */
export function stripMarkdown(md: string): string {
  return md
    .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/^#{1,6}\s+/gm, '')
    .replace(/[*_`~>]+/g, '')
    .replace(/^\s*[-+]\s+/gm, '')
    .replace(/\s+/g, ' ')
    .trim()
}
