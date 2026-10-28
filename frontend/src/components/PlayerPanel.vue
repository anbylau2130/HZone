<template>
  <view v-if="!player.hidden" class="pp">
    <view class="pp-head">
      <text class="pp-title">{{ $t('player.title') }}</text>
      <view class="pp-head-right">
        <view class="pp-loop" :class="{ on: player.loopMode === 'one' }" @click="player.toggleLoopMode()">
          <text class="pp-loop-text">{{ player.loopMode === 'one' ? $t('player.loopOne') : $t('player.loopList') }}</text>
        </view>
        <!-- 收起面板（右缘吸附钮随时恢复） -->
        <view class="pp-collapse" :title="$t('player.collapse')" @click="player.setHidden(true)">
          <PlayerIcon name="close" :size="10" />
        </view>
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
      <view class="pp-btn" @click="player.prev()"><PlayerIcon name="prev" :size="16" /></view>
      <view class="pp-btn main" @click="player.toggle()">
        <PlayerIcon :name="player.playing ? 'pause' : 'play'" :size="20" />
      </view>
      <view class="pp-btn" @click="player.next()"><PlayerIcon name="next" :size="16" /></view>
    </view>

    <!-- 添加块（与移动端抽屉共享）：音频库+上传 / 外链 -->
    <PlayerAdd />

    <!-- 播放列表：合并序列 = 服务器音乐（默认曲库，仅可播）+ 用户项（可排序/删除） -->
    <scroll-view scroll-y class="pp-list">
      <view v-if="!player.tracks.length" class="pp-empty">
        <text class="pp-empty-text">{{ $t('player.playlistEmpty') }}</text>
      </view>
      <view v-if="player.serverFiles.length" class="pp-group">
        <text class="pp-group-label">{{ $t('player.serverGroup') }}</text>
      </view>
      <view
        v-for="(it, i) in player.tracks"
        :key="it.id"
        class="pp-row"
        :class="{ on: i === player.currentIndex, srv: it.sourceKind === 'server' }"
        @click="player.playIndex(i)"
      >
        <text class="pp-row-ico">{{ i === player.currentIndex && player.playing ? '🔊' : it.sourceKind === 'url' ? '🔗' : it.sourceKind === 'server' ? '💿' : '🎵' }}</text>
        <text class="pp-row-title">{{ it.title }}</text>
        <view v-if="it.sourceKind !== 'server'" class="pp-row-ops" @click.stop>
          <text class="pp-op" :title="$t('common.rename')" @click="renameRow(it)">✎</text>
          <text class="pp-op" :class="{ dim: userIndexOf(it.id) <= 0 }" @click="player.moveAndSync(it.id, -1)">↑</text>
          <text class="pp-op" :class="{ dim: userIndexOf(it.id) >= player.items.length - 1 }" @click="player.moveAndSync(it.id, 1)">↓</text>
          <text class="pp-op danger" @click="player.removeAndSync(it.id)">✕</text>
        </view>
      </view>
    </scroll-view>
  </view>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { usePlayerStore } from '../stores/player'
import PlayerAdd from './PlayerAdd.vue'
import PlayerIcon from './PlayerIcon.vue'

const { t } = useI18n()

const player = usePlayerStore()

onMounted(() => {
  void player.load()
})

/** 用户项在其子序列中的下标（排序按钮边界判断用）。 */
function userIndexOf(id: string): number {
  return player.items.findIndex((x) => x.id === id)
}

/** 列表项重命名（歌名独立快照，当前曲名即时跟随）。 */
function renameRow(it: { id: string; title: string; sourceKind: string }) {
  if (it.sourceKind === 'server') return
  uni.showModal({
    title: t('common.rename'),
    editable: true,
    placeholderText: it.title,
    content: it.title,
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
    success: async (res) => {
      const title = (res.content ?? '').trim()
      if (!res.confirm || !title || title === it.title) return
      try {
        await player.renameItem(it.id, title)
      } catch {
        // 请求层已 toast
      }
    }
  })
}

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
  /* dock 是固定整高列：面板吃剩余空间并可收缩，列表随之弹性，不被挤出视口 */
  flex: 1;
  min-height: 0;
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
.pp-head-right {
  display: flex;
  align-items: center;
  gap: 6px;
}
.pp-collapse {
  width: 22px;
  height: 22px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--hz-color-border);
  border-radius: 50%;
  cursor: pointer;
  color: var(--hz-color-text-secondary);
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
  color: var(--hz-color-text-secondary);
}
.pp-btn.main {
  width: 44px;
  height: 44px;
  background: var(--hz-color-primary);
  border-color: var(--hz-color-primary);
  color: #fff;
}
.pp-list {
  flex: 1;
  min-height: 80px;
}
.pp-group {
  padding: 4px 6px 2px;
}
.pp-group-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  letter-spacing: 0.04em;
}
.pp-row.srv .pp-row-title {
  color: var(--hz-color-text-secondary);
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
