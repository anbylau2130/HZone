<template>
  <view class="page">
    <!-- 今日卡（hero）：唯一视觉重心，蜜黄-透明渐变底，仅此卡有首屏淡入 -->
    <view class="today-card">
      <text class="today-date">{{ gregorian }}</text>
      <text v-if="lunarLine" class="today-lunar">{{ lunarLine }}</text>
      <text v-if="weatherLine" class="today-weather">{{ weatherLine }}</text>
    </view>

    <!-- 统计数字行：四张卡（随记/日志/好友/连续记录天数） -->
    <view class="stats-row">
      <view class="stat-card">
        <text class="stat-value">{{ stats?.totals.says ?? 0 }}</text>
        <text class="stat-label">{{ $t('home.statsSays') }}</text>
      </view>
      <view class="stat-card">
        <text class="stat-value">{{ stats?.totals.journals ?? 0 }}</text>
        <text class="stat-label">{{ $t('home.statsJournals') }}</text>
      </view>
      <view class="stat-card">
        <text class="stat-value">{{ stats?.totals.friends ?? 0 }}</text>
        <text class="stat-label">{{ $t('home.statsFriends') }}</text>
      </view>
      <view class="stat-card" @click="onStreakCard">
        <text class="stat-value" :class="{ start: !hasStreak }">{{ streakDisplay }}</text>
        <text class="stat-label" :class="{ accent: !hasStreak }">{{ streakLabel }}</text>
      </view>
    </view>

    <!-- 里程碑（06，纯前端推导）：totals 跨过 10/50/100/500/1000 最高档，未达 10 隐藏 -->
    <view v-if="milestones.length" class="milestones-row">
      <text v-for="m in milestones" :key="m.kind" class="milestone-badge">{{ m.label }}</text>
    </view>

    <!-- 互动摘要（06）：近 7 天评论+被 @，0 也显示（互动为零也是信息） -->
    <view v-if="interactions" class="interaction-line">
      <text class="interaction-text">{{ interactionLine }}</text>
    </view>

    <!-- 热力格（05）：近 90 天发布密度；stats 已加载即显示（全 0 也是"从今天开始"的留白） -->
    <view v-if="stats" class="heatmap-section">
      <text class="section-title">{{ $t('home.heatmapTitle') }}</text>
      <Heatmap :cells="stats.heatmap" />
    </view>

    <!-- 月度回顾（06）：中国区本月一句式小卡（篇数/城市/最常天气） -->
    <view v-if="stats?.monthly" class="monthly-card">
      <text class="section-title">{{ $t('home.monthlyTitle') }}</text>
      <text class="monthly-line">{{ monthlyLine }}</text>
    </view>

    <!-- 足迹（05）：天气快照城市时间线，同城连续去重；无城市数据隐藏 -->
    <view v-if="stats?.footprints?.length" class="footprints-section">
      <text class="section-title">{{ $t('home.footprintsTitle') }}</text>
      <Footprints :items="stats.footprints" />
    </view>

    <!-- 那年今天（06）：往年同月同日的内容，无历史隐藏整块；不跳转（②C，详情页已移除） -->
    <view v-if="otd.length" class="otd-section">
      <text class="section-title">{{ $t('home.otdTitle') }}</text>
      <view v-for="it in otd" :key="it.id" class="otd-item">
        <text class="otd-year">{{ it.year }}</text>
        <text class="otd-icon">{{ it.kind === 'journal' ? '📖' : '✏️' }}</text>
        <text class="otd-title">{{ it.title ?? it.summary }}</text>
      </view>
    </view>

    <!-- 我的时间线：带全部/随记/笔记筛选 + 页内检索（搜索态输入框接管工具行） -->
    <view class="timeline-section">
      <text class="section-title">{{ $t('home.timeline') }}</text>
      <view v-if="searchOpen" class="search-inline">
        <svg class="si-ico" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round">
          <circle cx="9" cy="9" r="5.5" />
          <path d="M13.2 13.2L17.5 17.5" />
        </svg>
        <input
          v-model="keyword"
          class="si-input"
          :placeholder="$t('search.placeholder')"
          confirm-type="search"
          @confirm="refreshNow"
        />
        <text v-if="keyword" class="si-clear" @click="keyword = ''">×</text>
        <text class="si-cancel" @click="closeSearch">{{ $t('common.cancel') }}</text>
      </view>
      <view v-else class="tool-row">
        <view class="filter-wrap">
          <SegmentedFilter :model-value="kind" :options="kinds" @update:model-value="setKind" />
        </view>
        <view v-if="items.length > 0 || keyword" class="search-cell" :title="$t('search.button')" @click="toggleSearch">
          <svg class="si-ico" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round">
            <circle cx="9" cy="9" r="5.5" />
            <path d="M13.2 13.2L17.5 17.5" />
          </svg>
        </view>
      </view>

      <view v-if="pagination.loading.value && !items.length" class="hint">
        <text class="hint-text">{{ $t('common.loading') }}</text>
      </view>
      <view v-else-if="!items.length" class="hint">
        <text class="hint-text">{{ $t('common.empty') }}</text>
      </view>

      <ContentCard v-for="c in items" :key="c.id" :content="c" @updated="onCardUpdated" @deleted="onCardDeleted" />

      <!-- 滚动近底部自动加载（useInfiniteScroll），结束后只留提示 -->
      <view v-if="items.length && finished" class="more">
        <text class="hint-text">{{ $t('common.noMore') }}</text>
      </view>
    </view>

    <AppSidebar current="index" />
    <ToolDock />
    <MiniPlayer />
    <AppTabBar current="index" />
  </view>
