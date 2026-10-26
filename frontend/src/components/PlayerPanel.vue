<template>
  <view class="pp">
    <view class="pp-head">
      <text class="pp-title">{{ $t('player.title') }}</text>
      <view class="pp-loop" :class="{ on: player.loopMode === 'one' }" @click="player.toggleLoopMode()">
        <text class="pp-loop-text">{{ player.loopMode === 'one' ? $t('player.loopOne') : $t('player.loopList') }}</text>
      </view>
    </view>

    <!-- 正在播放：曲名 + 进度（点击 seek） -->
    <view class="pp-now">
      <text class="pp-now-title">{{ player.current ? player.current.title : $t('player.idle') }}</text>
      <view class="pp-bar" @click="onSeek">
        <view class="pp-bar-fill" :style="{ width: barPercent + '%' }" />
      </view>
      <text class="pp-time">{{ fmt(player.position) }} / {{ fmt(player.duration) }}</text>
    </view>

    <!-- 控制：上一首 / 播放暂停 / 下一首 -->
    <view class="pp-ctrl">
      <view class="pp-btn" @click="player.prev()"><text class="pp-btn-ico">⏮</text></view>
      <view class="pp-btn main" @click="player.toggle()">
        <text class="pp-btn-ico">{{ player.playing ? '⏸' : '▶' }}</text>
      </view>
      <view class="pp-btn" @click="player.next()"><text class="pp-btn-ico">⏭</text></view>
    </view>

    <!-- 添加块（与移动端抽屉共享）：音频库+上传 / 外链 -->
    <PlayerAdd />

    <!-- 播放列表 -->
    <scroll-view scroll-y class="pp-list">
      <view v-if="!player.items.length" class="pp-empty">
        <text class="pp-empty-text">{{ $t('player.playlistEmpty') }}</text>
      </view>
      <view
        v-for="(it, i) in player.items"
        :key="it.id"
        class="pp-row"
        :class="{ on: i === player.currentIndex }"
        @click="player.playIndex(i)"
      >
        <text class="pp-row-ico">{{ i === player.currentIndex && player.playing ? '🔊' : it.sourceKind === 'url' ? '🔗' : '🎵' }}</text>
        <text class="pp-row-title">{{ it.title }}</text>
        <view class="pp-row-ops" @click.stop>
          <text class="pp-op" :class="{ dim: i === 0 }" @click="player.moveAndSync(i, -1)">↑</text>
          <text class="pp-op" :class="{ dim: i === player.items.length - 1 }" @click="player.moveAndSync(i, 1)">↓</text>
          <text class="pp-op danger" @click="player.removeAndSync(it.id)">✕</text>
        </view>
      </view>
    </scroll-view>
  </view>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { usePlayerStore } from '../stores/player'
import PlayerAdd from './PlayerAdd.vue'

const player = usePlayerStore()

onMounted(() => {
  void player.load()
})

const barPercent = computed(() =>
  player.duration > 0 ? Math.min(100, (player.position / player.duration) * 100) : 0
)

function fmt(sec: number): string {
  if (!Number.isFinite(sec) || sec <= 0) return '0:00'
  const m = Math.floor(sec / 60)
  const s = Math.floor(sec % 60)
  return `${m}:${String(s).padStart(2, '0')}`
}

function onSeek(e: MouseEvent) {
  const el = e.currentTarget as HTMLElement
  const rect = el.getBoundingClientRect()
  if (rect.width <= 0 || player.duration <= 0) return
  player.seek(((e.clientX - rect.left) / rect.width) * player.duration)
}
</script>

<style scoped>
.pp {
  background: var(--hz-color-bg);
  border-radius: var(--hz-radius-lg);
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.pp-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.pp-title {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.pp-loop {
  padding: 2px 10px;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  cursor: pointer;
}
.pp-loop.on {
  background: var(--hz-color-primary);
  border-color: var(--hz-color-primary);
}
.pp-loop-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.pp-loop.on .pp-loop-text {
  color: #fff;
}
.pp-now {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.pp-now-title {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.pp-bar {
  height: 6px;
  border-radius: 3px;
  background: var(--hz-color-border);
  overflow: hidden;
  cursor: pointer;
}
.pp-bar-fill {
  height: 100%;
  border-radius: 3px;
  background: var(--hz-color-primary);
  transition: width 0.2s linear;
}
.pp-time {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
}
.pp-ctrl {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 18px;
}
.pp-btn {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  border: 1px solid var(--hz-color-border);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.pp-btn.main {
  width: 44px;
  height: 44px;
  background: var(--hz-color-primary);
  border-color: var(--hz-color-primary);
}
.pp-btn-ico {
  font-size: 16px;
  color: var(--hz-color-text-secondary);
  line-height: 1;
}
.pp-btn.main .pp-btn-ico {
  font-size: 18px;
  color: #fff;
}
.pp-list {
  max-height: 280px;
}
.pp-empty {
  padding: 12px 0;
  display: flex;
  justify-content: center;
}
.pp-empty-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.pp-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 6px;
  border-radius: var(--hz-radius-md);
  cursor: pointer;
}
.pp-row:hover {
  background: var(--hz-color-surface);
}
.pp-row.on {
  background: var(--hz-color-surface);
}
.pp-row-ico {
  font-size: 14px;
  line-height: 1;
}
.pp-row-title {
  flex: 1;
  min-width: 0;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.pp-row.on .pp-row-title {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.pp-row-ops {
  display: flex;
  gap: 6px;
}
.pp-op {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  padding: 0 2px;
  cursor: pointer;
}
.pp-op.danger {
  color: #e54d42;
}
.pp-op.dim {
  opacity: 0.3;
  pointer-events: none;
}
</style>
