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

    <!-- 热力格（05）：近 90 天发布密度；stats 已加载即显示（全 0 也是"从今天开始"的留白） -->
    <view v-if="stats" class="heatmap-section">
      <text class="section-title">{{ $t('home.heatmapTitle') }}</text>
      <Heatmap :cells="stats.heatmap" />
    </view>

    <!-- 足迹（05）：天气快照城市时间线，同城连续去重；无城市数据隐藏 -->
    <view v-if="stats?.footprints?.length" class="footprints-section">
      <text class="section-title">{{ $t('home.footprintsTitle') }}</text>
      <Footprints :items="stats.footprints" />
    </view>

    <!-- 我的时间线：带全部/随记/日志筛选，复用 timeline 数据逻辑 -->
    <view class="timeline-section">
      <text class="section-title">{{ $t('home.timeline') }}</text>
      <SegmentedFilter :model-value="kind" :options="kinds" @update:model-value="setKind" />

      <view v-if="pagination.loading.value && !items.length" class="hint">
        <text class="hint-text">{{ $t('common.loading') }}</text>
      </view>
      <view v-else-if="!items.length" class="hint">
        <text class="hint-text">{{ $t('common.empty') }}</text>
      </view>

      <ContentCard v-for="c in items" :key="c.id" :content="c" @open="openDetail" />

      <view v-if="items.length" class="more">
        <text v-if="finished" class="hint-text">{{ $t('common.noMore') }}</text>
        <button v-else class="pick" :disabled="pagination.loading.value" @click="loadMore">
          {{ $t('common.loadMore') }}
        </button>
      </view>
    </view>

    <AppSidebar current="index" />
    <AppTabBar current="index" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow, onHide } from '@dcloudio/uni-app'
import { useAuthStore } from '../../stores/auth'
import { useInboxStore } from '../../stores/inbox'
import {
  getSetupStatus,
  getMyStats,
  listMyContents,
  type ContentItem,
  type KindFilter,
  type MeStats
} from '../../api/endpoints'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import ContentCard from '../../components/ContentCard.vue'
import SegmentedFilter from '../../components/SegmentedFilter.vue'
import Heatmap from '../../components/Heatmap.vue'
import Footprints from '../../components/Footprints.vue'
import { usePagination } from '../../composables/usePagination'
import { getWeather, CONDITION_ICONS, type WeatherSnapshot } from '../../utils/weather'
import { formatLunar } from '../../utils/lunar'
import { i18n } from '../../locale'

const auth = useAuthStore()
const inbox = useInboxStore()
const stats = ref<MeStats | null>(null)
const weather = ref<WeatherSnapshot | null>(null)

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

const kinds: { value: KindFilter; label: string }[] = [
  { value: '', label: 'timeline.filterAll' },
  { value: 'say', label: 'timeline.kindSay' },
  { value: 'journal', label: 'timeline.kindJournal' }
]
const kind = ref<KindFilter>('')

const pagination = usePagination<ContentItem>((offset, limit) =>
  listMyContents(kind.value, offset, limit)
)
const { items, finished, loadMore } = pagination

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

function openDetail(c: ContentItem) {
  uni.navigateTo({ url: `/pages/detail/detail?id=${c.id}` })
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
.timeline-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
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
.pick {
  width: 60%;
  height: 38px;
  line-height: 38px;
  background: var(--hz-color-surface);
  color: var(--hz-color-primary);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  border: 1px solid var(--hz-color-border);
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
</style>
