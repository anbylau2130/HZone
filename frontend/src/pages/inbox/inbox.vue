<template>
  <view class="page">
    <view v-if="pagination.loading.value && !items.length" class="hint">
      <text class="hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="!items.length" class="hint">
      <text class="hint-text">{{ $t('common.empty') }}</text>
    </view>

    <view
      v-for="m in items"
      :key="m.id"
      class="item"
      :class="{ unread: !m.readAt }"
      @click="open(m)"
    >
      <view class="row-line">
        <text class="type">{{ typeLabel(m.type) }}</text>
        <text v-if="!m.readAt" class="unread-dot" />
      </view>
      <text v-if="m.previewText" class="preview">{{ m.previewText }}</text>
      <text class="time">{{ formatDateTime(m.createdAt) }}</text>
    </view>

    <view v-if="items.length" class="more">
      <text v-if="finished" class="hint-text">{{ $t('common.noMore') }}</text>
      <button v-else class="pick" :disabled="pagination.loading.value" @click="loadMore">
        {{ $t('common.loadMore') }}
      </button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { onLoad, onShow } from '@dcloudio/uni-app'
import { usePagination } from '../../composables/usePagination'
import { setNavTitle } from '../../locale'
import { listInbox, markInboxRead, type InboxItem } from '../../api/endpoints'
import { useInboxStore } from '../../stores/inbox'
import { formatDateTime } from '../../utils/format'

const { t } = useI18n()
const inboxStore = useInboxStore()

const pagination = usePagination<InboxItem>((offset, limit) => listInbox(offset, limit))
const { items, finished, loadMore } = pagination

onLoad(() => {
  pagination.refresh()
})

// 返回本页时刷新列表与红点（列表页也能看到已读变化）
onShow(() => {
  setNavTitle('hub.inbox')
  if (items.value.length) {
    pagination.refresh()
  }
  inboxStore.refresh()
})

function typeLabel(type: InboxItem['type']) {
  return t(`inbox.${type}`)
}

async function open(m: InboxItem) {
  if (!m.readAt) {
    try {
      await markInboxRead(m.id)
      inboxStore.refresh()
    } catch {
      // 已读失败不阻断跳转
    }
  }
  if (m.contentId) {
    // 详情页已移除（②C）：按消息内容类型跳对应列表页——日志→日志页、说说→随记页；
    // contentKind 缺失时兜底跳随记页（与 ②C 设计一致，并注释说明）
    if (m.contentKind === 'journal') {
      uni.navigateTo({ url: '/pages/journals/journals' })
    } else {
      uni.navigateTo({ url: '/pages/moments/moments' })
    }
  } else if (m.type === 'friendRequest') {
    uni.navigateTo({ url: '/pages/friends/friends' })
  }
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  box-sizing: border-box;
}
.item {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.item.unread {
  border: 1px solid var(--hz-color-primary);
}
.row-line {
  display: flex;
  align-items: center;
  gap: 6px;
}
.type {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  font-weight: 600;
}
.unread-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #e54d42;
}
.preview {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.time {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.hint {
  padding: 40px 0;
  display: flex;
  justify-content: center;
}
.hint-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.more {
  display: flex;
  justify-content: center;
  padding: 4px 0 12px;
}
.pick {
  width: 60%;
  height: 38px;
  line-height: 38px;
  background: var(--hz-color-surface);
  color: var(--hz-color-primary);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  border: 1px solid var(--hz-color-border);
}
</style>
