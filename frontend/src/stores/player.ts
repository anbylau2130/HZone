/**
 * 全局播放器（账号级播放列表 + 服务器音乐库）：状态与音频上下文全部挂在
 * **模块顶层**——uni-app H5 每个页面是独立 Vue 应用、pinia 不跨页共享（实测
 * auth 也靠每页 fetchMe 重拉），pinia store 在切页后是全新实例；而 Vite 模块
 * 身份全局唯一，模块级单例（refs + InnerAudioContext）才能真正跨页存活、
 * 播放不中断。
 * 列表 = tracks 合并序列：服务器音乐库（admin 配置的磁盘目录，虚拟项，在前）
 * + 用户播放列表项（media 上传 / url 外链，在后）；播放/切歌/循环对整个序列
 * 生效，增删/排序仅作用于用户项。
 * 音源三轨：url 直连；media 走 loadMediaUrl 授权 blob；server 走
 * /api/player/file/{name} 授权 blob（音频标签与 <img> 同样带不了请求头）。
 */
import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  listPlaylist,
  addPlaylistItem,
  reorderPlaylist,
  removePlaylistItem,
  getServerMusicLibrary,
  type PlaylistItem
} from '../api/endpoints'
import { loadMediaUrl } from '../api/media'
import { i18n } from '../locale'

/** 播放器统一曲目：用户项（PlaylistItem）+ 服务器音乐虚拟项的并集形状。 */
export interface PlayerTrack {
  id: string
  sourceKind: 'media' | 'url' | 'server'
  title: string
  url: string | null
  mediaStoredName: string | null
  /** server 项的文件名（配置目录顶层音频文件）。 */
  serverFile: string | null
  sortOrder: number
  createdAt: string
}

// ---------- 模块级单例状态（跨页面应用共享的真正载体） ----------
const items = ref<PlaylistItem[]>([])
const serverFiles = ref<{ name: string; sizeBytes: number }[]>([])
const musicDir = ref<string | null>(null)
const loaded = ref(false)
const currentIndex = ref(-1)
const playing = ref(false)
const position = ref(0) // 秒
const duration = ref(0) // 秒
const loopMode = ref<'list' | 'one'>('list')
const switching = ref(false) // 换曲中（src 异步解析），防重入

/** 合并曲目序列：服务器音乐（默认曲库，在前）+ 用户项（在后）。 */
const tracks = computed<PlayerTrack[]>(() => [
  ...serverFiles.value.map(
    (f): PlayerTrack => ({
      id: `srv:${f.name}`,
      sourceKind: 'server',
      title: f.name.replace(/\.[^.]+$/, ''),
      url: null,
      mediaStoredName: null,
      serverFile: f.name,
      sortOrder: 0,
      createdAt: ''
    })
  ),
  ...items.value.map(
    (it): PlayerTrack => ({
      ...it,
      serverFile: null
    })
  )
])

const current = computed(() =>
  currentIndex.value >= 0 ? tracks.value[currentIndex.value] ?? null : null
)

// 单例音频上下文：H5 底层即 audio 元素，App 端为原生播放器；挂模块层不随页面卸载
let ctx: UniApp.InnerAudioContext | null = null
// 连续失败计数（onPlay 清零）：列表循环下坏曲自动跳下一首，
// 但整表连坏时收手——避免坏表无限空转
let consecutiveErrors = 0
function ensureCtx(): UniApp.InnerAudioContext {
  if (ctx) return ctx
  ctx = uni.createInnerAudioContext()
  // 调试钩子（e2e/排查用）：window.__playerCtx 暴露单例上下文
  ;(globalThis as { __playerCtx?: unknown }).__playerCtx = ctx
  ctx.onTimeUpdate(() => {
    position.value = ctx!.currentTime
    duration.value = ctx!.duration || 0
  })
  ctx.onPlay(() => {
    playing.value = true
    consecutiveErrors = 0
  })
  ctx.onPause(() => (playing.value = false))
  ctx.onStop(() => (playing.value = false))
  ctx.onEnded(() => {
    if (loopMode.value === 'one') {
      ctx!.seek(0)
      ctx!.play()
    } else {
      void next()
    }
  })
  ctx.onError(() => {
    // 拉流失败（外链失效/文件损坏/格式不支持）：提示后跳下一首——
    // 坏曲不应终结整表播放；全表连坏（≥表长）才真正停下
    playing.value = false
    uni.showToast({ title: i18n.global.t('player.loadFailed'), icon: 'none' })
    consecutiveErrors++
    if (tracks.value.length > 1 && consecutiveErrors < tracks.value.length) {
      void next()
    }
  })
  return ctx
}

/** 拉取账号播放列表 + 服务器音乐库（幂等；已拉过不重复拉）。 */
async function load(force = false) {
  if (loaded.value && !force) return
  const wasId = currentId()
  try {
    const [pl, lib] = await Promise.all([listPlaylist(), getServerMusicLibrary()])
    items.value = pl.items
    serverFiles.value = lib.files
    musicDir.value = lib.musicDir
    loaded.value = true
    restoreIndexById(wasId)
  } catch {
    // 请求层已 toast；面板保持已有状态
  }
}

