<template>
  <view class="page">
    <view class="head">
      <text class="name">{{ nickname || $t('profile.title') }}</text>
      <text class="hint">{{ $t('profile.visibleOnly') }}</text>
    </view>

    <view v-if="pagination.loading.value && !items.length" class="hint">
      <text class="hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="!items.length" class="hint">
      <text class="hint-text">{{ $t('common.empty') }}</text>
    </view>

    <ContentCard v-for="c in items" :key="c.id" :content="c" @open="openDetail" @deleted="onCardDeleted" />

    <view v-if="items.length" class="more">
      <text v-if="finished" class="hint-text">{{ $t('common.noMore') }}</text>
      <button v-else class="pick" :disabled="pagination.loading.value" @click="loadMore">
        {{ $t('common.loadMore') }}
      </button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import ContentCard from '../../components/ContentCard.vue'
import { usePagination } from '../../composables/usePagination'
import { listUserContents, type ContentItem } from '../../api/endpoints'

const nickname = ref('')
const userId = ref('')

const pagination = usePagination<ContentItem>((offset, limit) =>
  listUserContents(userId.value, '', offset, limit)
)
const { items, finished, loadMore } = pagination

onLoad((query) => {
  setNavTitle('profile.title')
  const q = (query ?? {}) as Record<string, string>
  userId.value = q.userId ?? ''
  nickname.value = q.nickname ? decodeURIComponent(q.nickname) : ''
  if (userId.value) {
    pagination.refresh()
  }
})

function openDetail(c: ContentItem) {
  uni.navigateTo({ url: `/pages/detail/detail?id=${c.id}` })
}

// 列表卡上删除成功后移除本地列表项
function onCardDeleted(id: string) {
  items.value = items.value.filter((c) => c.id !== id)
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  box-sizing: border-box;
}
.head {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  align-items: center;
}
.name {
  font-size: 18px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.hint {
  padding: 24px 0;
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
