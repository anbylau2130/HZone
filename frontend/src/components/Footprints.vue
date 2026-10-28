<template>
  <!-- 足迹：横向滚动卡（最近在左），每卡 = 城市 + 天气快照 + 日期 -->
  <view class="footprints">
    <view v-for="(f, i) in items" :key="i" class="chip">
      <text class="city">{{ f.city }}</text>
      <text v-if="weatherLine(f)" class="weather">{{ weatherLine(f) }}</text>
      <text class="time">{{ dateOf(f.firstAt) }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { CONDITION_ICONS, type WeatherCondition } from '../utils/weather'
import type { Footprint } from '../api/endpoints'

defineProps<{ items: Footprint[] }>()

// 日期：ISO 取 yyyy-MM-dd（与热力格同格式，语言无关）。
function dateOf(iso: string): string {
  return iso.slice(0, 10)
}

/** 天气行：现象图标 + 温度（打卡时刻的快照；缺项只显示存在的部分）。 */
function weatherLine(f: Footprint): string {
  const icon = f.condition ? CONDITION_ICONS[f.condition as WeatherCondition] ?? '' : ''
  const temp = f.tempC != null ? `${Math.round(f.tempC)}°` : ''
  return [icon, temp].filter(Boolean).join(' ')
}
</script>

<style scoped>
.footprints {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 12px;
  display: flex;
  gap: 10px;
  overflow-x: auto;
  /* 隐藏滚动条但保留横向滚动（移动端手势/桌面滚轮+拖拽） */
  scrollbar-width: none;
}
.footprints::-webkit-scrollbar {
  display: none;
}
.chip {
  flex-shrink: 0;
  min-width: 96px;
  padding: 10px 12px;
  border-radius: var(--hz-radius-md);
  background: var(--hz-color-bg);
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.city {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
  white-space: nowrap;
}
.weather {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  white-space: nowrap;
}
.time {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
}
</style>
