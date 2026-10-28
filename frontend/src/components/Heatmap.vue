<template>
  <view class="heatmap">
    <view class="grid">
      <view v-for="(col, ci) in columns" :key="ci" class="col" :class="{ today: col.hasToday }">
        <view
          v-for="cell in col.cells"
          :key="cell.key"
          class="cell"
          :class="cell.date ? `lv${cell.level}` : 'blank'"
          @click="cell.date && pick(cell.date)"
        />
      </view>
    </view>
    <view class="legend">
      <text class="legend-text">{{ $t('home.heatmapLess') }}</text>
      <view class="cell lv0" />
      <view class="cell lv1" />
      <view class="cell lv2" />
      <view class="cell lv3" />
      <view class="cell lv4" />
      <text class="legend-text">{{ $t('home.heatmapMore') }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { HeatmapCell } from '../api/endpoints'

const props = defineProps<{ cells: HeatmapCell[] }>()

interface GridCell {
  key: string
  date: string | null
  level: number
}

interface GridColumn {
  hasToday: boolean
  cells: GridCell[]
}

// 近 90 天按周列（周日开头）排布：窗口首日不在周日时首列顶部补空位，
// 各列同一行恒为同一星期几，末列即本周（今日列高亮，今日格描边）。
const columns = computed<GridColumn[]>(() => {
  const map = new Map(props.cells.map((c) => [c.date, c.count]))
  const first = new Date(`${props.cells[0].date}T00:00:00`)
  const last = new Date(`${props.cells[props.cells.length - 1].date}T00:00:00`)
  const start = new Date(first)
  start.setDate(start.getDate() - start.getDay())

  const todayKey = isoOf(new Date())
  const cols: GridColumn[] = []
  const cursor = new Date(start)
  while (cursor <= last) {
    const cells: GridCell[] = []
    for (let i = 0; i < 7; i++) {
      const d = new Date(cursor)
      d.setDate(d.getDate() + i)
      const key = isoOf(d)
      const inWindow = d >= first && d <= last
      cells.push({
        key,
        date: inWindow ? key : null,
        level: levelOf(map.get(key) ?? 0)
      })
    }
    cols.push({ hasToday: cells.some((c) => c.date === todayKey), cells })
    cursor.setDate(cursor.getDate() + 7)
  }
  return cols
})

// 5 档：0 篇 / 1 / 2 / 3 / 4+ 篇。
function levelOf(count: number): number {
  return Math.min(count + 1, 4)
}

function isoOf(d: Date): string {
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${m}-${day}`
}

// 点格下钻：跳时间线并筛选该天。
function pick(date: string) {
  uni.navigateTo({ url: `/pages/timeline/timeline?date=${date}` })
}
</script>

<style scoped>
.heatmap {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
/* 紧凑自然排：格子统一 17px、列距固定 3px，整图在卡内居中
 （手机上几乎占满；桌面居中不拉伸——space-between 大列距被反馈怪） */
.grid {
  display: flex;
  justify-content: center;
  gap: 3px;
  width: 100%;
}
.col {
  flex: 1 1 0;
  max-width: 17px;
  display: flex;
  flex-direction: column;
  gap: 3px;
}
.col.today {
  background: color-mix(in srgb, var(--hz-color-primary) 10%, transparent);
  border-radius: 4px;
}
.cell {
  width: 100%;
  aspect-ratio: 1;
  border-radius: 2px;
}
.cell.blank {
  visibility: hidden;
}
.cell.lv0 {
  background: transparent;
  border: 1px solid var(--hz-color-border);
  box-sizing: border-box;
}
.cell.lv1 {
  background: color-mix(in srgb, var(--hz-color-primary) 25%, var(--hz-color-surface));
}
.cell.lv2 {
  background: color-mix(in srgb, var(--hz-color-primary) 45%, var(--hz-color-surface));
}
.cell.lv3 {
  background: color-mix(in srgb, var(--hz-color-primary) 70%, var(--hz-color-surface));
}
.cell.lv4 {
  background: var(--hz-color-primary);
}
.legend {
  display: flex;
  align-items: center;
  gap: 4px;
}
/* 图例小格与网格格同尺寸 */
.legend .cell {
  width: 15px;
  height: 15px;
  flex: none;
}
.legend-text {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
}
</style>
