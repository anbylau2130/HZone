<template>
  <!-- 全局 mini 桥：<1200px 常驻（≥1200 由 ToolDock 右栏 PlayerPanel 接管）。
       音频本体在 player store 模块单例里，本条只是遥控器——页面切换不中断。
       隐藏态（✕ 收起）：右缘吸附钮 = 播放/暂停 + 展开恢复，免进设置页。 -->
  <view v-if="player.tracks.length" class="mp">
    <!-- 桥本体：抽屉打开期间隐藏（面板盖不住整条桥，避免叠影） -->
    <view v-if="!player.hidden && !drawerOpen" class="mp-bar">
      <view class="mp-btn" @click="player.toggle()">
        <PlayerIcon :name="player.playing ? 'pause' : 'play'" :size="14" />
      </view>
      <view class="mp-mid" @click="drawerOpen = true">
        <text class="mp-title">{{ player.current ? player.current.title : $t('player.idle') }}</text>
        <view class="mp-progress">
          <view class="mp-fill" :style="{ width: percent + '%' }" />
        </view>
      </view>
      <view class="mp-btn" @click="player.next()">
        <PlayerIcon name="next" :size="14" />
      </view>
      <view class="mp-btn" @click="drawerOpen = true">
        <PlayerIcon name="list" :size="14" />
      </view>
      <!-- 收起播放器（右缘吸附钮随时恢复） -->
      <view class="mp-btn collapse" :title="$t('player.collapse')" @click="player.setHidden(true)">
        <PlayerIcon name="close" :size="11" />
      </view>
    </view>

    <!-- 隐藏态：吸附屏幕右缘的快捷钮——上：播放/暂停（图标随状态）；下：展开恢复 -->
    <view v-if="player.hidden" class="mp-tab">
      <view class="mp-tab-btn" :title="$t('player.playPause')" @click="player.toggle()">
        <PlayerIcon :name="player.playing ? 'pause' : 'play'" :size="15" />
      </view>
      <view class="mp-tab-btn aux" :title="$t('player.expand')" @click="player.setHidden(false)">
        <PlayerIcon name="note" :size="15" />
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
            <text
              v-if="it.sourceKind !== 'server'"
              class="mp-row-rename"
              @click.stop="renameRow(it)"
            >✎</text>
            <text v-if="it.sourceKind !== 'server'" class="mp-row-del" @click.stop="player.removeAndSync(it.id)">✕</text>
          </view>
        </scroll-view>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { usePlayerStore } from '../stores/player'
import PlayerAdd from './PlayerAdd.vue'
import PlayerIcon from './PlayerIcon.vue'
const { t } = useI18n()
const player = usePlayerStore()
const drawerOpen = ref(false)

/** 列表项重命名（与面板同款弹窗）。 */
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

const percent = computed(() =>
  player.duration > 0 ? Math.min(100, (player.position / player.duration) * 100) : 0
)

onMounted(() => {
  void player.load()
})
</script>

<style scoped>
/* 手机：桥吸附在 TabBar 正上方（与 TabBar 同高锚定，不留缝），右留 76px 避开 FAB */
.mp-bar {
  position: fixed;
  left: 12px;
  right: 76px;
  bottom: calc(56px + env(safe-area-inset-bottom));
  z-index: 90;
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--hz-color-surface);
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  padding: 6px 10px;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.12);
}
/* ≥768 平板：TabBar 退场改左侧栏，桥从侧栏右侧贴到屏底右缘成停靠条
   （高 48px < 页面自带 70px 底留白，滚到底不压内容；FAB 在 86px 起纵向错开） */
@media (min-width: 768px) {
  .mp-bar {
    left: 220px;
    right: 0;
    bottom: 0;
    border-radius: var(--hz-radius-lg) var(--hz-radius-lg) 0 0;
    padding: 6px 12px 6px 16px;
  }
}
/* ≥1200 桌面：ToolDock 右栏已有完整面板，桥退场（隐藏态吸附钮仍在场） */
@media (min-width: 1200px) {
  .mp-bar {
    display: none;
  }
}
/* 隐藏态：吸附屏幕右缘的快捷钮（半胶囊贴边），垂直居中避开 FAB/TabBar */
.mp-tab {
  position: fixed;
  right: 0;
  top: 50%;
  transform: translateY(-50%);
  z-index: 120;
  display: flex;
  flex-direction: column;
  gap: 6px;
  background: var(--hz-color-surface);
  border: 1px solid var(--hz-color-border);
  border-right: none;
  border-radius: var(--hz-radius-lg) 0 0 var(--hz-radius-lg);
  padding: 8px 5px;
  box-shadow: -4px 4px 14px rgba(0, 0, 0, 0.12);
}
.mp-tab-btn {
  width: 34px;
  height: 34px;
  border-radius: 50%;
  background: var(--hz-color-primary);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.mp-tab-btn.aux {
  background: transparent;
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
/* ≥1200 桌面：吸附到右栏 dock 左缘（dock 占 right 300px） */
@media (min-width: 1200px) {
  .mp-tab {
    right: 300px;
  }
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
.mp-btn .pi {
  color: #fff;
}
/* 收起钮弱一档（主色底半透明描边），与功能钮区分 */
.mp-btn.collapse {
  background: transparent;
  border: 1px solid var(--hz-color-border);
}
.mp-btn.collapse .pi {
  color: var(--hz-color-text-secondary);
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
  /* 88vh 封顶：PlayerAdd 展开 + 长列表也只内部滚动，标题行不会被推出屏 */
  max-height: calc(88vh - env(safe-area-inset-bottom));
  margin-bottom: env(safe-area-inset-bottom);
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
  /* 弹性占位：吃掉 88vh 封顶后的剩余空间，自身内部滚动 */
  flex: 1;
  min-height: 0;
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
.mp-row-rename {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  padding: 0 4px;
}
.mp-row-del {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  padding: 0 4px;
}
</style>