</template>

<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { onShow, onHide } from '@dcloudio/uni-app'
import { useAuthStore } from '../../stores/auth'
import { useInboxStore } from '../../stores/inbox'
import {
  getSetupStatus,
  getMyStats,
  getOnThisDay,
  getInteractionSummary,
  listMyContents,
  searchContents,
  type ContentItem,
  type KindFilter,
  type MeStats,
  type OnThisDayItem,
  type InteractionSummary
} from '../../api/endpoints'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import ToolDock from '../../components/ToolDock.vue'
import MiniPlayer from '../../components/MiniPlayer.vue'
import ContentCard from '../../components/ContentCard.vue'
import SegmentedFilter from '../../components/SegmentedFilter.vue'
import Heatmap from '../../components/Heatmap.vue'
import Footprints from '../../components/Footprints.vue'
import { usePagination } from '../../composables/usePagination'
import { useInfiniteScroll } from '../../composables/useInfiniteScroll'
import { getWeather, CONDITION_ICONS, type WeatherSnapshot } from '../../utils/weather'
import { formatLunar } from '../../utils/lunar'
import { i18n } from '../../locale'

const auth = useAuthStore()
const inbox = useInboxStore()
const stats = ref<MeStats | null>(null)
const weather = ref<WeatherSnapshot | null>(null)
const otd = ref<OnThisDayItem[]>([])
const interactions = ref<InteractionSummary | null>(null)

// 公历日期「10月20日 星期二」：本地（浏览器）日期即可，农历才由后端推导。
const gregorian = computed(() => {
  const now = new Date()
  const date = i18n.global.t('home.monthDay', { m: now.getMonth() + 1, d: now.getDate() })
  return `${date} ${i18n.global.t(`home.weekday${now.getDay()}`)}`
})
const lunarLine = computed(() => formatLunar(stats.value?.today))
const weatherLine = computed(() => {
  if (!weather.value) return null
  const w = weather.value
  const parts = [w.city, i18n.global.t(`weather.conditions.${w.condition}`)].filter(Boolean)
  parts.unshift(CONDITION_ICONS[w.condition])
  parts.push(`${Math.round(w.tempC)}°`)
  return parts.join(' · ')
})
const hasStreak = computed(() => (stats.value?.streak ?? 0) > 0)
const streakDisplay = computed(() => (hasStreak.value ? String(stats.value!.streak) : ''))
const streakLabel = computed(() =>
  hasStreak.value ? i18n.global.t('home.statsStreak') : i18n.global.t('home.startToday')
)

// 里程碑（06，纯前端推导）：随记/日志各自取已达成的最高档；两者都未达 10 时隐藏。
const MILESTONE_THRESHOLDS = [10, 50, 100, 500, 1000]
const milestones = computed(() => {
  const totals = stats.value?.totals
  if (!totals) return []
  const rows: { kind: string; label: string }[] = []
  const kinds = [
    { key: 'says', label: i18n.global.t('home.statsSays') },
    { key: 'journals', label: i18n.global.t('home.statsJournals') }
  ]
  for (const k of kinds) {
    const count = totals[k.key as 'says' | 'journals']
    const reached = MILESTONE_THRESHOLDS.filter((th) => count >= th).pop()
    if (reached) {
      rows.push({
        kind: k.key,
        label: i18n.global.t('home.milestoneBadge', { kind: k.label, n: reached })
      })
    }
  }
  return rows
})

