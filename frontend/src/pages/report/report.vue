<template>
  <view class="page">
    <!-- 年份切换 -->
    <view v-if="years.length" class="rp-years">
      <view
        v-for="y in years"
        :key="y"
        class="rp-year"
        :class="{ on: y === activeYear }"
        @click="switchYear(y)"
      >
        <text class="rp-year-text">{{ y }}</text>
      </view>
    </view>

    <view v-if="loading" class="rp-empty">
      <text class="rp-empty-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="!report || report.empty" class="rp-empty">
      <text class="rp-empty-ico">🕰️</text>
      <text class="rp-empty-text">{{ $t('report.empty') }}</text>
    </view>

    <template v-else>
      <!-- 封面卡 -->
      <view class="rp-card cover">
        <text class="rp-cover-year">{{ report.year }}</text>
        <text class="rp-cover-title">{{ $t('report.coverTitle') }}</text>
        <text class="rp-cover-total">{{ totalPieces }}</text>
        <text class="rp-cover-sub">{{ $t('report.piecesWritten') }}</text>
      </view>

      <!-- 构成 -->
      <view class="rp-card">
        <view class="rp-duo">
          <view class="rp-duo-col">
            <text class="rp-duo-num">{{ report.sayCount }}</text>
            <text class="rp-duo-label">{{ $t('report.says') }}</text>
          </view>
          <view class="rp-duo-col">
            <text class="rp-duo-num">{{ report.journalCount }}</text>
            <text class="rp-duo-label">{{ $t('report.journals') }}</text>
          </view>
        </view>
      </view>

      <!-- 日夜与坚持 -->
      <view class="rp-card">
        <text class="rp-line">{{ $t('report.daysLine', { d: report.recordedDays }) }}</text>
        <text class="rp-line">{{ $t('report.streakLine', { n: report.longestStreak }) }}</text>
      </view>

      <!-- 足迹 -->
      <view v-if="report.cities.length" class="rp-card">
        <text class="rp-line">{{ $t('report.citiesLine', { n: report.cities.length }) }}</text>
        <view class="rp-city-wrap">
          <view v-for="c in report.cities" :key="c.city" class="rp-city">
            <text class="rp-city-name">{{ c.city }}</text>
            <text class="rp-city-count">×{{ c.count }}</text>
          </view>
        </view>
      </view>

      <!-- 天气 -->
      <view v-if="report.weather.length" class="rp-card">
        <text class="rp-line">{{ $t('report.weatherLine', { w: weatherTopLabel }) }}</text>
        <view class="rp-weather-wrap">
          <view v-for="w in report.weather" :key="w.condition" class="rp-weather">
            <text class="rp-weather-ico">{{ CONDITION_ICONS[w.condition as WeatherCondition] ?? '⛅' }}</text>
            <text class="rp-weather-count">{{ w.count }}</text>
          </view>
        </view>
      </view>

      <!-- 深夜与最晚 -->
      <view class="rp-card">
        <text class="rp-line">{{ $t('report.nightLine', { n: report.nightCount }) }}</text>
        <text v-if="latestLabel" class="rp-line">{{ $t('report.latestLine', { t: latestLabel }) }}</text>
      </view>

      <!-- 高频标签 -->
      <view v-if="report.tags.length" class="rp-card">
        <text class="rp-line">{{ $t('report.tagsLine') }}</text>
        <view class="rp-tag-wrap">
          <text v-for="tg in report.tags" :key="tg.tag" class="rp-tag">#{{ tg.tag }} ×{{ tg.count }}</text>
        </view>
      </view>

      <!-- 记录之最 -->
      <view v-if="report.longestJournal" class="rp-card">
        <text class="rp-line">
          {{ $t('report.longestLine', { n: report.longestJournal.chars }) }}
        </text>
        <text class="rp-sub">《{{ report.longestJournal.title || $t('report.untitled') }}》</text>
      </view>
      <view class="rp-card">
        <text class="rp-line">
          {{ $t('report.mediaLine', { i: report.imageCount, v: report.voiceCount }) }}
        </text>
      </view>

      <!-- 收到的回应 -->
      <view class="rp-card">
        <text class="rp-line">
          {{ $t('report.interactionLine', { c: report.commentsReceived, l: report.likesReceived }) }}
        </text>
      </view>

      <!-- 年度高光 -->
      <view v-if="report.highlight" class="rp-card highlight">
        <text class="rp-hl-title">{{ $t('report.highlightTitle') }}</text>
        <text class="rp-hl-text">“{{ report.highlight.snippet }}”</text>
        <text class="rp-hl-meta">❤ ×{{ report.highlight.likes }}</text>
      </view>
    </template>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import {
  getMyReport,
  type YearlyReport
} from '../../api/endpoints'
import { CONDITION_ICONS, type WeatherCondition } from '../../utils/weather'
import { formatDateTime } from '../../utils/format'
import { i18n } from '../../locale'

