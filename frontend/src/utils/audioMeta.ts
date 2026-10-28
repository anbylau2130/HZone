/**
 * 音频元数据探测（上传前在浏览器侧完成，随表单落库）：
 * - 时长：Audio 元素 loadedmetadata（mp3/m4a/wav/ogg 通用）
 * - 艺术家：ID3v2.2/2.3/2.4 TPE1(TP1) 文本帧——手写极简解析器，免依赖
 *   （pnpm+uni 的 resolver 对传递依赖不友好，见 mermaid 教训）
 * - 比特率：体积×8/时长的平均码率（CBR 近似，够展示用）
 * 全部尽力而为：任何一步失败只缺对应字段，绝不阻断上传。
 */

export interface AudioMeta {
  durationSeconds: number | null
  artist: string | null
  bitrateKbps: number | null
}

export async function probeAudio(file: File): Promise<AudioMeta> {
  const [durationSeconds, artist] = await Promise.all([
    probeDuration(file),
    readId3Artist(file)
  ])
  const bitrateKbps =
    durationSeconds && durationSeconds > 1
      ? Math.round((file.size * 8) / 1000 / durationSeconds)
      : null
  return { durationSeconds, artist, bitrateKbps }
}

/** 时长探测：object URL + metadata 预载；5s 超时/解码失败返回 null。 */
function probeDuration(file: File): Promise<number | null> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file)
    const audio = new Audio()
    let settled = false
    const done = (v: number | null) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      URL.revokeObjectURL(url)
      resolve(v)
    }
    const timer = setTimeout(() => done(null), 5000)
    audio.preload = 'metadata'
    audio.onloadedmetadata = () =>
      done(Number.isFinite(audio.duration) && audio.duration > 0 ? audio.duration : null)
    audio.onerror = () => done(null)
    audio.src = url
  })
}

/** ID3v2 艺术家：标签头在文件开头，帧区一般 < 256KB，只读前缀切片。 */
async function readId3Artist(file: File): Promise<string | null> {
  try {
    const head = new Uint8Array(await file.slice(0, 262_144).arrayBuffer())
    if (head.length < 10 || head[0] !== 0x49 || head[1] !== 0x44 || head[2] !== 0x33) {
      return null // 无 "ID3" 头
    }
    const major = head[3]
    // 标签总长（syncsafe 整数，不含头部 10 字节）
    const tagSize = (head[6] << 21) | (head[7] << 14) | (head[8] << 7) | head[9]
    const end = Math.min(head.length, 10 + tagSize)
    const artistFrameId = major <= 2 ? 'TP1' : 'TPE1'
    let offset = 10
    while (offset + 10 <= end) {
      const id = ascii(head, offset, artistFrameId.length)
      if (!/^[A-Z0-9]{3,4}$/.test(id)) break // 越界进音频数据：结束
      let size: number
      if (major <= 2) {
        size = (head[offset + 3] << 16) | (head[offset + 4] << 8) | head[offset + 5]
        offset += 6
      } else if (major === 4) {
        size = (head[offset + 4] << 21) | (head[offset + 5] << 14) | (head[offset + 6] << 7) | head[offset + 7]
        offset += 10
      } else {
        size = (head[offset + 4] << 24) | (head[offset + 5] << 16) | (head[offset + 6] << 8) | head[offset + 7]
        offset += 10
      }
      if (size <= 0 || offset + size > end) break
      if (id === artistFrameId) {
        return decodeTextFrame(head.subarray(offset, offset + size))
      }
      offset += size
    }
    return null
  } catch {
    return null
  }
}

/** 文本帧解码：首字节编码 0=ISO-8859-1 1=UTF-16(BOM) 2=UTF-16BE 3=UTF-8。 */
function decodeTextFrame(bytes: Uint8Array): string | null {
  if (bytes.length < 2) return null
  const body = bytes.subarray(1) // 跳过编码字节
  let text: string
  try {
    if (bytes[0] === 1) {
      // BOM 判序，无 BOM 按 LE
      const be = body[0] === 0xfe && body[1] === 0xff
      const b = be || !(body[0] === 0xff && body[1] === 0xfe) ? body : body.subarray(2)
      text = new TextDecoder(be ? 'utf-16be' : 'utf-16le').decode(b)
    } else if (bytes[0] === 2) {
      text = new TextDecoder('utf-16be').decode(body)
    } else if (bytes[0] === 3) {
      text = new TextDecoder('utf-8').decode(body)
    } else {
      text = new TextDecoder('iso-8859-1').decode(body)
    }
  } catch {
    return null
  }
  const cleaned = text.replace(/\0+$/g, '').replace(/\0/g, ' ').trim()
  return cleaned.length > 0 && cleaned.length <= 200 ? cleaned : null
}

function ascii(b: Uint8Array, offset: number, len: number): string {
  let s = ''
  for (let i = 0; i < len; i++) s += String.fromCharCode(b[offset + i])
  return s
}

/** m:ss（≥1h 用 h:mm:ss）。 */
export function fmtDuration(sec: number | null | undefined): string | null {
  if (!sec || !Number.isFinite(sec) || sec <= 0) return null
  const s = Math.round(sec)
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const r = s % 60
  return h > 0
    ? `${h}:${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}`
    : `${m}:${String(r).padStart(2, '0')}`
}

/** 人类可读大小（KB/MB）。 */
export function fmtSize(n: number | null | undefined): string | null {
  if (!n || n <= 0) return null
  return n >= 1048576 ? `${(n / 1048576).toFixed(1)}MB` : `${Math.max(1, Math.round(n / 1024))}KB`
}

/** 列表行第二行元信息：艺术家 · 时长 · 比特率 · 大小 · 来源（探测到的才显示）。 */
export function metaLine(m: {
  artist?: string | null
  durationSeconds?: number | null
  bitrateKbps?: number | null
  sizeBytes?: number | null
  sourceLabel?: string | null
}): string {
  return [
    m.artist,
    fmtDuration(m.durationSeconds),
    m.bitrateKbps ? `${m.bitrateKbps}kbps` : null,
    fmtSize(m.sizeBytes),
    m.sourceLabel
  ]
    .filter(Boolean)
    .join(' · ')
}
