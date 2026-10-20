<template>
  <view class="page" :class="mode === 'journal' ? 'journal-mode' : 'say-mode'">
    <!-- 日记头：左侧日期+农历，右侧天气（点击弹选择器修改）。类型由入口 query 决定，页内不切换 -->
    <view class="diary-head">
      <view class="dh-left">
        <text class="dh-date">{{ dateLine }}</text>
        <text v-if="lunarLine" class="dh-lunar">{{ lunarLine }}</text>
      </view>
      <view class="dh-weather" @click="openWeatherPicker">
        <text v-if="weather" class="dh-weather-text">
          {{ CONDITION_ICONS[weather.condition] }} {{ weather.city }} {{ formatTemp(weather.tempC) }}
        </text>
        <text v-else class="dh-weather-add">{{ $t('weather.add') }}</text>
      </view>
    </view>

    <!-- 随记：textarea → 媒体独立区块 → 工具行（媒体与输入区视觉分离） -->
    <view v-if="mode === 'say'" class="panel">
      <textarea
        v-model="sayText"
        class="area"
        :placeholder="$t('publish.sayPlaceholder')"
        :maxlength="-1"
        auto-height
      />
      <view v-if="mediaItems.length || uploading" class="media-block">
        <text class="media-title">{{ $t('publish.media') }}</text>
        <view class="media-grid">
          <view v-for="m in mediaItems" :key="m.localPath" class="cell">
            <image v-if="m.kind === 'image'" class="img" :src="m.localPath" mode="aspectFill" />
            <video v-else class="img" :src="m.localPath" controls />
            <text class="remove" @click="removeMedia(m.localPath)">{{ $t('common.delete') }}</text>
          </view>
          <view v-if="uploading" class="cell uploading">
            <text class="uploading-text">{{ $t('publish.uploading') }}</text>
          </view>
        </view>
      </view>
      <view v-if="pickedFriends.length" class="chips-row">
        <text v-for="r in pickedFriends" :key="r.id" class="chip">{{ r.nickname }}</text>
      </view>
      <view class="toolbar">
        <button class="pick" @click="pickImage">{{ $t('publish.addImages') }}</button>
        <!-- #ifdef H5 -->
        <view
          class="rec-btn"
          :class="{ on: recording }"
          @mousedown.prevent="onRecordStart"
          @mouseup="onRecordEnd"
          @mouseleave="onRecordEnd"
          @touchstart.prevent="onRecordStart"
          @touchend="onRecordEnd"
        >
          <text v-if="recording" class="rec-label">{{ recordLabel }}</text>
          <text v-else class="rec-ico">🎥</text>
        </view>
        <!-- #endif -->
        <button class="pick" @click="pickerVisible = true">{{ $t('publish.chooseRecipients') }}</button>
        <view class="toolbar-spacer" />
        <button class="btn-inline" :disabled="busy" @click="submit">{{ $t('publish.submit') }}</button>
      </view>
      <text class="hint">{{ $t('publish.mediaRule') }}</text>
    </view>

    <!-- 日志：标题独立（无圆角+分隔线）→ 工具栏 → Markdown 编辑 → 下方预览 -->
    <template v-else>
      <view class="panel journal-panel">
        <input v-model="title" class="j-title" :placeholder="$t('publish.titlePlaceholder')" />
        <view class="j-toolbar">
          <button class="pick small" @click="pickJournalImage">{{ $t('publish.insertImage') }}</button>
          <button class="pick small" @click="pickJournalVideo">{{ $t('publish.insertVideo') }}</button>
          <button class="pick small" @click="insertWrap('**', '**')">{{ $t('publish.bold') }}</button>
          <button class="pick small" @click="insertHeading">{{ $t('publish.heading') }}</button>
        </view>
        <textarea
          v-model="markdown"
          class="area tall"
          :placeholder="$t('publish.bodyPlaceholder')"
          :maxlength="-1"
          auto-height
        />
        <view class="md-preview" v-html="previewHtml" />
      </view>
      <view class="recipients">
        <text class="label">{{ $t('publish.recipients') }}</text>
        <view class="chips">
          <text v-for="r in pickedFriends" :key="r.id" class="chip">{{ r.nickname }}</text>
        </view>
        <button class="pick small" @click="pickerVisible = true">{{ $t('publish.chooseRecipients') }}</button>
      </view>
      <button class="btn" :disabled="busy" @click="submit">{{ $t('publish.submit') }}</button>
    </template>

    <FriendPicker v-model:visible="pickerVisible" @confirm="onFriendsConfirmed" />

    <view v-if="weatherPickerOpen" class="overlay" @click="weatherPickerOpen = false">
      <view class="sheet" @click.stop>
        <view class="head">
          <text class="sheet-title">{{ $t('weather.title') }}</text>
          <text class="cancel" @click="weatherPickerOpen = false">{{ $t('common.cancel') }}</text>
        </view>
        <view class="cond-grid">
          <view
            v-for="c in WEATHER_CONDITIONS"
            :key="c"
            class="cond"
            :class="{ on: wCondition === c }"
            @click="wCondition = c"
          >
            <text class="cond-icon">{{ CONDITION_ICONS[c] }}</text>
            <text class="cond-name">{{ $t('weather.conditions.' + c) }}</text>
          </view>
        </view>
        <view class="w-fields">
          <input v-model="wCity" class="field" :placeholder="$t('weather.city')" />
          <input v-model="wTemp" class="field" type="digit" :placeholder="$t('weather.tempPlaceholder')" />
        </view>
        <view class="sheet-actions">
          <button class="mini ghost" @click="clearWeather">{{ $t('weather.clear') }}</button>
          <button class="mini" @click="confirmWeather">{{ $t('common.confirm') }}</button>
        </view>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { marked } from 'marked'
