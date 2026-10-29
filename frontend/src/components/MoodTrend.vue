<template>
  <!-- 心情趋势卡：近 N 天日均值折线（SVG）+ 按天气分组均值 + 交叉洞察 -->
  <view class="mt">
    <view class="mt-head">
      <text class="mt-title">{{ $t('mood.trendTitle') }}</text>
      <text class="mt-sub">{{ $t('mood.trendSub', { n: data.days }) }}</text>
    </view>

    <!-- 折线：y = 心情 1~5，x = 天；无数据的日期断开 -->
    <view class="mt-chart">
      <svg :viewBox="`0 0 ${W} ${H}`" class="mt-svg" preserveAspectRatio="none">
        <line v-for="g in 5" :key="g" :x1="0" :x2="W" :y1="yOf(g)" :y2="yOf(g)" class="mt-grid" />
        <polyline :points="polylinePoints" class="mt-line" />
        <circle v-for="(p, i) in pointList" :key="i" :cx="p.x" :cy="p.y" r="3" class="mt-dot" />
      </svg>
      <view class="mt-yaxis">
        <text class="mt-y">😄</text>
        <text class="mt-y">😐</text>
        <text class="mt-y">😢</text>
      </view>
    </view>

    <!-- 按天气分组 -->
    <view class="mt-weather">
      <view v-for="w in data.byWeather" :key="w.condition" class="mt-w-row">
        <text class="mt-w-ico">{{ CONDITION_ICONS[w.condition as WeatherCondition] ?? '⛅' }}</text>
        <view class="mt-w-bar">
          <view class="mt-w-fill" :style="{ width: (w.avg / 5) * 100 + '%' }" />
        </view>
        <text class="mt-w-avg">{{ w.avg.toFixed(1) }}</text>
        <text class="mt-w-count">×{{ w.count }}</text>
      </view>
    </view>

    <!-- 交叉洞察 -->
    <text v-if="insightText" class="mt-insight">{{ insightText }}</text>
  </view>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { CONDITION_ICONS, type WeatherCondition } from '../utils/weather'
import { i18n } from '../locale'
import type { MoodReport } from '../api/endpoints'

const props = defineProps<{ data: MoodReport }>()
const { t } = useI18n()

const W = 300
const H = 90
const PAD = 6

// x：窗口首日 → 末日；y：心情 1（底）→ 5（顶）
function xOf(dateStr: string): number {
  const first = props.data.byDay[0]?.date
  const last = props.data.byDay[props.data.byDay.length - 1]?.date
  if (!first || !last) return 0
  const days = Math.max(1, dayDiff(first, last))
  const offset = dayDiff(first, dateStr)
  return PAD + (offset / days) * (W - PAD * 2)
}
function yOf(level: number): number {
  return H - PAD - ((level - 1) / 4) * (H - PAD * 2)
}
function dayDiff(a: string, b: string): number {
  return Math.round((Date.parse(b) - Date.parse(a)) / 86_400_000)
}

const pointList = computed(() =>
  props.data.byDay.map((d) => ({ x: +xOf(d.date).toFixed(1), y: +yOf(d.avg).toFixed(1) }))
)
// 相邻点日期跳 >1.5 天则断线（多段折线，一条 polyline 满足不了断点）
const polylinePoints = computed(() => {
  const pts: { x: number; y: number; break: boolean }[] = []
  let prevDate: string | null = null
  props.data.byDay.forEach((d, i) => {
    const gap = prevDate ? dayDiff(prevDate, d.date) > 1 : false
    pts.push({ x: pointList.value[i].x, y: pointList.value[i].y, break: gap })
    prevDate = d.date
  })
  // gap 处重复上一个点制造垂直回撤到自身（视觉断开：无 Z 填充时用移段）
  let s = ''
  pts.forEach((p, i) => {
    if (i === 0 || p.break) s += `M ${p.x} ${p.y} `
    else s += `L ${p.x} ${p.y} `
  })
  return s.trim()
})

const insightText = computed(() => {
  const ins = props.data.insight
  if (!ins) return ''
  const best = i18n.global.t(`weather.conditions.${ins.best}`)
  const worst = i18n.global.t(`weather.conditions.${ins.worst}`)
  return t('mood.insight', {
    best,
    bestAvg: ins.bestAvg.toFixed(1),
    worst,
    worstAvg: ins.worstAvg.toFixed(1),
    gap: ins.gap.toFixed(1)
  })
})
</script>

<style scoped>
.mt {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.mt-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
}
.mt-title {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.mt-sub {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
}
.mt-chart {
  position: relative;
  padding-left: 22px;
}
.mt-svg {
  width: 100%;
  height: 90px;
  display: block;
}
.mt-grid {
  stroke: var(--hz-color-border);
  stroke-width: 0.5;
  stroke-dasharray: 3 3;
}
.mt-line {
  fill: none;
  stroke: var(--hz-color-primary);
  stroke-width: 2;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.mt-dot {
  fill: var(--hz-color-primary);
}
.mt-yaxis {
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  padding: 4px 0;
}
.mt-y {
  font-size: 11px;
  line-height: 1;
}
.mt-weather {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.mt-w-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.mt-w-ico {
  font-size: 15px;
  width: 20px;
}
.mt-w-bar {
  flex: 1;
  height: 8px;
  border-radius: 4px;
  background: var(--hz-color-bg);
  overflow: hidden;
}
.mt-w-fill {
  height: 100%;
  background: var(--hz-color-primary);
  border-radius: 4px;
}
.mt-w-avg {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  width: 26px;
  text-align: right;
}
.mt-w-count {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
  width: 30px;
  text-align: right;
}
.mt-insight {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  line-height: 1.6;
}
</style>
