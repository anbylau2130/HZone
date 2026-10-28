/**
 * 音频元数据探测（上传前在浏览器侧完成，随表单落库）：
 * - 时长：Audio 元素 loadedmetadata（mp3/m4a/wav/ogg 通用）
 * - 艺术家：按容器格式分流——
 *   mp3  → ID3v2.2/2.3/2.4 TPE1(TP1) 文本帧
 *   m4a  → MP4 ilst ©ART/aART（iTunes 元数据 atom）
 *   flac → VORBIS_COMMENT 块 ARTIST=
 *   ogg  → 首包 vorbis comment / OpusTags ARTIST=
 *   全部手写极简解析器，免依赖（pnpm+uni 的 resolver 对传递依赖不友好，见 mermaid 教训）
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
    readArtist(file)
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

/** 艺术家解析：按文件头分流到对应容器解析器（读一次前缀切片）。 */
async function readArtist(file: File): Promise<string | null> {
  try {
    const head = new Uint8Array(await file.slice(0, 524_288).arrayBuffer())
    if (head.length < 12) return null
    if (head[0] === 0x49 && head[1] === 0x44 && head[2] === 0x33) {
      return readId3Artist(head)
    }
    if (ascii(head, 4, 4) === 'ftyp') {
      return readMp4Artist(head)
    }
    if (ascii(head, 0, 4) === 'fLaC') {
      return readFlacArtist(head)
    }
    if (ascii(head, 0, 4) === 'OggS') {
      return readOggArtist(head)
    }
    return null
  } catch {
    return null
  }
}

/** ID3v2 艺术家：标签头在文件开头，帧区一般 < 256KB。 */
function readId3Artist(head: Uint8Array): string | null {
  if (head.length < 10) return null
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
}

/** MP4 艺术家：moov→udta→meta→ilst→©ART/aART→data（UTF-8 文本）。 */
function readMp4Artist(head: Uint8Array): string | null {
  const walk = (start: number, end: number, path: string[]): { body: number; size: number } | null => {
    let off = start
    while (off + 8 <= end) {
      const size = u32be(head, off)
      const type = ascii(head, off + 4, 4)
      if (size < 8 || off + size > end) return null
      if (type === path[0]) {
        const bodyStart = off + 8 + (type === 'meta' ? 4 : 0) // meta 有 4 字节 version/flags
        if (path.length === 1) return { body: bodyStart, size: size - (bodyStart - off) }
        const sub = walk(bodyStart, off + size, path.slice(1))
        if (sub) return sub
      }
      off += size
    }
    return null
  }
  // ilst 里每个条目是 "©ART"/"aART" atom，内嵌 data atom：4 长度+4 类型+4 版本+4 保留 + 文本
  for (const tag of ['\u00A9ART', 'aART']) {
    const item = walk(0, head.length, ['moov', 'udta', 'meta', 'ilst', tag])
    if (!item) continue
    const data = walk(item.body, item.body + item.size, ['data'])
    if (!data || data.size <= 16) continue
    const text = new TextDecoder('utf-8')
      .decode(head.subarray(data.body + 8, data.body + data.size))
      .replace(/\0+$/g, '')
      .trim()
    if (text.length > 0 && text.length <= 200) return text
  }
  return null
}

/** FLAC 艺术家：fLaC + 元数据块，type 4 = VORBIS_COMMENT。 */
function readFlacArtist(head: Uint8Array): string | null {
  let off = 4
  while (off + 4 <= head.length) {
    const header = head[off]
    const last = (header & 0x80) !== 0
    const type = header & 0x7f
    const size = (head[off + 1] << 16) | (head[off + 2] << 8) | head[off + 3]
    if (off + 4 + size > head.length) return null
    if (type === 4) {
      return parseVorbisComment(head.subarray(off + 4, off + 4 + size))
    }
    if (last) return null
    off += 4 + size
  }
  return null
}

/** OGG 艺术家：首包找 "\x03vorbis"（vorbis）或 "OpusTags"（opus）后接 vorbis comment。 */
function readOggArtist(head: Uint8Array): string | null {
  const win = head.subarray(0, Math.min(head.length, 65_536))
  for (const magic of ['\x03vorbis', 'OpusTags']) {
    const at = indexOfBytes(win, magic)
    if (at < 0) continue
    const body = head.subarray(at + magic.length, Math.min(head.length, at + magic.length + 8192))
    const artist = parseVorbisComment(body)
    if (artist) return artist
  }
  return null
}

/** Vorbis comment：u32le vendor 长 + vendor + u32le 条目数 + 各 u32le "K=V"。取 ARTIST。 */
function parseVorbisComment(b: Uint8Array): string | null {
  try {
    let off = 0
    const vendorLen = u32le(b, off)
    off += 4 + vendorLen
    const count = u32le(b, off)
    off += 4
    if (count > 1000) return null
    for (let i = 0; i < count && off + 4 <= b.length; i++) {
      const len = u32le(b, off)
      off += 4
      if (len > b.length - off) return null
      const entry = new TextDecoder('utf-8').decode(b.subarray(off, off + len))
      off += len
      const eq = entry.indexOf('=')
      if (eq > 0 && entry.slice(0, eq).toUpperCase() === 'ARTIST') {
        const v = entry.slice(eq + 1).trim()
        if (v.length > 0 && v.length <= 200) return v
      }
    }
  } catch {
    /* 尽力而为 */
  }
  return null
}

function indexOfBytes(b: Uint8Array, s: string): number {
  const target = new TextEncoder().encode(s)
  outer: for (let i = 0; i + target.length <= b.length; i++) {
    for (let j = 0; j < target.length; j++) {
      if (b[i + j] !== target[j]) continue outer
    }
    return i
  }
  return -1
}

function u32be(b: Uint8Array, off: number): number {
  return (b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3]
}

function u32le(b: Uint8Array, off: number): number {
  return b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24)
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
