<template>
  <view class="footprints">
    <view v-for="(f, i) in items" :key="i" class="row">
      <view class="dot" />
      <view v-if="i < items.length - 1" class="line" />
      <view class="info">
        <text class="city">{{ f.city }}</text>
        <text class="time">{{ monthOf(f.firstAt) }}</text>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import type { Footprint } from '../api/endpoints'

defineProps<{ items: Footprint[] }>()

// 首达月份：ISO 时间戳取 yyyy-MM，与热力格日期同格式（语言无关）。
function monthOf(iso: string): string {
  return iso.slice(0, 7)
}
</script>

<style scoped>
.footprints {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 12px;
}
.row {
  position: relative;
  padding-left: 20px;
  padding-bottom: 14px;
}
.row:last-child {
  padding-bottom: 0;
}
.dot {
  position: absolute;
  left: 2px;
  top: 4px;
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--hz-color-primary);
}
.line {
  position: absolute;
  left: 6px;
  top: 18px;
  bottom: 0;
  width: 2px;
  background: var(--hz-color-border);
}
.info {
  display: flex;
  align-items: baseline;
  gap: 8px;
}
.city {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.time {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
</style>
