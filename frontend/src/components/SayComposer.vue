<template>
  <!-- 随记页内快速写说说（不跳 publish 页）：轻编辑器 = 日期/农历/天气头 + 文本 + 表情 + 图片/语音。
       传入 edit 即为原地编辑某条随记（保存走 PUT，取消还原；编辑态不显示头/不重捕天气） -->
  <view class="composer" :class="{ editing: isEdit }">
    <view v-if="!isEdit" class="c-head">
      <view class="c-head-left">
        <text class="c-head-date">{{ dateLine }}</text>
        <text v-if="lunarLine" class="c-head-lunar">{{ lunarLine }}</text>
      </view>
      <text v-if="weather" class="c-head-weather">
        {{ CONDITION_ICONS[weather.condition] }} {{ weather.city }} {{ Math.round(weather.tempC) }}°C
      </text>
    </view>
    <textarea
      v-if="isEdit"
      v-model="text"
      ref="areaRef"
      class="c-area"
      :maxlength="-1"
      auto-height
    />
    <textarea
      v-if="!isEdit"
      v-model="text"
      ref="areaRef"
      class="c-area"
      :placeholder="$t('publish.sayPlaceholder')"
      :maxlength="-1"
      auto-height
    />
    <EmojiPicker v-if="emojiOpen" @select="onEmoji" />
    <view v-if="mediaItems.length" class="c-media">
      <view v-for="m in mediaItems" :key="m.preview" class="c-cell">
        <!-- 语音：麦占位格（发布后卡片内点「播放语音」收听） -->
        <view v-if="m.kind === 'audio'" class="c-audio">
          <text class="c-audio-ico">🎤</text>
          <text class="c-audio-label">{{ $t('publish.voiceClip') }}</text>
        </view>
        <image v-else class="c-img" :src="m.preview" mode="aspectFill" />
        <text class="c-remove" @click="removeMedia(m.preview)">×</text>
      </view>
    </view>
    <view class="c-bar">
      <view class="c-ico" :class="{ on: emojiOpen }" @click="emojiOpen = !emojiOpen">
        <text class="c-ico-txt">😊</text>
      </view>
      <view class="c-ico" @click="pickImage">
        <text class="c-ico-txt">📷</text>
      </view>
      <!-- 语音随记（与发布页同款）：点击开始录音（≤60s 自停），再点提前停 -->
      <view class="c-ico" :class="{ on: voice.recording.value }" @click="voice.toggle(mediaItems.length === 0)">
        <text v-if="voice.recording.value" class="c-rec-sec">{{ voice.seconds.value }}s</text>
        <text v-else class="c-ico-txt">🎤</text>
      </view>
      <view class="c-spacer" />
      <button v-if="isEdit" class="c-cancel" :disabled="busy" @click="emit('cancel')">
        {{ $t('common.cancel') }}
      </button>
      <button class="c-send" :disabled="!canSend || busy" @click="send">
        {{ busy ? $t('common.loading') : isEdit ? $t('common.save') : $t('publish.submit') }}
      </button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import EmojiPicker from './EmojiPicker.vue'
import { createSay, updateContent, getMyStats, type ContentItem } from '../api/endpoints'
import { uploadFile } from '../api/client'
import { loadMediaUrl } from '../api/media'
import { useVoiceRecorder } from '../composables/useVoiceRecorder'
import { getWeather, CONDITION_ICONS, type WeatherSnapshot } from '../utils/weather'
import { formatLunar } from '../utils/lunar'
import { useI18n } from 'vue-i18n'

// 编辑模式：传入已有随记则回填（随记页原地编辑，不跳 publish）；
// 保存走 PUT（recipientIds/weather 不传 = 后端保持原值），新建走 POST
const props = defineProps<{ edit?: ContentItem | null }>()
const emit = defineEmits<{
  (e: 'published', c: ContentItem): void
  (e: 'updated', c: ContentItem): void
  (e: 'cancel'): void
}>()

const { t } = useI18n()
const text = ref('')
const emojiOpen = ref(false)
const busy = ref(false)

// ---------- 日记头（新建态）：日期/星期 + 农历 + 自动天气 ----------
// 与发布页同源：天气走 getWeather 模块缓存（失败静默降级），农历来自 /me/stats
const weather = ref<WeatherSnapshot | null>(null)
const lunarLine = ref<string | null>(null)

