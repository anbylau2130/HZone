<template>
  <!-- 全局 mini 桥：<1200px 常驻（≥1200 由 ToolDock 右栏 PlayerPanel 接管，CSS 隐藏本条）。
       音频本体在 player store 模块单例里，本条只是遥控器——页面切换不中断。 -->
  <view v-if="player.tracks.length" class="mp">
    <view class="mp-bar">
      <view class="mp-btn" @click="player.toggle()">
        <text class="mp-ico">{{ player.playing ? '⏸' : '▶' }}</text>
      </view>
      <view class="mp-mid" @click="drawerOpen = true">
        <text class="mp-title">{{ player.current ? player.current.title : $t('player.idle') }}</text>
        <view class="mp-progress">
          <view class="mp-fill" :style="{ width: percent + '%' }" />
        </view>
      </view>
      <view class="mp-btn" @click="player.next()">
        <text class="mp-ico">⏭</text>
      </view>
      <view class="mp-btn" @click="drawerOpen = true">
        <text class="mp-ico">☰</text>
      </view>
    </view>

    <!-- 抽屉：添加块 + 简化列表（点播/删除；排序等完整管理在桌面右栏面板） -->
    <view v-if="drawerOpen" class="mp-overlay" @click="drawerOpen = false">
      <view class="mp-sheet" @click.stop>
        <view class="mp-sheet-head">
          <text class="mp-sheet-title">{{ $t('player.title') }}</text>
          <text class="mp-close" @click="drawerOpen = false">{{ $t('common.cancel') }}</text>
        </view>
        <!-- 添加块（与桌面右栏面板共享）：移动端配置播放列表的唯一入口 -->
        <PlayerAdd />
        <scroll-view scroll-y class="mp-list">
          <view v-if="!player.tracks.length" class="mp-empty">
            <text class="mp-empty-text">{{ $t('player.playlistEmpty') }}</text>
          </view>
          <view
            v-for="(it, i) in player.tracks"
            :key="it.id"
            class="mp-row"
            :class="{ on: i === player.currentIndex }"
            @click="player.playIndex(i)"
          >
            <text class="mp-row-ico">{{ it.sourceKind === 'server' ? '💿' : '' }}</text>
            <text class="mp-row-title">{{ it.title }}</text>
            <text v-if="it.sourceKind !== 'server'" class="mp-row-del" @click.stop="player.removeAndSync(it.id)">✕</text>
          </view>
        </scroll-view>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { usePlayerStore } from '../stores/player'
import PlayerAdd from './PlayerAdd.vue'
const player = usePlayerStore()
const drawerOpen = ref(false)

const percent = computed(() =>
  player.duration > 0 ? Math.min(100, (player.position / player.duration) * 100) : 0
)

onMounted(() => {
  void player.load()
})
</script>

<style scoped>
/* 桥底位置与 TabBar 同拍（70px + 安全区），右留 16px 避开 FAB */
.mp {
  position: fixed;
  left: 12px;
  right: 76px;
  bottom: calc(70px + env(safe-area-inset-bottom) + 8px);
  z-index: 90;
}
/* ≥1200px：ToolDock 右栏已有完整面板，全局桥退场 */
@media (min-width: 1200px) {
  .mp {
    display: none;
  }
}
.mp-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--hz-color-surface);
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  padding: 6px 10px;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.12);
}
.mp-btn {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  background: var(--hz-color-primary);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  cursor: pointer;
}
.mp-ico {
  font-size: 13px;
  color: #fff;
  line-height: 1;
}
.mp-mid {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 3px;
  cursor: pointer;
}
.mp-title {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.mp-progress {
  height: 3px;
  border-radius: 2px;
  background: var(--hz-color-border);
  overflow: hidden;
}
.mp-fill {
  height: 100%;
  background: var(--hz-color-primary);
  transition: width 0.2s linear;
}
.mp-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: flex-end;
  justify-content: center;
  z-index: 999;
}
.mp-sheet {
  width: 100%;
  max-width: 520px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg) var(--hz-radius-lg) 0 0;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.mp-sheet-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.mp-sheet-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.mp-close {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.mp-list {
  max-height: 50vh;
}
.mp-empty {
  padding: 16px 0;
  display: flex;
  justify-content: center;
}
.mp-empty-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.mp-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 10px 4px;
  border-bottom: 1px solid var(--hz-color-border);
  cursor: pointer;
}
.mp-row:last-child {
  border-bottom: none;
}
.mp-row-title {
  flex: 1;
  min-width: 0;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.mp-row-ico {
  font-size: 13px;
  line-height: 1;
}
.mp-row.on .mp-row-title {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.mp-row-del {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  padding: 0 4px;
}
</style>
