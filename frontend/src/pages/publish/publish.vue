<template>
  <view class="page" :class="mode === 'journal' ? 'journal-mode' : 'say-mode'">
    <view class="modes">
      <button class="mode" :class="{ on: mode === 'say' }" @click="mode = 'say'">
        {{ $t('publish.sayMode') }}
      </button>
      <button class="mode" :class="{ on: mode === 'journal' }" @click="mode = 'journal'">
        {{ $t('publish.journalMode') }}
      </button>
    </view>

    <view v-if="mode === 'say'" class="panel">
      <textarea
        v-model="sayText"
        class="area"
        :placeholder="$t('publish.sayPlaceholder')"
        :maxlength="-1"
        auto-height
      />
      <view v-if="mediaItems.length" class="media-grid">
        <view v-for="m in mediaItems" :key="m.localPath" class="cell">
          <image class="img" :src="m.localPath" mode="aspectFill" />
          <text class="remove" @click="removeMedia(m.localPath)">{{ $t('common.delete') }}</text>
        </view>
      </view>
      <view class="pick-row">
        <button class="pick" @click="pickImage">{{ $t('publish.addImages') }}</button>
        <button class="pick" @click="pickVideo">{{ $t('publish.addVideo') }}</button>
      </view>
      <text class="hint">{{ $t('publish.mediaRule') }}</text>
    </view>

    <view v-else class="panel">
      <input v-model="title" class="field" :placeholder="$t('publish.titlePlaceholder')" />
      <view class="journal-cols">
        <textarea
          v-model="markdown"
          class="area tall"
          :placeholder="$t('publish.bodyPlaceholder')"
          :maxlength="-1"
          auto-height
        />
        <view class="md-preview" v-html="previewHtml" />
      </view>
    </view>

    <view class="weather" @click="openWeatherPicker">
      <template v-if="weather">
        <text class="w-icon">{{ CONDITION_ICONS[weather.condition] }}</text>
        <text class="w-text">
          {{ weather.city }} · {{ $t('weather.conditions.' + weather.condition) }} ·
          {{ formatTemp(weather.tempC) }}
        </text>
        <text class="w-action">{{ $t('weather.edit') }}</text>
      </template>
      <template v-else>
        <text class="w-icon">📍</text>
        <text class="w-action">{{ $t('weather.add') }}</text>
      </template>
    </view>

    <view class="recipients">
      <text class="label">{{ $t('publish.recipients') }}</text>
      <view class="chips">
        <text v-for="r in pickedFriends" :key="r.id" class="chip">{{ r.nickname }}</text>
      </view>
      <button class="pick small" @click="pickerVisible = true">{{ $t('publish.chooseRecipients') }}</button>
    </view>

    <button class="btn" :disabled="busy" @click="submit">{{ $t('publish.submit') }}</button>

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
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { marked } from 'marked'
import FriendPicker from '../../components/FriendPicker.vue'
import {
  createSay,
  createJournal,
  type FriendInfo,
  type WeatherSnapshotInput
} from '../../api/endpoints'
import { uploadFile } from '../../api/client'
import { onLoad, onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import { CONDITION_ICONS, WEATHER_CONDITIONS, getWeather, type WeatherSnapshot } from '../../utils/weather'

onShow(() => {
  setNavTitle('hub.publish')
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

function onFriendsConfirmed(friends: FriendInfo[]) {
  pickedFriends.value = friends
}

const MAX_IMAGES = 9

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
  uni.chooseImage({
    count: remaining,
    success: (res) => {
      uploadAll(res.tempFilePaths, 'image')
    }
  })
}

function pickVideo() {
  if (mediaItems.value.length > 0) {
    uni.showToast({ title: t('publish.videoOnlySingle'), icon: 'none' })
    return
  }
  uni.chooseVideo({
    success: (res) => {
      uploadAll([res.tempFilePath], 'video')
    }
  })
}

function hasVideo() {
  return mediaItems.value.some((m) => m.kind === 'video')
}

function removeMedia(localPath: string) {
  // 已上传未绑定的临时媒体后端暂无删除接口（对他人不可见），本地移除即可。
  mediaItems.value = mediaItems.value.filter((m) => m.localPath !== localPath)
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

async function submit() {
  if (busy.value) return
  const recipientIds = pickedFriends.value.map((f) => f.id)
  submitting.value = true
  try {
    if (mode.value === 'say') {
      const mediaIds = mediaItems.value
        .map((m) => m.mediaId)
        .filter((id): id is string => Boolean(id))
      if (!sayText.value.trim() && mediaIds.length === 0) {
        uni.showToast({ title: t('publish.sayEmpty'), icon: 'none' })
        return
      }
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
.modes {
  display: flex;
  gap: 8px;
}
.mode {
  flex: 1;
  height: 40px;
  line-height: 40px;
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-base);
  border: 1px solid var(--hz-color-border);
}
.mode.on {
  background: var(--hz-color-primary);
  color: #fff;
  border-color: var(--hz-color-primary);
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
/* 移动端：预览隐藏，布局与双栏前的原始结构一致 */
.journal-cols {
  display: flex;
  flex-direction: column;
  gap: 10px;
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
.pick-row {
  display: flex;
  gap: 8px;
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
.hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.weather {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 10px 12px;
  display: flex;
  align-items: center;
  gap: 8px;
}
.w-icon {
  font-size: 18px;
}
.w-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
.w-action {
  margin-left: auto;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
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
.chip {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
  background: var(--hz-color-bg);
  border-radius: var(--hz-radius-md);
  padding: 2px 8px;
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
  /* journal：桌面双栏编辑器 */
  .journal-cols {
    flex-direction: row;
    gap: 16px;
    align-items: stretch;
  }
  .journal-cols .area.tall {
    flex: 1;
    height: 70vh !important; /* 覆盖 uni-app auto-height 的行内样式 */
    min-height: 0;
    font-size: 15px;
    line-height: 1.8;
    font-family: ui-monospace, SFMono-Regular, Consolas, 'Courier New', monospace;
  }
  .md-preview {
    display: block;
    flex: 1;
    max-width: 42rem;
    height: 70vh;
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
