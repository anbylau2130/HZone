<template>
  <!-- 桌面三栏壳（composer-polish 03）：右侧工具 dock，仅 ≥1200px 显示。
       本期内容只有「今日」小卡（公历日期+农历+天气）；音乐播放器等后续工具在此追加，本期不做假工具。 -->
  <view class="dock">
    <text class="dock-title">{{ $t('dock.title') }}</text>

    <!-- 今日小卡：公历日期 + 农历（/me/stats 的 today，失败隐藏该行）+ 天气（getWeather，失败只显示农历行） -->
    <view class="dock-today">
      <text class="today-date">{{ gregorian }}</text>
      <text v-if="lunarLine" class="today-lunar">{{ lunarLine }}</text>
      <text v-if="weatherLine" class="today-weather">{{ weatherLine }}</text>
    </view>

    <!-- 音乐播放器（dock 预留位兑现）：面板只是遥控器，音频本体在 player store
         单例上下文（切换页面不中断）；<1200px 由全局 MiniPlayer 桥接管 -->
    <PlayerPanel />
  </view>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import PlayerPanel from './PlayerPanel.vue'
import { getMyStats, type MeStats } from '../api/endpoints'
import { getWeather, CONDITION_ICONS, type WeatherSnapshot } from '../utils/weather'
import { formatLunar } from '../utils/lunar'
import { i18n } from '../locale'

const stats = ref<MeStats | null>(null)
const weather = ref<WeatherSnapshot | null>(null)

// 公历日期「10月20日 星期二」：本地日期即可（与首页今日卡同款），农历才由后端推导。
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

onMounted(() => {
  // 农历失败隐藏该行（与首页今日卡同一静默降级口径）
  getMyStats()
    .then((s) => (stats.value = s))
    .catch(() => {})
  // 天气失败只显示农历行；getWeather 模块级缓存，失败不缓存下次重试
  getWeather()
    .then((w) => (weather.value = w))
    .catch(() => {})
})
</script>

<style scoped>
.dock {
  display: none;
}
@media (min-width: 1200px) {
  .dock {
    display: flex;
    flex-direction: column;
    position: fixed;
    top: 0;
    bottom: 0;
    right: 0;
    width: 300px;
    background: var(--hz-color-surface);
    border-left: 1px solid var(--hz-color-border);
    padding: 20px 16px;
    gap: 16px;
    box-sizing: border-box;
    z-index: 100;
  }
}
.dock-title {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  padding: 0 4px;
  letter-spacing: 0.05em;
}
.dock-today {
  background: var(--hz-color-bg);
  border-radius: var(--hz-radius-lg);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.today-date {
  font-size: 22px;
  font-weight: 800;
  color: var(--hz-color-text);
}
.today-lunar {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
}
.today-weather {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
}
</style>
