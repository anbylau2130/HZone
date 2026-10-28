<template>
  <view class="page" :class="mode === 'journal' ? 'journal-mode' : 'say-mode'">
    <!-- 日记头：左侧日期+农历，右侧天气（点击弹选择器修改）。类型由入口 query 决定，页内不切换 -->
    <view v-if="mode === 'say'" class="diary-head">
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

    <!-- 随记：textarea → 表情面板 → 媒体独立区块 → 工具行（媒体与输入区视觉分离） -->
    <view v-if="mode === 'say'" class="panel">
      <textarea
        v-model="sayText"
        class="area say-area"
        :placeholder="$t('publish.sayPlaceholder')"
        :maxlength="-1"
        auto-height
      />
      <EmojiPicker v-if="emojiOpen" @select="insertEmoji" />
      <view v-if="mediaItems.length || uploading" class="media-block">
        <text class="media-title">{{ $t('publish.media') }}</text>
        <view class="media-grid">
          <view v-for="m in mediaItems" :key="m.localPath" class="cell">
            <image v-if="m.kind === 'image'" class="img" :src="m.localPath" mode="aspectFill" />
            <!-- 语音：音波占位格（上传后卡片内点「播放语音」收听） -->
            <view v-else-if="m.kind === 'audio'" class="audio-cell">
              <text class="audio-ico">🎤</text>
              <text class="audio-label">{{ $t('publish.voiceClip') }}</text>
            </view>
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
      <!-- 标签行：回车/确认添加，chip × 删除；上限 10、单条 ≤32 字符 -->
      <view class="tags-row">
        <text class="tags-label">#</text>
        <view v-for="(t, i) in tags" :key="t" class="tag-chip">
          <text class="tag-text">{{ t }}</text>
          <text class="tag-x" @click="removeTag(i)">×</text>
        </view>
        <!-- 非受控 uni-input + :key 重建：H5 受控有异步回写/滞后怪癖、手动清 DOM
             会被内部 buffer 回写（实测三轮）——提交后 key 变化销毁重建，天然空框 -->
        <input
          v-if="tags.length < 10"
          :key="tags.length"
          class="tag-input"
          :placeholder="tags.length ? '' : $t('publish.tagPlaceholder')"
          confirm-type="done"
          :maxlength="32"
          @input="onTagInput"
          @confirm="addTagEvent($event, true)"
          @blur="addTagEvent"
        />
      </view>
      <view class="toolbar">
        <!-- 表情包开关（面板在输入区下方，连点即连插） -->
        <view class="rec-btn" :class="{ on: emojiOpen }" @click="emojiOpen = !emojiOpen">
          <text class="rec-ico">😊</text>
        </view>
        <!-- 语音随记：点击开始录音（≤60s 自动停），再点提前停；上传后随记附语音 -->
        <view class="rec-btn" :class="{ on: voice.recording.value }" @click="voice.toggle(mediaItems.length === 0)">
          <text v-if="voice.recording.value" class="rec-label">{{ voice.seconds.value }}s</text>
          <text v-else class="rec-ico">🎤</text>
        </view>
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
        <button class="btn-inline" :disabled="busy" @click="submit">
          {{ editingId ? $t('common.save') : $t('publish.submit') }}
        </button>
      </view>
      <text class="hint">{{ $t('publish.mediaRule') }}</text>
    </view>

    <!-- 日志：标题独立（无圆角+分隔线）。桌面双栏（编辑器+预览并排常显）；
         移动端 cherry 互斥单显（纯编辑 ↔ 满宽预览，标题后按钮切换） -->
    <template v-else>
      <view class="panel journal-panel">
        <view class="j-head-row">
          <input v-model="title" class="j-title" :placeholder="$t('publish.titlePlaceholder')" />
          <!-- #ifdef H5 -->
          <!-- 预览态显示「编辑」、编辑态显示「预览」（按钮指向可切入的模式）；仅移动端显示（桌面 CSS 隐藏） -->
          <view class="preview-toggle" :class="{ on: previewOn }" @click="previewOn = !previewOn"
             :title="previewOn ? $t('publish.previewToggle') : $t('common.edit')">
            <!-- 预览态=睁眼，编辑态=闭眼（图标反映当前状态） -->
            <svg v-if="previewOn" class="eye-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
              <circle cx="12" cy="12" r="3" />
            </svg>
            <svg v-else class="eye-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24" />
              <line x1="1" y1="1" x2="23" y2="23" />
            </svg>
          </view>
          <!-- #endif -->
        </view>
          <!-- 日记头移入卡片：三行两端对齐——日期+星期 / 农历+节气 / 地址+天气；点第三行弹选择器 -->
          <view class="diary-head in-panel">
            <view class="dh-row">
              <text class="dh-date">{{ datePart }}</text>
              <text class="dh-week">{{ weekPart }}</text>
            </view>
            <view v-if="lunarParts" class="dh-row">
              <text class="dh-lunar">{{ lunarParts.date }}</text>
              <text v-if="lunarParts.term" class="dh-term">{{ lunarParts.term }}</text>
            </view>
            <view class="dh-row dh-weather" @click="openWeatherPicker">
              <text v-if="weather?.city" class="dh-city">{{ weather.city }}</text>
              <text v-if="weather" class="dh-weather-text">
                {{ CONDITION_ICONS[weather.condition] }} {{ formatTemp(weather.tempC) }}
              </text>
              <text v-else class="dh-weather-add">{{ $t('weather.add') }}</text>
            </view>
          </view>
        <!-- #ifdef H5 -->
        <!-- 桌面不传 exclusive → cherry edit&preview 双栏；移动端 exclusive 互斥单显 -->
        <CherryEditor
          v-model="markdown"
          :placeholder="$t('publish.bodyPlaceholder')"
          :exclusive="isMobile"
          :initial-preview="previewOn"
          :preview="previewOn"
        />
        <!-- #endif -->
        <!-- #ifndef H5 -->
        <textarea
          v-model="markdown"
          class="area tall"
          :placeholder="$t('publish.bodyPlaceholder')"
          :maxlength="-1"
          auto-height
        />
        <!-- #endif -->
      </view>
      <view class="recipients">
        <text class="label link" @click="pickerVisible = true">{{ $t('publish.recipients') }}</text>
        <view class="chips">
          <text v-for="r in pickedFriends" :key="r.id" class="chip">{{ r.nickname }}</text>
        </view>
        <!-- 选择好友按钮移除：点「@ 谁」label 打开选好友（用户要求） -->
      </view>
      <!-- 标签行（与随记面板同款）：回车/确认添加，chip × 删除 -->
      <view class="tags-row">
        <text class="tags-label">#</text>
        <view v-for="(t, i) in tags" :key="t" class="tag-chip">
          <text class="tag-text">{{ t }}</text>
          <text class="tag-x" @click="removeTag(i)">×</text>
        </view>
        <!-- 非受控 uni-input + :key 重建：H5 受控有异步回写/滞后怪癖、手动清 DOM
             会被内部 buffer 回写（实测三轮）——提交后 key 变化销毁重建，天然空框 -->
        <input
          v-if="tags.length < 10"
          :key="tags.length"
          class="tag-input"
          :placeholder="tags.length ? '' : $t('publish.tagPlaceholder')"
          confirm-type="done"
          :maxlength="32"
          @input="onTagInput"
          @confirm="addTagEvent($event, true)"
          @blur="addTagEvent"
        />
      </view>
      <button class="btn" :disabled="busy" @click="submit">
        {{ editingId ? $t('common.save') : $t('publish.submit') }}
      </button>
    </template>

    <FriendPicker v-model:visible="pickerVisible" :selected="pickedFriends" @confirm="onFriendsConfirmed" />

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
import FriendPicker from '../../components/FriendPicker.vue'
import EmojiPicker from '../../components/EmojiPicker.vue'
// #ifdef H5
import CherryEditor from '../../components/CherryEditor.vue'
// #endif
import {
  createSay,
  createJournal,
  getContent,
  getMyStats,
  updateContent,
  type FriendInfo,
  type LunarInfo,
  type MeStats,
  type WeatherSnapshotInput
} from '../../api/endpoints'
import { uploadFile, getToken } from '../../api/client'
import { useVoiceRecorder } from '../../composables/useVoiceRecorder'
import { loadMediaUrl } from '../../api/media'
import { onHide, onLoad, onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import { CONDITION_ICONS, WEATHER_CONDITIONS, getWeather, type WeatherSnapshot } from '../../utils/weather'
import { formatLunar } from '../../utils/lunar'

onShow(() => {
  // 日志编辑模式标题「日志编辑」；随记沿用 发布内容/编辑内容
  setNavTitle(mode.value === 'journal' ? 'hub.journalEdit' : (editingId.value ? 'hub.edit' : 'hub.publish'))
  // 编辑模式：日记头日期/农历/天气由 loadForEdit 回填原内容快照，不拉今日统计
  if (editingId.value) return
  refreshDiaryHead()
  // 自动天气：成功一次模块级缓存即止；失败不缓存——每次 onShow 重试，
  // 直到拿到或用户手动改过（境外 API 偶发被断时不再一次失败永久放弃）
  if (!weatherAutoStopped) {
    getWeather()
      .then((w) => {
        if (!weather.value) weather.value = w
      })
      .catch(() => {})
  }
})

// 用户手动清除后不再自动回填（手动改过视为定稿）
let weatherAutoStopped = false

// 支持 ?kind=say / ?kind=journal 预选发布类型（随记/日志分页入口跳入）；
// ?id= 进入编辑模式——编辑复用创建页全套编辑器（story：编辑与创建同一页面）
onLoad((query) => {
  // 与 home 同款守卫：无 token 深链/残留标签页直接进发布页会一路 401 卡死
  if (!getToken()) {
    uni.reLaunch({ url: '/pages/login/login' })
    return
  }
  const q = (query ?? {}) as Record<string, string>
  if (q.kind === 'journal') {
    mode.value = 'journal'
  } else if (q.kind === 'say') {
    mode.value = 'say'
  }
  if (q.id) {
    editingId.value = q.id
    loadForEdit()
  } else {
    restoreDraft()
  }
})

const { t } = useI18n()

interface PickedMedia {
  localPath: string
  /** H5 直传的原始 File（不经 blob: URL 中转，上传更可靠）；App 端为空走路径上传 */
  file?: Blob
  mediaId?: string
  kind: 'image' | 'video' | 'audio'
}

const mode = ref<'say' | 'journal'>('say')
const editingId = ref('')
const sayText = ref('')
const title = ref('')
const markdown = ref('')
const emojiOpen = ref(false)

// 表情插入到光标处（H5：取 uni-textarea 内层真实 textarea 的选区；App 端追加到末尾）。
// 选中即收起面板（连发场景可再点开）
function insertEmoji(emoji: string) {
  // #ifdef H5
  const ta = document.querySelector('.say-area textarea') as HTMLTextAreaElement | null
  if (ta) {
    const s = ta.selectionStart ?? sayText.value.length
    const e = ta.selectionEnd ?? s
    sayText.value = sayText.value.slice(0, s) + emoji + sayText.value.slice(e)
    emojiOpen.value = false
    nextTick(() => {
      ta.focus()
      const pos = s + emoji.length
      ta.setSelectionRange(pos, pos)
    })
    return
  }
  // #endif
  sayText.value += emoji
  emojiOpen.value = false
}

const mediaItems = ref<PickedMedia[]>([])
const pickedFriends = ref<FriendInfo[]>([])
const pickerVisible = ref(false)
const uploading = ref(false)
const submitting = ref(false)

const busy = computed(() => uploading.value || submitting.value)

// ---------- 编辑模式：?id= 加载原内容回填（创建页即编辑页） ----------
const editLunar = ref<LunarInfo | null>(null)

async function loadForEdit() {
  try {
    const c = await getContent(editingId.value)
    mode.value = c.kind === 'journal' ? 'journal' : 'say'
    // 日记头还原原发布时刻，而非编辑时的今天
    now.value = new Date(c.createdAt)
    editLunar.value = c.lunar ?? null
    title.value = c.title ?? ''
    markdown.value = c.markdownBody ?? ''
    sayText.value = c.text ?? ''
    if (c.weather?.condition) {
      weather.value = {
        city: c.weather.city ?? '',
        condition: c.weather.condition as WeatherSnapshot['condition'],
        tempC: c.weather.tempC ?? 0
      }
    }
    pickedFriends.value = c.recipients.map((r) => ({ id: r.userId, nickname: r.nickname, email: '' }))
    tags.value = c.tags ?? []
    for (const m of c.media ?? []) {
      // 语音随记：audio 也回填（上传时绑定，MediaGrid 内点播）
      mediaItems.value.push({
        localPath: m.url,
        mediaId: m.id,
        kind: m.kind === 'video' ? 'video' : m.kind === 'audio' ? 'audio' : 'image'
      })
      // /media 预览带不上鉴权头——换授权 blob（失败保留原 src，可提交只是裂图）
      loadMediaUrl(m.url)
        .then((u) => {
          const target = mediaItems.value.find((x) => x.mediaId === m.id)
          if (target) target.localPath = u
        })
        .catch(() => {})
    }
    // 只恢复比服务端新的草稿（多编辑面过期草稿防护）
    restoreDraft(c.updatedAt)
  } catch {
    // 请求层已 toast；停留在空编辑页，用户可返回
  }
}

// ---------- 草稿自动保存（会话过期被踢回登录/误退出不丢稿） ----------
// 编辑模式草稿=中断现场（比服务端内容新，恢复时覆盖）；创建模式只填空字段。
// 提交成功即清；drawio 的 base64 大稿（>1MB）不入草稿，护 localStorage 限额。
const draftKey = computed(() =>
  editingId.value
    ? `hz-draft-journal-e-${editingId.value}`
    : mode.value === 'journal'
      ? 'hz-draft-journal'
      : 'hz-draft-say'
)

let draftTimer: ReturnType<typeof setTimeout> | null = null
function saveDraft() {
  if (draftTimer) clearTimeout(draftTimer)
  draftTimer = setTimeout(() => {
    try {
      const raw = JSON.stringify(
        mode.value === 'journal'
          ? { title: title.value, markdown: markdown.value, at: Date.now() }
          : { text: sayText.value, at: Date.now() }
      )
      if (raw.length > 1_000_000) return
      localStorage.setItem(draftKey.value, raw)
    } catch {
      // 满额等异常静默：草稿是尽力而为
    }
  }, 800)
}

function restoreDraft(serverUpdatedAt?: string) {
  try {
    const raw = localStorage.getItem(draftKey.value)
    if (!raw) return
    const d = JSON.parse(raw) as { title?: string; markdown?: string; text?: string; at?: number }
    if (editingId.value) {
      // 多编辑面时代（发布页/卡片内联）本地草稿不一定比服务端新：
      // 仅当草稿时间戳晚于服务端更新时间才恢复，否则丢弃，
      // 避免旧草稿覆盖别处刚保存的内容（多编辑面过期草稿问题）
      const serverMs = serverUpdatedAt ? Date.parse(serverUpdatedAt) : 0
      if (!d.at || serverMs >= d.at) {
        localStorage.removeItem(draftKey.value)
        return
      }
      if (typeof d.title === 'string') title.value = d.title
      if (typeof d.markdown === 'string') markdown.value = d.markdown
      if (typeof d.text === 'string') sayText.value = d.text
    } else {
      if (d.title && !title.value) title.value = d.title
      if (d.markdown && !markdown.value) markdown.value = d.markdown
      if (d.text && !sayText.value) sayText.value = d.text
    }
  } catch {
    // 损坏草稿忽略
  }
}

function clearDraft() {
  localStorage.removeItem(draftKey.value)
}

watch([title, markdown, sayText], () => {
  // 回填/恢复也会触发，但 debounce 只留最后一次值，无需区分来源
  if (title.value || markdown.value || sayText.value) saveDraft()
})

// ---------- 日志预览/编辑切换（仅 H5） ----------
// 桌面（≥768px）：双栏常显（previewOn 恒 true，切换按钮 CSS 隐藏）；
// 移动（<768px）：默认纯编辑，用户点标题后按钮在「编辑 ↔ 预览」间切换并持久化。
// 传给 CherryEditor 的 exclusive（仅移动端）/initial-preview/preview 三件套驱动 switchModel 切换
// #ifdef H5
const PREVIEW_KEY = 'hz-journal-preview'
// 断点 767px 与样式表 @media (max-width: 767px) 保持一致
const isMobile = window.matchMedia('(max-width: 767px)').matches
// 桌面恒双栏；移动默认关，用户开关后持久化到 localStorage
const previewOn = ref(isMobile ? localStorage.getItem(PREVIEW_KEY) === 'on' : true)
watch(previewOn, (v) => {
  localStorage.setItem(PREVIEW_KEY, v ? 'on' : 'off')
})
// #endif
// #ifndef H5
const previewOn = ref(true)
// #endif

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

// 日记头三行拆分：日期/星期、农历/节气各成一对（in-panel 两端对齐用）；dateLine/lunarLine 供随记顶部单行沿用
const datePart = computed(() => {
  const d = now.value
  return t('home.monthDay', { m: d.getMonth() + 1, d: d.getDate() })
})
const weekPart = computed(() => t(`home.weekday${now.value.getDay()}`))
const dateLine = computed(() => `${datePart.value} ${weekPart.value}`)

const lunarLine = computed(() =>
  editingId.value ? formatLunar(editLunar.value) : formatLunar(stats.value?.today)
)
// 农历第二行两段：干支年+月日 | 节气（无农历整行隐藏，无节气省略右端）
const lunarParts = computed(() => {
  const l = (editingId.value ? editLunar.value : stats.value?.today) ?? null
  if (!l?.ganzhiYear) return null
  return { date: `${l.ganzhiYear} ${l.monthDay}`, term: l.term || '' }
})

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
  weatherAutoStopped = true
}

