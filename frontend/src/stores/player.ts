/**
 * 全局播放器（账号级播放列表）：状态与音频上下文全部挂在**模块顶层**——
 * uni-app H5 每个页面是独立 Vue 应用、pinia 不跨页共享（实测 auth 也靠每页
 * fetchMe 重拉），pinia store 在切页后是全新实例；而 Vite 模块身份全局唯一，
 * 模块级单例（refs + InnerAudioContext）才能真正跨页存活、播放不中断。
 * UI 三件套共用本 store：首页右栏 PlayerPanel（ToolDock ≥1200px）、
 * MiniPlayer 全局桥（<1200px 常驻 + 移动端抽屉）。
 * 音源双轨：url 项直接拉流；media 项走 loadMediaUrl 授权 blob（/media 需 Bearer，
 * 音频标签与 <img> 同样带不了请求头）。
 */
import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  listPlaylist,
  addPlaylistItem,
  reorderPlaylist,
  removePlaylistItem,
  type PlaylistItem
} from '../api/endpoints'
import { loadMediaUrl } from '../api/media'
import { i18n } from '../locale'

// ---------- 模块级单例状态（跨页面应用共享的真正载体） ----------
const items = ref<PlaylistItem[]>([])
const loaded = ref(false)
const currentIndex = ref(-1)
const playing = ref(false)
const position = ref(0) // 秒
const duration = ref(0) // 秒
const loopMode = ref<'list' | 'one'>('list')
const switching = ref(false) // 换曲中（src 异步解析），防重入

const current = computed(() =>
  currentIndex.value >= 0 ? items.value[currentIndex.value] ?? null : null
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
    if (items.value.length > 1 && consecutiveErrors < items.value.length) {
      void next()
    }
  })
  return ctx
}

/** 拉取账号播放列表（幂等；已拉过不重复拉）。 */
async function load(force = false) {
  if (loaded.value && !force) return
  try {
    const res = await listPlaylist()
    items.value = res.items
    if (currentIndex.value >= items.value.length) currentIndex.value = -1
    loaded.value = true
  } catch {
    // 请求层已 toast；面板保持空列表态
  }
}

/** 解析可播放 src：url 直连；media 换授权 blob（模块级缓存，二次播放零开销）。 */
async function resolveSrc(item: PlaylistItem): Promise<string> {
  if (item.sourceKind === 'url') return item.url ?? ''
  if (!item.mediaStoredName) throw new Error('missing mediaStoredName')
  return loadMediaUrl(`/media/${item.mediaStoredName}`)
}

async function playIndex(i: number) {
  if (i < 0 || i >= items.value.length) return
  if (switching.value) return
  switching.value = true
  currentIndex.value = i
  position.value = 0
  duration.value = 0
  try {
    const src = await resolveSrc(items.value[i])
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
    if (items.value.length) void playIndex(0)
    return
  }
  if (playing.value) c.pause()
  else c.play()
}

async function next() {
  if (!items.value.length) return
  void playIndex((currentIndex.value + 1) % items.value.length)
}

async function prev() {
  if (!items.value.length) return
  void playIndex((currentIndex.value - 1 + items.value.length) % items.value.length)
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

async function removeAndSync(id: string) {
  try {
    await removePlaylistItem(id)
  } catch {
    return // 请求层已 toast
  }
  const idx = items.value.findIndex((x) => x.id === id)
  if (idx < 0) return
  const wasCurrent = idx === currentIndex.value
  items.value = items.value.filter((x) => x.id !== id)
  if (wasCurrent) {
    // 删的是当前曲：停播并尽量接播同位置下一首
    ctx?.stop()
    playing.value = false
    currentIndex.value = -1
    if (items.value.length) void playIndex(Math.min(idx, items.value.length - 1))
  } else if (idx < currentIndex.value) {
    currentIndex.value -= 1
  }
}

/** 上移/下移：本地换序 + 服务端整体重排。 */
async function moveAndSync(index: number, dir: -1 | 1) {
  const j = index + dir
  if (j < 0 || j >= items.value.length) return
  const swapped = [...items.value]
  ;[swapped[index], swapped[j]] = [swapped[j], swapped[index]]
  const wasCurrent = currentIndex.value === index
  const wasCurrentJ = currentIndex.value === j
  items.value = swapped
  if (wasCurrent) currentIndex.value = j
  else if (wasCurrentJ) currentIndex.value = index
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
  loaded,
  currentIndex,
  playing,
  position,
  duration,
  loopMode,
  current,
  load,
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