const dateLine = computed(() => {
  const now = new Date()
  const date = t('home.monthDay', { m: now.getMonth() + 1, d: now.getDate() })
  return `${date} ${t(`home.weekday${now.getDay()}`)}`
})

onMounted(() => {
  getWeather()
    .then((w) => (weather.value = w))
    .catch(() => {})
  getMyStats()
    .then((s) => (lunarLine.value = formatLunar(s.today)))
    .catch(() => {})
})

interface PickedMedia {
  preview: string
  mediaId?: string
  file?: Blob
  kind: 'image' | 'video' | 'audio'
}

const mediaItems = ref<PickedMedia[]>([])

// 语音随记（共享 composable，与发布页同款）：录音结果直接入媒体格（语音独占）
const voice = useVoiceRecorder((r) => {
  mediaItems.value.push({ preview: r.previewUrl, file: r.file, kind: 'audio' })
})
const canSend = computed(() => text.value.trim().length > 0 || mediaItems.value.length > 0)
const isEdit = computed(() => !!props.edit)

// 编辑回填：文本 + 已有媒体（带 mediaId 重提交；预览换授权 blob）
watch(
  () => props.edit,
  (c) => {
    if (!c) return
    text.value = c.text ?? ''
    mediaItems.value = []
    for (const m of c.media ?? []) {
      // 语音随记：audio 也回填（占格不可预览，发送重绑保留）
      mediaItems.value.push({
        preview: m.url,
        mediaId: m.id,
        kind: m.kind === 'video' ? 'video' : m.kind === 'audio' ? 'audio' : 'image'
      })
      if (m.kind === 'audio') continue
      loadMediaUrl(m.url)
        .then((u) => {
          const target = mediaItems.value.find((x) => x.mediaId === m.id)
          if (target) target.preview = u
        })
        .catch(() => {})
    }
  },
  { immediate: true }
)

// 本组件实例的内层 textarea（页面可能同时存在新建+编辑两个实例，
// 不能用全局 .c-area 选择器——会插错框）
const areaRef = ref<unknown>(null)
function innerTextarea(): HTMLTextAreaElement | null {
  // #ifdef H5
  const el = (areaRef.value as { $el?: HTMLElement } | null)?.$el ?? (areaRef.value as HTMLElement | null)
  return (el?.querySelector?.('textarea') as HTMLTextAreaElement | null) ?? null
  // #endif
  // #ifndef H5
  return null
  // #endif
}

// 表情插入到光标处，选完即收起
function onEmoji(emoji: string) {
  const ta = innerTextarea()
  if (ta) {
    const s = ta.selectionStart ?? text.value.length
    const e = ta.selectionEnd ?? s
    text.value = text.value.slice(0, s) + emoji + text.value.slice(e)
    emojiOpen.value = false
    nextTick(() => {
      ta.focus()
      const pos = s + emoji.length
      ta.setSelectionRange(pos, pos)
    })
    return
  }
  text.value += emoji
  emojiOpen.value = false
}

const MAX_IMAGES = 9

// H5 同步建 input 选图（挂 body 手势链路确定可开，与 publish 页同套路）
function pickImage() {
  const remaining = MAX_IMAGES - mediaItems.value.length
  if (remaining <= 0) {
    uni.showToast({ title: t('publish.maxImagesReached'), icon: 'none' })
    return
  }
  // #ifdef H5
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = 'image/*'
  input.multiple = true
  input.style.cssText = 'position:fixed;left:-9999px;top:0;width:1px;height:1px;opacity:0;'
  document.body.appendChild(input)
  input.onchange = () => {
    const files = Array.from(input.files ?? []).slice(0, remaining)
    input.remove()
    for (const f of files) {
      const preview = URL.createObjectURL(f)
      mediaItems.value.push({ preview, file: f, kind: f.type.startsWith('video/') ? 'video' : 'image' })
    }
  }
  input.addEventListener('cancel', () => input.remove())
  input.click()
  // #endif
  // #ifndef H5
  uni.chooseImage({
    count: remaining,
    success: (res) => {
      for (const p of res.tempFilePaths) mediaItems.value.push({ preview: p, kind: 'image' })
    }
  })
  // #endif
}

function removeMedia(preview: string) {
  mediaItems.value = mediaItems.value.filter((m) => m.preview !== preview)
  // #ifdef H5
  if (preview.startsWith('blob:')) URL.revokeObjectURL(preview)
  // #endif
}

