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