/** 重载服务器音乐库（admin 改目录后调）。 */
async function reloadServerLibrary() {
  try {
    const lib = await getServerMusicLibrary()
    serverFiles.value = lib.files
    musicDir.value = lib.musicDir
    const wasId = currentId()
    restoreIndexById(wasId)
  } catch {
    // 请求层已 toast
  }
}

function currentId(): string | null {
  return tracks.value[currentIndex.value]?.id ?? null
}

function restoreIndexById(id: string | null) {
  if (!id) return
  const i = tracks.value.findIndex((t) => t.id === id)
  currentIndex.value = i // 消失（被删/库清空）则 -1
}

/** 解析可播放 src：url 直连；media/server 换授权 blob（模块级缓存，二次播放零开销）。 */
async function resolveSrc(track: PlayerTrack): Promise<string> {
  if (track.sourceKind === 'url') return track.url ?? ''
  if (track.sourceKind === 'server') {
    if (!track.serverFile) throw new Error('missing serverFile')
    return loadMediaUrl(`/api/player/file/${encodeURIComponent(track.serverFile)}`)
  }
  if (!track.mediaStoredName) throw new Error('missing mediaStoredName')
  return loadMediaUrl(`/media/${track.mediaStoredName}`)
}

async function playIndex(i: number) {
  if (i < 0 || i >= tracks.value.length) return
  if (switching.value) return
  switching.value = true
  currentIndex.value = i
  position.value = 0
  duration.value = 0
  try {
    const src = await resolveSrc(tracks.value[i])
    const c = ensureCtx()
    c.stop()
    c.src = src
    c.play()
  } catch {
    // blob 换取失败（401/网络）：提示停住
    playing.value = false
    uni.showToast({ title: i18n.global.t('player.loadFailed'), icon: 'none' })
  } finally {
    switching.value = false
  }
}

function toggle() {
  const c = ensureCtx()
  if (current.value == null) {
    // 未选曲：默认播第一首
    if (tracks.value.length) void playIndex(0)
    return
  }
  if (playing.value) c.pause()
  else c.play()
}

async function next() {
  if (!tracks.value.length) return
  void playIndex((currentIndex.value + 1) % tracks.value.length)
}

async function prev() {
  if (!tracks.value.length) return
  void playIndex((currentIndex.value - 1 + tracks.value.length) % tracks.value.length)
}

function seek(sec: number) {
  if (!ctx || !current.value) return
  ctx.seek(sec)
  position.value = sec
}

function toggleLoopMode() {
  loopMode.value = loopMode.value === 'list' ? 'one' : 'list'
}

/** 面板增删/重排后的本地同步（避免整表重拉）。 */
function applyAdded(item: PlaylistItem) {
  items.value = [...items.value, item]
}

/** 删除用户列表项（服务器音乐为虚拟项，不可删——不响应）。 */
async function removeAndSync(id: string) {
  if (id.startsWith('srv:')) return
  try {
    await removePlaylistItem(id)
  } catch {
    return // 请求层已 toast
  }
  const wasCurrent = currentId() === id
  items.value = items.value.filter((x) => x.id !== id)
  if (wasCurrent) {
    // 删的是当前曲：停播并接播原位置的下一首（删除后它顶到同一坐标）
    ctx?.stop()
    playing.value = false
    const i = currentIndex.value
    currentIndex.value = -1
    if (i < tracks.value.length) void playIndex(i)
  } else {
    restoreIndexById(currentId())
  }
}

/** 用户项上移/下移（id 定位，服务器项不响应）：本地换序 + 服务端整体重排。 */
async function moveAndSync(id: string, dir: -1 | 1) {
  const idx = items.value.findIndex((x) => x.id === id)
  if (idx < 0) return
  const j = idx + dir
  if (j < 0 || j >= items.value.length) return
  const swapped = [...items.value]
  ;[swapped[idx], swapped[j]] = [swapped[j], swapped[idx]]
  items.value = swapped
  try {
    await reorderPlaylist(swapped.map((x) => x.id))
  } catch {
    void load(true) // 重排失败：回源重拉真实顺序
  }
}

async function addUrl(title: string, url: string) {
  const item = await addPlaylistItem({ sourceKind: 'url', title, url })
  applyAdded(item)
  return item
}

async function addMedia(mediaAssetId: string, title?: string) {
  const item = await addPlaylistItem({ sourceKind: 'media', mediaAssetId, title })
  applyAdded(item)
  return item
}

/** pinia 薄壳：uni H5 每页独立 pinia 实例，但 setup 闭包到的都是上面的模块态。 */
export const usePlayerStore = defineStore('player', () => ({
  items,
  serverFiles,
  musicDir,
  loaded,
  currentIndex,
  playing,
  position,
  duration,
  loopMode,
  tracks,
  current,
  load,
  reloadServerLibrary,
  playIndex,
  toggle,
  next,
  prev,
  seek,
  toggleLoopMode,
  removeAndSync,
  moveAndSync,
  addUrl,
  addMedia
}))