// 互动摘要行：数值 0 也显示——互动为零也是信息。
const interactionLine = computed(() =>
  interactions.value
    ? i18n.global.t('home.interactionsLine', {
        n: interactions.value.comments,
        m: interactions.value.mentions
      })
    : ''
)

// 月度回顾一句式：本月 N 篇 · M 城（+ 常见天气现象，可省）。
const monthlyLine = computed(() => {
  const m = stats.value?.monthly
  if (!m) return ''
  const base = i18n.global.t('home.monthlyLine', { n: m.says + m.journals, c: m.cities })
  const condition = m.topCondition
    ? i18n.global.te(`weather.conditions.${m.topCondition}`)
      ? i18n.global.t(`weather.conditions.${m.topCondition}`)
      : m.topCondition
    : null
  return condition
    ? `${base}${i18n.global.t('home.monthlyWeather', { w: condition })}`
    : base
})

const kinds: { value: KindFilter; label: string }[] = [
  { value: '', label: 'timeline.filterAll' },
  { value: 'say', label: 'timeline.kindSay' },
  { value: 'journal', label: 'timeline.kindJournal' }
]
const kind = ref<KindFilter>('')

// ---------- 时间线页内检索：关键词走 /search（与类型筛选叠加），空=全部 ----------
const searchOpen = ref(false)
const keyword = ref('')

const pagination = usePagination<ContentItem>((offset, limit) => {
  const kw = keyword.value.trim()
  return kw ? searchContents(kw, kind.value, offset, limit) : listMyContents(kind.value, offset, limit)
})
const { items, finished, loadMore } = pagination

function toggleSearch() {
  searchOpen.value = true
  nextTick(() => {
    document.querySelector('.si-input input')?.focus?.()
  })
}

function closeSearch() {
  searchOpen.value = false
  if (keyword.value) {
    keyword.value = ''
    pagination.refresh()
  }
}

function refreshNow() {
  pagination.refresh()
}

// 输入防抖 400ms 即时筛选；清空恢复全部
let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(keyword, () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => pagination.refresh(), 400)
})

// 滚动近底部自动加载；回调防重入由 usePagination 短路（loading/finished 已判断）
useInfiniteScroll(() => {
  if (!finished.value && !pagination.loading.value) loadMore()
})

onShow(async () => {
  try {
    if (!(await getSetupStatus())) {
      uni.reLaunch({ url: '/pages/setup/setup' })
      return
    }
  } catch {
    // 探测失败（后端不可达）不阻断首页
  }
  if (!auth.token) {
    uni.reLaunch({ url: '/pages/login/login' })
    return
  }
  auth.fetchMe().catch(() => {
    // 401 由请求层统一清会话并跳登录
  })
  inbox.startPolling()
  loadStats()
  loadOnThisDay()
  loadInteractions()
  // 天气行失败整行静默隐藏（getWeather 模块级缓存，失败不缓存下次重试）
  getWeather()
    .then((w) => (weather.value = w))
    .catch(() => {})
  pagination.refresh()
})

onHide(() => {
  inbox.stopPolling()
})

async function loadStats() {
  try {
    stats.value = await getMyStats()
  } catch {
    // 统计失败保持上次值/空值，不阻断首页
  }
}

// 那年今天：请求失败或空数组都静默隐藏整块（设计纪律：无数据静默）。
async function loadOnThisDay() {
  try {
    otd.value = await getOnThisDay()
  } catch {
    otd.value = []
  }
}

// 互动摘要：失败静默隐藏该行（与"零也显示"不冲突：仅网络失败才藏）。
async function loadInteractions() {
  try {
    interactions.value = await getInteractionSummary()
  } catch {
    interactions.value = null
  }
}

function setKind(k: string) {
  const next = k as KindFilter
  if (kind.value === next) return
  kind.value = next
  pagination.refresh()
}

// 连续 0 天：卡片整体可点，跳发布页开始第一天
function onStreakCard() {
  if (hasStreak.value) return
  uni.navigateTo({ url: '/pages/publish/publish' })
}

// 列表卡上删除成功后移除本地列表项
function onCardDeleted(id: string) {
  items.value = items.value.filter((c) => c.id !== id)
}

