<template>
  <!-- 多歌单切换行（面板/抽屉共用）：横向 chip 切换 + ＋新建 + ⋯管理（改名/删除当前） -->
  <view class="pt">
    <scroll-view scroll-x class="pt-scroll" :show-scrollbar="false">
      <view class="pt-row">
        <view
          v-for="p in player.playlists"
          :key="p.id"
          class="pt-chip"
          :class="{ on: p.id === player.activePlaylistId }"
          @click="player.switchPlaylist(p.id)"
        >
          <text class="pt-name">{{ p.name }}</text>
          <text class="pt-count">{{ p.itemCount }}</text>
        </view>
      </view>
    </scroll-view>
    <view class="pt-op" :title="$t('player.newPlaylist')" @click="createList">＋</view>
    <view class="pt-op" :title="$t('common.more')" @click="manage">⋯</view>
  </view>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { usePlayerStore } from '../stores/player'

const { t } = useI18n()
const player = usePlayerStore()

function promptName(title: string, initial: string, onDone: (name: string) => void) {
  uni.showModal({
    title,
    editable: true,
    placeholderText: t('player.playlistNamePlaceholder'),
    content: initial,
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
    success: (res) => {
      const name = (res.content ?? '').trim()
      if (!res.confirm || !name) return
      onDone(name)
    }
  })
}

function createList() {
  promptName(t('player.newPlaylist'), '', async (name) => {
    try {
      await player.createAndSwitch(name)
    } catch {
      // 请求层已 toast（限额/校验）
    }
  })
}

function manage() {
  const active = player.playlists.find((p) => p.id === player.activePlaylistId)
  if (!active) return
  uni.showActionSheet({
    itemList: [t('player.renameList'), t('player.deleteList')],
    success: (res) => {
      if (res.tapIndex === 0) {
        promptName(t('player.renameList'), active.name, async (name) => {
          try {
            await player.renameList(active.id, name)
          } catch {
            // 请求层已 toast
          }
        })
      } else if (res.tapIndex === 1) {
        uni.showModal({
          title: t('player.deleteList'),
          content: t('player.deleteListConfirm'),
          confirmText: t('common.confirm'),
          cancelText: t('common.cancel'),
          success: async (m) => {
            if (!m.confirm) return
            try {
              await player.removeList(active.id)
            } catch {
              // 请求层已 toast
            }
          }
        })
      }
    }
  })
}
</script>

<style scoped>
.pt {
  display: flex;
  align-items: center;
  gap: 6px;
}
.pt-scroll {
  flex: 1;
  min-width: 0;
  white-space: nowrap;
}
.pt-row {
  display: inline-flex;
  gap: 6px;
}
.pt-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 3px 10px;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  cursor: pointer;
  flex-shrink: 0;
}
.pt-chip.on {
  border-color: var(--hz-color-primary);
  background: color-mix(in srgb, var(--hz-color-primary) 10%, transparent);
}
.pt-name {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  max-width: 96px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.pt-chip.on .pt-name {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.pt-count {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
}
.pt-op {
  flex-shrink: 0;
  width: 26px;
  height: 26px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px dashed var(--hz-color-border);
  border-radius: 50%;
  font-size: 14px;
  color: var(--hz-color-text-secondary);
  cursor: pointer;
}
</style>
