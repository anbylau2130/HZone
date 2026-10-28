/**
 * lrc 歌词解析：`[mm:ss.xx]文本` → 按时间升序的行。
 * 支持一行多时间标签（[00:01.0][00:05.0]副歌）、毫秒 2~3 位、
 * 增强格式（offset 忽略——少见且展示无感）。坏行静默跳过。
 */

export interface LrcLine {
  time: number
  text: string
}

const TAG = /\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?\]/g

export function parseLrc(raw: string | null | undefined): LrcLine[] {
  if (!raw) return []
  const lines: LrcLine[] = []
  for (const row of raw.split(/\r?\n/)) {
    TAG.lastIndex = 0
    const tags: number[] = []
    let m: RegExpExecArray | null
    let lastEnd = 0
    while ((m = TAG.exec(row))) {
      const min = Number(m[1])
      const sec = Number(m[2])
      const fracRaw = m[3] ?? '0'
      const frac = Number(fracRaw) / 10 ** fracRaw.length
      if (min <= 99 && sec < 60) tags.push(min * 60 + sec + frac)
      lastEnd = TAG.lastIndex
    }
    if (!tags.length) continue
    const text = row.slice(lastEnd).trim()
    if (!text) continue
    for (const t of tags) lines.push({ time: t, text })
  }
  return lines.sort((a, b) => a.time - b.time)
}

/** 当前应显示的行下标：最后一个 time ≤ pos 的行；无歌词/未到首行返回 -1。 */
export function lrcIndexAt(lines: LrcLine[], pos: number): number {
  if (!lines.length) return -1
  let lo = 0
  let hi = lines.length - 1
  let ans = -1
  while (lo <= hi) {
    const mid = (lo + hi) >> 1
    if (lines[mid].time <= pos) {
      ans = mid
      lo = mid + 1
    } else {
      hi = mid - 1
    }
  }
  return ans
}