import FriendPicker from '../../components/FriendPicker.vue'
import {
  createSay,
  createJournal,
  getMyStats,
  type FriendInfo,
  type MeStats,
  type WeatherSnapshotInput
} from '../../api/endpoints'
import { uploadFile } from '../../api/client'
import { loadMediaUrl } from '../../api/media'
import { onHide, onLoad, onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import { CONDITION_ICONS, WEATHER_CONDITIONS, getWeather, type WeatherSnapshot } from '../../utils/weather'
import { formatLunar } from '../../utils/lunar'

onShow(() => {
  setNavTitle('hub.publish')
  refreshDiaryHead()
  // 进入时自动获取天气（模块级缓存，一次会话只探一次）；失败静默降级为手动选择
  if (!weatherAutoDone) {
    weatherAutoDone = true
    getWeather()
      .then((w) => {
        if (!weather.value) weather.value = w
      })
      .catch(() => {})
  }
})

// 页面实例内只自动获取一次（用户清除后不再回填）
let weatherAutoDone = false

// 支持 ?kind=say / ?kind=journal 预选发布类型（随记/日志分页入口跳入）
onLoad((query) => {
  const q = (query ?? {}) as Record<string, string>
  if (q.kind === 'journal') {
    mode.value = 'journal'
  } else if (q.kind === 'say') {
    mode.value = 'say'
  }
})

const { t } = useI18n()

interface PickedMedia {
  localPath: string
  mediaId?: string
  kind: 'image' | 'video'
}

const mode = ref<'say' | 'journal'>('say')
const sayText = ref('')
const title = ref('')
const markdown = ref('')
const mediaItems = ref<PickedMedia[]>([])
const pickedFriends = ref<FriendInfo[]>([])
const pickerVisible = ref(false)
const uploading = ref(false)
const submitting = ref(false)

const busy = computed(() => uploading.value || submitting.value)

// ---------- 日记头：公历日期+星期 + 农历（/me/stats）+ 天气快照入口 ----------
const stats = ref<MeStats | null>(null)
const now = ref(new Date())

function refreshDiaryHead() {
  now.value = new Date()
  getMyStats()
    .then((s) => {
      stats.value = s
    })
    .catch(() => {
      // 统计/农历失败静默：日记头只显示公历
    })
}

const dateLine = computed(() => {
  const d = now.value
  const date = t('home.monthDay', { m: d.getMonth() + 1, d: d.getDate() })
  return `${date} ${t(`home.weekday${d.getDay()}`)}`
})

const lunarLine = computed(() => formatLunar(stats.value?.today))

// ---------- 天气（homepage-v2）：快照随发布提交，可改可清 ----------
const weather = ref<WeatherSnapshot | null>(null)
const weatherPickerOpen = ref(false)
const wCondition = ref<WeatherSnapshot['condition']>('sunny')
const wCity = ref('')
const wTemp = ref('')

function openWeatherPicker() {
  const w = weather.value
  wCondition.value = w?.condition ?? 'sunny'
  wCity.value = w?.city ?? ''
  wTemp.value = w ? String(Math.round(w.tempC)) : ''
  weatherPickerOpen.value = true
}

function confirmWeather() {
  const temp = Number.parseFloat(wTemp.value)
  weather.value = {
    city: wCity.value.trim(),
    condition: wCondition.value,
    tempC: Number.isFinite(temp) ? temp : 0
  }
  weatherPickerOpen.value = false
}

function clearWeather() {
  weather.value = null
  weatherPickerOpen.value = false
}

function formatTemp(tempC: number): string {
  return `${Math.round(tempC)}°C`
}

function toSnapshot(): WeatherSnapshotInput | undefined {
  return weather.value
    ? { city: weather.value.city, condition: weather.value.condition, tempC: weather.value.tempC }
    : undefined
}

// 桌面端实时预览（H5 only；marked 默认转义输出，内容可见名单本就受限）
const previewHtml = computed(() => marked.parse(markdown.value) as string)

// 预览里的 /media/ 媒体发不了 Authorization 头，复用 MediaGrid 的 blob 方案换 src（仅 H5）。
// #ifdef H5
watch(previewHtml, () => {
  nextTick(() => {
    const root = document.querySelector('.journal-panel .md-preview')
    if (!root) return
    for (const el of Array.from(root.querySelectorAll('img[src], video[src]'))) {
      const raw = el.getAttribute('src') ?? ''
      if (!raw.startsWith('/media/')) continue
      loadMediaUrl(raw)
        .then((u) => {
          el.setAttribute('src', u)
        })
        .catch(() => {})
    }
  })
})
// #endif

function onFriendsConfirmed(friends: FriendInfo[]) {
  pickedFriends.value = friends
}

const MAX_IMAGES = 9

// ---------- H5 媒体选择修复 ----------
// uni.chooseImage/chooseVideo 的 input.click() 经 defineAsyncApi/promisify 包装，在部分场景
// （自动化点击、无最新手势的任务）下浏览器会静默拦截文件对话框——表现为"点了没反应"。
// H5 改为在 @click 处理器内同步创建原生 <input type=file> 并 click()，手势链路确定可开。

function pickImage() {
  if (hasVideo()) {
    uni.showToast({ title: t('publish.videoOnlySingle'), icon: 'none' })
    return
  }
  const remaining = MAX_IMAGES - mediaItems.value.length
  if (remaining <= 0) {
    uni.showToast({ title: t('publish.maxImagesReached'), icon: 'none' })
    return
  }
  // #ifdef H5
  openH5FilePicker('image/*', remaining, (files) => {
    uploadAll(
      files.map((f) => URL.createObjectURL(f)),
      'image'
    )
  })
  // #endif
  // #ifndef H5
  uni.chooseImage({
    count: remaining,
    success: (res) => {
      uploadAll(res.tempFilePaths, 'image')
    }
  })
  // #endif
}

function pickVideo() {
  if (mediaItems.value.length > 0) {
    uni.showToast({ title: t('publish.videoOnlySingle'), icon: 'none' })
    return
  }
  // #ifdef H5
  openH5FilePicker('video/*', 1, (files) => {
    if (files.length) uploadAll([URL.createObjectURL(files[0])], 'video')
  })
  // #endif
  // #ifndef H5
  uni.chooseVideo({
    success: (res) => {
      uploadAll([res.tempFilePath], 'video')
    }
  })
  // #endif
}

/** H5 同步建 input 触发文件选择（复用 import-export 的成熟路径）。 */
function openH5FilePicker(accept: string, maxCount: number, onPicked: (files: File[]) => void) {
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = accept
  if (maxCount > 1) input.multiple = true
  input.onchange = () => {
    const files = Array.from(input.files ?? []).slice(0, maxCount)
    if (files.length) onPicked(files)
  }
  input.click()
}

function hasVideo() {
  return mediaItems.value.some((m) => m.kind === 'video')
}

function removeMedia(localPath: string) {
  // 已上传未绑定的临时媒体后端暂无删除接口（对他人不可见），本地移除即可；blob 预览 URL 一并回收
  mediaItems.value = mediaItems.value.filter((m) => m.localPath !== localPath)
  // #ifdef H5
  if (localPath.startsWith('blob:')) URL.revokeObjectURL(localPath)
  // #endif
}

async function uploadAll(paths: string[], kind: 'image' | 'video') {
  uploading.value = true
  try {
    for (const p of paths) {
      const asset = await uploadFile<{ id: string }>('/api/media', p)
      mediaItems.value.push({ localPath: p, mediaId: asset.id, kind })
    }
  } catch {
    // 上传失败提示已由请求层统一 toast；失败的文件不入列
  } finally {
    uploading.value = false
  }
}

// ---------- 朋友圈式按住录视频（仅 H5） ----------
// #ifdef H5
const MAX_RECORD_SECONDS = 60

const recording = ref(false)
const pressHeld = ref(false)
const recordSeconds = ref(0)

let mediaStream: MediaStream | null = null
let mediaRecorder: MediaRecorder | null = null
let recordChunks: Blob[] = []
let recordTimer: ReturnType<typeof setInterval> | null = null

const recordLabel = computed(() => {
  const s = recordSeconds.value
  return `● ${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
})

async function onRecordStart() {
  if (recording.value) return
  if (mediaItems.value.length > 0) {
    uni.showToast({ title: t('publish.videoOnlySingle'), icon: 'none' })
    return
  }
  if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
    uni.showToast({ title: t('publish.recordUnavailable'), icon: 'none' })
    return
  }
  pressHeld.value = true
  try {
    mediaStream = await navigator.mediaDevices.getUserMedia({ video: true, audio: true })
  } catch {
    pressHeld.value = false
    // 拒绝权限/无摄像头：提示并保留「添加视频」文件选择兜底
    uni.showToast({ title: t('publish.recordPermissionDenied'), icon: 'none' })
    return
  }
  // 等待授权期间已松手 → 直接释放
  if (!pressHeld.value) {
    releaseCamera()
    return
  }
  const mimeType = pickRecordMime()
  mediaRecorder = new MediaRecorder(mediaStream, mimeType ? { mimeType } : undefined)
  recordChunks = []
  mediaRecorder.ondataavailable = (e) => {
    if (e.data.size > 0) recordChunks.push(e.data)
  }
  mediaRecorder.onstop = finalizeRecord
  mediaRecorder.start()
  recording.value = true
  recordSeconds.value = 0
  recordTimer = setInterval(() => {
    recordSeconds.value++
    // 上限保护：录满自动停（长按意外滞留不至于无限录）
    if (recordSeconds.value >= MAX_RECORD_SECONDS) onRecordEnd()
  }, 1000)
}

function onRecordEnd() {
  pressHeld.value = false
  if (recording.value && mediaRecorder && mediaRecorder.state !== 'inactive') {
    mediaRecorder.stop()
  }
}

function finalizeRecord() {
  stopRecordTimer()
  recording.value = false
  releaseCamera()
  const type = mediaRecorder?.mimeType || 'video/webm'
  const blob = new Blob(recordChunks, { type })
  recordChunks = []
  mediaRecorder = null
  if (blob.size === 0) return
  // 视频独占：清掉已有媒体（上面 start 已挡，这里兜底）
  mediaItems.value = [{ localPath: URL.createObjectURL(blob), kind: 'video' }]
}

function pickRecordMime(): string | undefined {
  const candidates = ['video/webm;codecs=vp9,opus', 'video/webm;codecs=vp8,opus', 'video/webm', 'video/mp4']
  return candidates.find((m) => MediaRecorder.isTypeSupported(m))
}

function releaseCamera() {
  mediaStream?.getTracks().forEach((track) => track.stop())
  mediaStream = null
}

function stopRecordTimer() {
  if (recordTimer !== null) {
    clearInterval(recordTimer)
    recordTimer = null
  }
}

// 组件卸载/页面隐藏时释放摄像头流
onUnmounted(() => {
  stopRecordTimer()
  if (recording.value && mediaRecorder && mediaRecorder.state !== 'inactive') {
    mediaRecorder.onstop = null
    mediaRecorder.stop()
  }
  releaseCamera()
})
onHide(() => {
  if (recording.value) onRecordEnd()
})
// #endif

// ---------- 日志工具栏：光标处插入 markdown（H5 读真实 textarea 的 selection） ----------
function insertWrap(prefix: string, suffix = '') {
  const value = markdown.value
  let start = value.length
  let end = value.length
  // #ifdef H5
  const el = document.querySelector<HTMLTextAreaElement>('.journal-panel textarea')
  if (el) {
    start = el.selectionStart ?? value.length
    end = el.selectionEnd ?? value.length
  }
  // #endif
  markdown.value = value.slice(0, start) + prefix + value.slice(start, end) + suffix + value.slice(end)
}

function insertHeading() {
  const value = markdown.value
  let pos = value.length
  // #ifdef H5
  const el = document.querySelector<HTMLTextAreaElement>('.journal-panel textarea')
  if (el) {
    pos = el.selectionStart ?? value.length
  }
  // #endif
  const lineStart = value.lastIndexOf('\n', Math.max(0, pos - 1)) + 1
  markdown.value = value.slice(0, lineStart) + '## ' + value.slice(lineStart)
}

// ---------- 日志附件上传：复用 /api/media，返回 url 以 markdown 语法插入光标处 ----------
// journal 无 mediaIds 绑定（后端 MediaAsset.ContentId 只绑 say），直接以 url 进 markdown。
function pickJournalImage() {
  // #ifdef H5
  openH5FilePicker('image/*', 1, async (files) => {
    await insertUploadedMedia(files, 'image')
  })
  // #endif
  // #ifndef H5
  uni.chooseImage({
    success: (res) => {
      insertUploadedMedia(res.tempFilePaths, 'image')
    }
  })
  // #endif
}

function pickJournalVideo() {
  // #ifdef H5
  openH5FilePicker('video/*', 1, (files) => {
    if (files.length) insertUploadedMedia([files[0]], 'video')
  })
  // #endif
  // #ifndef H5
  uni.chooseVideo({
    success: (res) => {
      insertUploadedMedia([res.tempFilePath], 'video')
    }
  })
  // #endif
}

async function insertUploadedMedia(files: (File | string)[], kind: 'image' | 'video') {
  uploading.value = true
  try {
    for (const f of files) {
      const path = typeof f === 'string' ? f : URL.createObjectURL(f)
      const asset = await uploadFile<{ id: string; url: string }>('/api/media', path)
      if (kind === 'image') {
        insertWrap(`![${t('publish.imageAlt')}](${asset.url})`)
      } else {
        insertWrap(`<video src="${asset.url}" controls></video>\n`)
      }
    }
  } catch {
    // 上传失败提示已由请求层统一 toast
  } finally {
    uploading.value = false
  }
}

async function submit() {
  if (busy.value) return
  const recipientIds = pickedFriends.value.map((f) => f.id)
  submitting.value = true
  try {
    if (mode.value === 'say') {
      if (!sayText.value.trim() && mediaItems.value.length === 0) {
        uni.showToast({ title: t('publish.sayEmpty'), icon: 'none' })
        return
      }
      // 补传本地媒体（如刚录制的视频尚无 mediaId）
      uploading.value = true
      try {
        for (const m of mediaItems.value) {
          if (!m.mediaId) {
            const asset = await uploadFile<{ id: string }>('/api/media', m.localPath)
            m.mediaId = asset.id
          }
        }
      } catch {
        return // 上传失败提示已由请求层统一 toast
      } finally {
        uploading.value = false
      }
      const mediaIds = mediaItems.value
        .map((m) => m.mediaId)
        .filter((id): id is string => Boolean(id))
      await createSay(sayText.value, recipientIds, mediaIds, toSnapshot())
    } else {
      if (!title.value.trim() || !markdown.value.trim()) {
        uni.showToast({ title: t('publish.journalEmpty'), icon: 'none' })
        return
      }
      await createJournal(title.value, markdown.value, recipientIds, toSnapshot())
    }
    uni.showToast({ title: t('publish.success'), icon: 'success' })
    setTimeout(() => {
      uni.navigateBack()
    }, 600)
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  box-sizing: border-box;
}
/* 媒体独立区块：自带边框/底色/小标题，与输入区视觉分离 */
.media-block {
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  background: var(--hz-color-bg);
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.media-title {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
/* 日记头：低调的日记本页眉，14px，secondary 色，日期 primary 强调 */
.diary-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.dh-left {
  display: flex;
  align-items: baseline;
  gap: 8px;
  flex-wrap: wrap;
}
.dh-date {
  font-size: 14px;
  font-weight: 600;
  color: var(--hz-color-primary);
}
.dh-lunar {
  font-size: 14px;
  color: var(--hz-color-text-secondary);
}
.dh-weather {
  cursor: pointer;
  user-select: none;
}
.dh-weather-text {
  font-size: 14px;
  color: var(--hz-color-text-secondary);
}
.dh-weather-add {
  font-size: 14px;
  color: var(--hz-color-interactive);
}
.panel {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.area {
  width: 100%;
  min-height: 120px;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.area.tall {
  min-height: 260px;
}
/* 日志：标题无圆角+分隔线，与编辑器明显分离 */
.j-title {
  height: 48px;
  font-size: 18px;
  font-weight: 600;
  border: none;
  border-bottom: 1px solid var(--hz-color-border);
  border-radius: 0;
  background: transparent;
  color: var(--hz-color-text);
}
.j-toolbar {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}
/* uni-button 默认宽度铺满，工具栏小按钮收紧 */
.j-toolbar .pick {
  flex: none;
  width: auto;
  margin: 0;
}
.md-preview {
  display: none;
}
.field {
  height: 44px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 12px;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  background: var(--hz-color-bg);
}
.media-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 6px;
}
.cell {
  position: relative;
  aspect-ratio: 1;
  border-radius: var(--hz-radius-md);
  overflow: hidden;
}
.img {
  width: 100%;
  height: 100%;
}
.remove {
  position: absolute;
  right: 4px;
  bottom: 4px;
  font-size: 11px;
  color: #fff;
  background: rgba(0, 0, 0, 0.55);
  border-radius: 8px;
  padding: 1px 6px;
}
.cell.uploading {
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px dashed var(--hz-color-border);
  background: var(--hz-color-bg);
}
.uploading-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.chips-row {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}
.chip {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
  background: var(--hz-color-bg);
  border-radius: var(--hz-radius-md);
  padding: 2px 8px;
}
/* 工具行：+图片 / 按住录视频 / 选择好友，发布按钮居右 */
.toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.pick {
  background: var(--hz-color-bg);
  color: var(--hz-color-primary);
  border: 1px dashed var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
}
.pick.small {
  padding: 0 12px;
  height: 32px;
  line-height: 32px;
  font-size: var(--hz-font-size-sm);
}
/* 按住录视频圆形按钮（仅 H5） */
.rec-btn {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  border: 1px dashed var(--hz-color-border);
  background: var(--hz-color-bg);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  user-select: none;
  touch-action: none;
  flex-shrink: 0;
}
.rec-btn.on {
  background: var(--hz-color-accent);
  border-color: var(--hz-color-accent);
  border-style: solid;
}
.rec-ico {
  font-size: 18px;
  line-height: 1;
}
.rec-label {
  font-size: 11px;
  color: #fff;
  line-height: 1;
}
.toolbar-spacer {
  flex: 1;
}
.hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  text-align: right;
}
.btn-inline {
  height: 36px;
  line-height: 36px;
  padding: 0 20px;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
}
.btn-inline[disabled] {
  opacity: 0.6;
}
.overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: flex-end;
  justify-content: center;
  z-index: 999;
}
.sheet {
  width: 100%;
  max-width: 520px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg) var(--hz-radius-lg) 0 0;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.sheet-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.cancel {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.cond-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 8px;
}
.cond {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  padding: 8px 0;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
}
.cond.on {
  border-color: var(--hz-color-primary);
  background: var(--hz-color-bg);
}
.cond-icon {
  font-size: 22px;
}
.cond-name {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
.w-fields {
  display: flex;
  gap: 8px;
}
.field {
  flex: 1;
  height: 38px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 12px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  background: var(--hz-color-bg);
}
.sheet-actions {
  display: flex;
  gap: 8px;
}
.mini {
  flex: 1;
  height: 38px;
  line-height: 38px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
}
.mini.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
.recipients {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 12px;
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.chips {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}
.btn {
  height: 44px;
  line-height: 44px;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-base);
}
@media (min-width: 768px) {
  .page {
    padding: 32px max(24px, calc((100% - 80rem) / 2));
  }
  /* say 短内容：单栏限宽居中（覆盖视口 padding 公式，否则 672px 盒子被 185px×2 的 padding 挤扁） */
  .page.say-mode {
    max-width: calc(42rem + 48px);
    margin: 0 auto;
    padding: 32px 24px;
  }
  /* journal：上下布局——编辑在上（40vh），预览在下（50vh 可滚动） */
  .journal-panel .area.tall {
    height: 40vh !important; /* 覆盖 uni-app auto-height 的行内样式 */
    min-height: 0;
    font-size: 15px;
    line-height: 1.8;
    font-family: ui-monospace, SFMono-Regular, Consolas, 'Courier New', monospace;
  }
  .md-preview {
    display: block;
    height: 50vh;
    overflow-y: auto;
    background: var(--hz-color-bg);
    border: 1px solid var(--hz-color-border);
    border-radius: var(--hz-radius-lg);
    padding: 16px 20px;
    font-size: 15px;
    line-height: 1.8;
  }
  .btn {
    align-self: flex-end;
    width: 160px;
    margin-left: auto;
  }
}
</style>