function formatTemp(tempC: number): string {
  return `${Math.round(tempC)}°C`
}

function toSnapshot(): WeatherSnapshotInput | undefined {
  return weather.value
    ? { city: weather.value.city, condition: weather.value.condition, tempC: weather.value.tempC }
    : undefined
}

function onFriendsConfirmed(friends: FriendInfo[]) {
  pickedFriends.value = friends
}

// ---------- 标签（跨时间主题检索）：回车/失焦添加，chip × 删除，上限 10 ----------
const tags = ref<string[]>([])
// uni-input H5 深坑（实测两轮）：v-model 快速输入+回车滞后丢字；受控清空后
// 内部 buffer 异步回写旧文本——彻底绕开：keydown.enter 原生处理 + 直接读
// DOM 值（keydown 时刻无竞态窗口），不用 v-model/confirm
let lastTagInput = ''

function onTagInput(e: { detail: { value: string } }) {
  lastTagInput = e.detail.value
}

function addTagEvent(e?: { detail?: { value: string } }, fromConfirm = false) {
  const raw = (e?.detail?.value || lastTagInput).trim()
  lastTagInput = ''
  if (!raw) return
  commitTag(raw)
  if (fromConfirm) {
    // :key 重建后焦点随旧组件销毁——回焦新框，连续输入不断流
    nextTick(() => {
      document.querySelector<HTMLInputElement>('.tag-input input')?.focus()
    })
  }
}