async function send() {
  if (!canSend.value || busy.value) return
  busy.value = true
  try {
    // 补传未上传的本地图片（选完即传，此处一般只剩刚选的）
    for (const m of mediaItems.value) {
      if (!m.mediaId) {
        const asset = await uploadFile<{ id: string }>('/api/media', m.file ?? m.preview)
        m.mediaId = asset.id
      }
    }
    const mediaIds = mediaItems.value.map((m) => m.mediaId).filter((id): id is string => !!id)

    if (props.edit) {
      // 原地保存：媒体整体重绑（移除格子=解绑）；recipientIds/weather 不传，后端保持原值
      const updated = await updateContent(props.edit.id, { text: text.value, mediaIds })
      uni.showToast({ title: t('publish.saved'), icon: 'success' })
      emit('updated', updated)
      return
    }

    // 新建随记附上自动采集的天气快照（采集失败不带，与发布页同口径）
    const created = await createSay(
      text.value,
      [],
      mediaIds,
      weather.value
        ? { city: weather.value.city, condition: weather.value.condition, tempC: weather.value.tempC }
        : undefined
    )
    // 成功即清空，准备下一条
    text.value = ''
    for (const m of mediaItems.value) {
      // #ifdef H5
      if (m.preview.startsWith('blob:')) URL.revokeObjectURL(m.preview)
      // #endif
    }
    mediaItems.value = []
    emojiOpen.value = false
    uni.showToast({ title: t('publish.success'), icon: 'success' })
    emit('published', created)
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    busy.value = false
  }
}

defineExpose({
  focus() {
    innerTextarea()?.focus()
  }
})
</script>

<style scoped>
/* 轻编辑器：与 feed 卡片同底色的浅色框，弱化"表单感"强化"随手记" */
.composer {
  background: var(--hz-color-surface);
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-lg);
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
/* 日记头：左=日期+农历，右=天气快照（采集到才显示） */
.c-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
}
.c-head-left {
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
}
.c-head-date {
  font-size: var(--hz-font-size-sm);
  font-weight: 600;
  color: var(--hz-color-text);
  white-space: nowrap;
}
.c-head-lunar {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.c-head-weather {
  flex-shrink: 0;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  white-space: nowrap;
}
.c-area {
  width: 100%;
  min-height: 64px;
  font-size: 16px;
  line-height: 1.6;
  color: var(--hz-color-text);
}
.c-media {
  /* 与阅读侧九宫格同款：3 列限宽 300px（用户要求 3×3 布局） */
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 4px;
  max-width: 300px;
}
.c-cell {
  position: relative;
  aspect-ratio: 1;
  border-radius: var(--hz-radius-md);
  overflow: hidden;
}
.c-img {
  width: 100%;
  height: 100%;
}
.c-remove {
  position: absolute;
  right: 2px;
  top: 0;
  width: 20px;
  height: 20px;
  line-height: 20px;
  text-align: center;
  font-size: 14px;
  color: #fff;
  background: rgba(0, 0, 0, 0.55);
  border-radius: 0 0 0 8px;
  cursor: pointer;
}
/* 语音占位格（与发布页同款） */
.c-audio {
  width: 100%;
  height: 100%;
  background: var(--hz-color-bg);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
}
.c-audio-ico {
  font-size: 22px;
}
.c-audio-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.c-rec-sec {
  font-size: 14px;
  font-variant-numeric: tabular-nums;
  color: var(--hz-color-primary);
}
.c-bar {
  display: flex;
  align-items: center;
  gap: 6px;
}
.c-ico {
  width: 34px;
  height: 34px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--hz-radius-md);
  cursor: pointer;
  transition: background 0.12s ease;
}
.c-ico:hover {
  background: var(--hz-color-bg);
}
.c-ico.on {
  background: var(--hz-color-bg);
}
.c-ico-txt {
  font-size: 19px;
  line-height: 1;
}
.c-spacer {
  flex: 1;
}
.c-send {
  height: 34px;
  line-height: 34px;
  padding: 0 20px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: 999px;
}
.c-send[disabled] {
  opacity: 0.55;
}
/* 编辑态：主色调描边突出"正在编辑这条"；取消按钮弱化 */
.composer.editing {
  border-color: var(--hz-color-primary);
}
.c-cancel {
  height: 34px;
  line-height: 34px;
  padding: 0 16px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-bg);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
}
</style>