// 卡片原地编辑保存成功后本地替换该项
function onCardUpdated(c: ContentItem) {
  items.value = items.value.map((x) => (x.id === c.id ? c : x))
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 0 16px calc(70px + env(safe-area-inset-bottom));
  display: flex;
  flex-direction: column;
  gap: 16px;
  box-sizing: border-box;
}
.today-card {
  margin: 0 -16px;
  padding: 28px 16px 22px;
  background: linear-gradient(180deg, var(--hz-color-primary) 0%, transparent 100%);
  opacity: 0.92;
  animation: today-fade-in 0.5s ease-out;
}
.today-date {
  font-size: 32px;
  font-weight: 800;
  color: var(--hz-color-text);
  line-height: 1.2;
}
.today-lunar {
  display: block;
  margin-top: 6px;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
}
.today-weather {
  display: block;
  margin-top: 4px;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
  text-align: right;
}
@keyframes today-fade-in {
  from {
    opacity: 0;
  }
  to {
    opacity: 0.92;
  }
}
@media (prefers-reduced-motion: reduce) {
  .today-card {
    animation: none;
  }
}
.stats-row {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 10px;
}
.stat-card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 14px 8px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
}
.stat-value {
  font-size: 24px;
  font-weight: 800;
  color: var(--hz-color-primary);
  line-height: 1.1;
}
.stat-label {
  font-size: 12px;
  color: var(--hz-color-text-secondary);
  text-align: center;
}
.stat-value.start {
  font-size: 16px;
}
.stat-label.accent {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.milestones-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.milestone-badge {
  background: var(--hz-color-surface);
  border: 1px solid var(--hz-color-primary);
  color: var(--hz-color-primary);
  border-radius: 999px;
  padding: 4px 12px;
  font-size: var(--hz-font-size-sm);
  font-weight: 600;
}
.interaction-line {
  margin-top: -10px;
  display: flex;
  justify-content: center;
}
.interaction-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.monthly-card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.monthly-line {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
}
.otd-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.otd-item {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 12px 14px;
  display: flex;
  align-items: center;
  gap: 10px;
}
.otd-year {
  font-size: var(--hz-font-size-base);
  font-weight: 800;
  color: var(--hz-color-primary);
  min-width: 44px;
}
.otd-icon {
  font-size: var(--hz-font-size-base);
}
.otd-title {
  flex: 1;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.timeline-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
/* 工具行：筛选左、搜索格右；搜索态由 search-inline 接管（与笔记/收到页同款） */
.tool-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.filter-wrap {
  flex: 1;
  min-width: 0;
}
.search-cell {
  width: 32px;
  height: 32px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  cursor: pointer;
}
.search-inline {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 32px;
  border: 1px solid var(--hz-color-primary);
  border-radius: 999px;
  padding: 0 6px 0 12px;
  background: var(--hz-color-surface);
}
.si-ico {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
  color: var(--hz-color-text-secondary);
}
.si-input {
  flex: 1;
  min-width: 0;
  height: 100%;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  background: transparent;
}
.si-clear {
  width: 20px;
  height: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: var(--hz-color-bg);
  font-size: 13px;
  color: var(--hz-color-text-secondary);
  flex-shrink: 0;
  cursor: pointer;
}
.si-cancel {
  flex-shrink: 0;
  padding: 0 6px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  cursor: pointer;
}
.heatmap-section,
.footprints-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.section-title {
  font-size: var(--hz-font-size-lg);
  font-weight: 700;
  color: var(--hz-color-text);
}
.hint {
  padding: 40px 0;
  display: flex;
  justify-content: center;
}
.hint-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.more {
  display: flex;
  justify-content: center;
  padding: 4px 0 12px;
}
@media (min-width: 768px) {
  .page {
    margin-left: 220px;
    padding-left: max(16px, calc((100% - 220px - 60rem) / 2));
    padding-right: max(16px, calc((100% - 220px - 60rem) / 2));
  }
  .today-card {
    padding: 40px 16px 32px;
  }
}
/* ≥1200 三栏壳：右侧为 300px 工具 dock 留位，内容在侧栏与 dock 之间居中 */
@media (min-width: 1200px) {
  .page {
    margin-right: 300px;
    padding-left: max(16px, calc((100% - 220px - 300px - 60rem) / 2));
    padding-right: max(16px, calc((100% - 220px - 300px - 60rem) / 2));
  }
}
</style>