function commitTag(raw: string) {
  if (tags.value.length >= 10) {
    uni.showToast({ title: t('publish.tagLimit'), icon: 'none' })
    return
  }
  if (!tags.value.some((x) => x.toLowerCase() === raw.toLowerCase())) {
    tags.value = [...tags.value, raw]
  }
}

function removeTag(i: number) {
  tags.value = tags.value.filter((_, idx) => idx !== i)
}

const MAX_IMAGES = 9

// ---------- 语音随记：共享 composable（与随记页 SayComposer 同款实现） ----------
const voice = useVoiceRecorder((r) => {
  mediaItems.value.push({ localPath: r.previewUrl, file: r.file, kind: 'audio' })
})

onUnmounted(() => {
  if (voice.recording.value) voice.stop()
})

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
      files.map((f) => ({ preview: URL.createObjectURL(f), file: f })),
      'image'
    )
  })
  // #endif
  // #ifndef H5
  uni.chooseImage({
    count: remaining,
    success: (res) => {
      uploadAll(res.tempFilePaths.map((p) => ({ preview: p })), 'image')
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
    if (files.length) uploadAll([{ preview: URL.createObjectURL(files[0]), file: files[0] }], 'video')
  })
  // #endif
  // #ifndef H5
  uni.chooseVideo({
    success: (res) => {
      uploadAll([{ preview: res.tempFilePath }], 'video')
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

async function uploadAll(items: { preview: string; file?: Blob }[], kind: 'image' | 'video') {
  uploading.value = true
  try {
    for (const it of items) {
      const asset = await uploadFile<{ id: string }>('/api/media', it.file ?? it.preview)
      mediaItems.value.push({ localPath: it.preview, file: it.file, mediaId: asset.id, kind })
    }
  } catch {
    // 上传失败：请求层已 toast 具体错误，这里兜底提示保证用户可感知
    uni.showToast({ title: t('publish.uploadFailed'), icon: 'none' })
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
  mediaItems.value = [{ localPath: URL.createObjectURL(blob), file: blob, kind: 'video' }]
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

// ---------- 日志附件上传：cherry 工具栏/自定义菜单接管（见 components/CherryEditor.vue） ----------

/** 补传本地媒体（如刚录制的视频尚无 mediaId），false = 失败已提示可中止提交。 */
async function uploadPendingMedia(): Promise<boolean> {
  uploading.value = true
  try {
    for (const m of mediaItems.value) {
      if (!m.mediaId) {
        const asset = await uploadFile<{ id: string }>('/api/media', m.file ?? m.localPath)
        m.mediaId = asset.id
      }
    }
    return true
  } catch {
    return false // 上传失败提示已由请求层统一 toast
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
      if (!(await uploadPendingMedia())) return
      const mediaIds = mediaItems.value
        .map((m) => m.mediaId)
        .filter((id): id is string => Boolean(id))
      if (editingId.value) {
        // 媒体整体重绑：移除格子 = 解绑，保留项带原 mediaId 原样提交
        await updateContent(editingId.value, {
          text: sayText.value,
          mediaIds,
          recipientIds,
          weather: toSnapshot(),
          tags: tags.value
        })
      } else {
        await createSay(sayText.value, recipientIds, mediaIds, toSnapshot(), tags.value)
      }
    } else {
      if (!title.value.trim() || !markdown.value.trim()) {
        uni.showToast({ title: t('publish.journalEmpty'), icon: 'none' })
        return
      }
      if (editingId.value) {
        await updateContent(editingId.value, {
          title: title.value,
          markdownBody: markdown.value,
          recipientIds,
          weather: toSnapshot(),
          tags: tags.value
        })
      } else {
        await createJournal(title.value, markdown.value, recipientIds, toSnapshot(), tags.value)
      }
    }
    uni.showToast({
      title: editingId.value ? t('publish.saved') : t('publish.success'),
      icon: 'success'
    })
    clearDraft() // 已落库，草稿使命完成
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
  padding: 0; /* 移动端零内边距：卡片全宽贴边 */
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
/* in-panel 三行版式：每行左右内容分列两端（space-between），左右各留 15px（用户指定） */
.diary-head.in-panel {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 2px;
  padding: 0 15px 8px;
  border-bottom: 1px solid var(--hz-color-border);
  margin-bottom: 2px;
}
.dh-row {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
}
.diary-head.in-panel .dh-date,
.diary-head.in-panel .dh-week,
.diary-head.in-panel .dh-lunar,
.diary-head.in-panel .dh-term,
.diary-head.in-panel .dh-city,
.diary-head.in-panel .dh-weather-text {
  font-size: 13px;
  color: var(--hz-color-text-secondary);
}
.diary-head.in-panel .dh-date {
  font-weight: 500;
}
/* 天气端恒靠右：地址缺失/无天气（添加天气）时右端不落空 */
.diary-head.in-panel .dh-weather-text,
.diary-head.in-panel .dh-weather-add {
  margin-left: auto;
}
.diary-head.in-panel .dh-weather-add {
  font-size: 13px;
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
.j-head-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.j-title {
  flex: 1;
  height: 48px;
  font-size: 18px;
  font-weight: 600;
  border: none;
  border-bottom: 1px solid var(--hz-color-border);
  border-radius: 0;
  background: transparent;
  color: var(--hz-color-text);
}
/* 预览开关小按钮（仅 H5）：低调 pill，on 态 primary 描边+浅底 */
/* 预览切换：图标式超链接（无边框无底色，轻量不抢戏） */
.preview-toggle {
  flex-shrink: 0;
  padding: 4px;
  border: none;
  background: transparent;
  color: var(--hz-color-interactive);
  cursor: pointer;
  user-select: none;
}
.preview-toggle.on {
  color: var(--hz-color-interactive);
}
.pt-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.preview-toggle.on .pt-text {
  color: var(--hz-color-primary);
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
  /* 与阅读侧九宫格同款：3 列限宽 300px（用户要求 3×3 布局） */
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 4px;
  max-width: 300px;
}
.cell {
  position: relative;
  aspect-ratio: 1;
  border-radius: var(--hz-radius-md);
  overflow: hidden;
}
/* 语音占位格：麦图标 + 文案居中 */
.audio-cell {
  width: 100%;
  height: 100%;
  background: var(--hz-color-bg);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
}
.audio-ico {
  font-size: 24px;
}
.audio-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
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
/* 标签行：# 前缀 + chips + 内联输入（随记面板与笔记模板共用） */
.tags-row {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.tags-label {
  font-size: var(--hz-font-size-sm);
  font-weight: 700;
  color: var(--hz-color-interactive);
}
.tag-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--hz-color-bg);
  border: 1px solid var(--hz-color-border);
}
.tag-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
}
.tag-x {
  font-size: 13px;
  color: var(--hz-color-text-secondary);
  padding: 0 2px;
  cursor: pointer;
}
.tag-input {
  flex: 1;
  min-width: 90px;
  height: 28px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
.btn {
  height: 44px;
  line-height: 44px;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-base);
}
/* 移动端编辑器贴边：容器去圆角去内边距，cherry 同步去圆角边框（用户要求无嵌入感）；标题行单独留 12px 横向内边距，正文区满宽贴屏幕边 */
@media (max-width: 767px) {
  .journal-panel {
    border-radius: 0;
    padding: 0;
    /* 编辑器撑满首屏：面板吃掉剩余高度，保存按钮露出在下方 */
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
  }
  .j-head-row {
    padding: 0 15px;
    flex-shrink: 0;
  }
  .journal-panel .cherry-host {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    border-radius: 0;
  }
  .journal-panel :deep(.cherry) {
    border: none;
    border-radius: 0;
    /* 覆盖 cherry 内联 60vh，改随面板弹性填充 */
    height: auto !important;
    flex: 1;
    display: flex;
    flex-direction: column;
  }
  /* 编辑态：编辑区撑满；预览态：预览区撑满（排除 cherry 的 --hidden 态，
     否则预览模式下隐藏的编辑面板仍 flex 占位、预览内容沉底） */
  .journal-panel :not(.preview-only) > :deep(.cherry-editor:not(.cherry-editor--hidden)) {
    flex: 1;
    display: flex;
    flex-direction: column;
  }
  .journal-panel :not(.preview-only) > :deep(.cm-editor:not(.cm-empty)) {
    flex: 1;
  }
  .journal-panel.preview-only :deep(.cherry-previewer) {
    flex: 1;
  }
  /* 眼睛图标 */
  .preview-toggle .eye-icon {
    width: 20px;
    height: 20px;
  }
  /* 保存按钮贴底常显，不被编辑器挤出首屏；两侧 15px 与标题行/日记头/收件人行同拍 */
  .page .btn {
    position: sticky;
    bottom: 8px;
    z-index: 5;
    margin: 0 15px;
  }

  /* 「@ 谁」可点开选好友 */
  .recipients .label.link {
    color: var(--hz-color-interactive);
    cursor: pointer;
  }
  /* 收件人行贴边时代 15px 横向内边距（与日记头一致） */
  .recipients {
    padding: 12px 15px;
  }
}

@media (min-width: 768px) {
  /* 预览切换按钮桌面隐藏：桌面恒双栏，无需切换 */
  .preview-toggle {
    display: none;
  }
  /* 日记头桌面限宽：48rem+ 内容宽下三行两端对齐会被拉出巨大间隙；360px = 移动端
     内容宽（390-30），两端对齐的疏密与移动端完全一致，保持日记本页眉观感 */
  .diary-head.in-panel {
    max-width: 360px;
  }
  /* journal 模式占满左导航与 dock 之间的可用宽度，仅留 24px padding */
  .page {
    padding: 32px 24px;
  }
  /* say 短内容：单栏限宽居中（覆盖视口 padding 公式，否则 672px 盒子被 185px×2 的 padding 挤扁） */
  .page.say-mode {
    max-width: calc(42rem + 48px);
    margin: 0 auto;
    padding: 32px 24px;
  }
    .journal-panel .area.tall {
    height: 40vh !important; /* 覆盖 uni-app auto-height 的行内样式 */
    min-height: 0;
    font-size: 15px;
    line-height: 1.8;
    font-family: ui-monospace, SFMono-Regular, Consolas, 'Courier New', monospace;
  }
  .btn {
    align-self: flex-end;
    width: 160px;
    margin-left: auto;
  }
}
</style>
