<template>
  <view class="page">
    <view v-if="pagination.loading.value && !items.length" class="hint">
      <text class="hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="!items.length" class="hint">
      <text class="hint-text">{{ $t('moments.empty') }}</text>
    </view>

    <view
      v-for="c in items"
      :key="c.id"
      class="card"
      @click="openDetail(c)"
    >
      <view class="head">
        <view class="avatar">{{ c.author.nickname.slice(0, 1) }}</view>
        <view class="head-mid">
          <text class="name">{{ c.author.nickname }}</text>
          <text class="time">{{ formatRelativeTime(c.createdAt) }}</text>
        </view>
      </view>
      <text class="text">{{ c.text }}</text>
      <MediaGrid v-if="(c.media ?? []).length" :media="c.media" />
      <RecipientsBar :recipients="c.recipients" />
      <view class="foot">
        <text class="foot-btn">{{ $t('detail.commentCount', { n: c.commentCount }) }}</text>
      </view>
    </view>

    <view v-if="items.length" class="more">
      <text v-if="finished" class="hint-text">{{ $t('common.noMore') }}</text>
      <button v-else class="pick" :disabled="pagination.loading.value" @click="loadMore">
        {{ $t('common.loadMore') }}
      </button>
    </view>

    <AppSidebar current="moments" />
    <AppTabBar current="moments" />
  </view>
</template>

<script setup lang="ts">
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import MediaGrid from '../../components/MediaGrid.vue'
import RecipientsBar from '../../components/RecipientsBar.vue'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import { usePagination } from '../../composables/usePagination'
import { listMyContents, type ContentItem } from '../../api/endpoints'
import { formatRelativeTime } from '../../utils/format'

const pagination = usePagination<ContentItem>((offset, limit) => listMyContents('say', offset, limit))
const { items, finished, loadMore } = pagination

// onShow 而非 onLoad：H5 端直接 hash 进入时 onLoad 不触发（timeline 同款坑），onShow 稳定
onShow(() => {
  setNavTitle('moments.title')
  pagination.refresh()
})

function openDetail(c: ContentItem) {
  uni.navigateTo({ url: `/pages/detail/detail?id=${c.id}` })
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px 16px calc(70px + env(safe-area-inset-bottom));
  display: flex;
  flex-direction: column;
  gap: 12px;
  box-sizing: border-box;
}
.card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.head {
  display: flex;
  align-items: center;
  gap: 10px;
}
.avatar {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: var(--hz-color-primary);
  color: #fff;
  font-size: 17px;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.head-mid {
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.name {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.time {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.foot {
  border-top: 1px solid var(--hz-color-border);
  padding-top: 8px;
}
.foot-btn {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
}
.more {
  display: flex;
  justify-content: center;
  padding: 4px 0 12px;
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
.pick {
  width: 60%;
  height: 38px;
  line-height: 38px;
  background: var(--hz-color-surface);
  color: var(--hz-color-interactive);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  border: 1px solid var(--hz-color-border);
}
@media (min-width: 768px) {
  .page {
    margin-left: 220px;
    padding-left: max(16px, calc((100% - 220px - 42rem) / 2));
    padding-right: max(16px, calc((100% - 220px - 42rem) / 2));
  }
  .card {
    padding: 20px;
  }
}
</style>