const report = ref<YearlyReport | null>(null)
const years = ref<number[]>([])
const activeYear = ref<number | null>(null)
const loading = ref(false)

onShow(() => void load())

async function load(year?: number) {
  loading.value = true
  try {
    const res = await getMyReport(year)
    report.value = res
    years.value = res.years ?? []
    activeYear.value = res.year
  } catch {
    // 请求层已 toast
  } finally {
    loading.value = false
  }
}

function switchYear(y: number) {
  if (y === activeYear.value) return
  void load(y)
}

const totalPieces = computed(() =>
  report.value ? report.value.sayCount + report.value.journalCount : 0
)
const weatherTopLabel = computed(() => {
  const top = report.value?.weather?.[0]
  if (!top) return ''
  return i18n.global.t(`weather.conditions.${top.condition}`)
})
const latestLabel = computed(() =>
  report.value?.latestAt ? formatDateTime(report.value.latestAt) : ''
)
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  gap: 12px;
  max-width: 640px;
  margin: 0 auto;
}
/* 年份切换 */
.rp-years {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}
.rp-year {
  padding: 4px 14px;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  background: var(--hz-color-surface);
  cursor: pointer;
}
.rp-year.on {
  border-color: var(--hz-color-primary);
  background: color-mix(in srgb, var(--hz-color-primary) 10%, transparent);
}
.rp-year-text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  font-variant-numeric: tabular-nums;
}
.rp-year.on .rp-year-text {
  color: var(--hz-color-primary);
  font-weight: 700;
}
/* 卡片 */
.rp-card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 18px 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.rp-card.cover {
  align-items: center;
  padding: 28px 16px;
  background: linear-gradient(135deg, var(--hz-color-primary), color-mix(in srgb, var(--hz-color-primary) 70%, #000));
}
.rp-cover-year {
  font-size: 40px;
  font-weight: 800;
  color: rgba(255, 255, 255, 0.85);
  line-height: 1;
}
.rp-cover-title {
  font-size: 18px;
  font-weight: 700;
  color: #fff;
  margin-top: 6px;
}
.rp-cover-total {
  font-size: 56px;
  font-weight: 800;
  color: #fff;
  line-height: 1.1;
  margin-top: 10px;
  font-variant-numeric: tabular-nums;
}
.rp-cover-sub {
  font-size: var(--hz-font-size-sm);
  color: rgba(255, 255, 255, 0.8);
}
/* 双列 */
.rp-duo {
  display: flex;
}
.rp-duo-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
}
.rp-duo-num {
  font-size: 26px;
  font-weight: 700;
  color: var(--hz-color-text);
  font-variant-numeric: tabular-nums;
}
.rp-duo-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
/* 行文 */
.rp-line {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  line-height: 1.7;
}
.rp-sub {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
/* 城市/天气/标签 chips */
.rp-city-wrap,
.rp-weather-wrap,
.rp-tag-wrap {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.rp-city,
.rp-tag {
  padding: 3px 10px;
  border-radius: 999px;
  background: var(--hz-color-bg);
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
.rp-city-count,
.rp-tag {
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
}
.rp-weather {
  display: flex;
  align-items: baseline;
  gap: 4px;
}
.rp-weather-ico {
  font-size: 22px;
}
.rp-weather-count {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
}
/* 高光 */
.rp-card.highlight {
  border: 1px solid var(--hz-color-primary);
}
.rp-hl-title {
  font-size: var(--hz-font-size-base);
  font-weight: 700;
  color: var(--hz-color-primary);
}
.rp-hl-text {
  font-size: 16px;
  color: var(--hz-color-text);
  line-height: 1.7;
}
.rp-hl-meta {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  align-self: flex-end;
}
.rp-empty {
  padding: 80px 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
}
.rp-empty-ico {
  font-size: 40px;
}
.rp-empty-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
</style>
