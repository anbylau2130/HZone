<template>
  <view class="page">
    <view class="container">
      <view class="topbar">
        <text class="topbar-title">{{ $t('journals.title') }}</text>
        <view v-if="items.length > 0" class="write-btn" @click="goPublish">
          <text class="write-plus">＋</text>
          <text>{{ $t('journals.write') }}</text>
        </view>
      </view>

      <view v-if="pagination.loading.value && !items.length" class="hint">
        <text class="hint-text">{{ $t('common.loading') }}</text>
      </view>
      <view v-else-if="!items.length" class="hint empty">
        <text class="hint-text">{{ $t('journals.empty') }}</text>
        <view class="write-btn empty-cta" @click="goPublish">
          <text class="write-plus">＋</text>
          <text>{{ $t('journals.write') }}</text>
        </view>
      </view>

      <view v-if="items.length" class="cards">
        <view
          v-for="c in items"
          :key="c.id"
          class="card"
          @click="openDetail(c)"
        >
          <text class="title">{{ c.title }}</text>
          <text class="summary">{{ summary(c) }}</text>
          <text class="date">{{ formatDateTime(c.createdAt) }}</text>
        </view>
      </view>

      <view v-if="items.length" class="more">
        <text v-if="finished" class="hint-text">{{ $t('common.noMore') }}</text>
        <button v-else class="pick" :disabled="pagination.loading.value" @click="loadMore">
          {{ $t('common.loadMore') }}
        </button>
      </view>
    </view>

    <AppSidebar current="journals" />
    <AppTabBar current="journals" />
    <view class="fab" @click="goPublish">＋</view>
  </view>
</template>

<script setup lang="ts">
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import { usePagination } from '../../composables/usePagination'
import { listMyContents, type ContentItem } from '../../api/endpoints'
import { formatDateTime, stripMarkdown } from '../../utils/format'

const pagination = usePagination<ContentItem>((offset, limit) =>
  listMyContents('journal', offset, limit)
)
const { items, finished, loadMore } = pagination

// onShow 而非 onLoad：H5 端直接 hash 进入时 onLoad 不触发，onShow 稳定
onShow(() => {
  setNavTitle('journals.title')
  pagination.refresh()
})

function openDetail(c: ContentItem) {
  uni.navigateTo({ url: `/pages/detail/detail?id=${c.id}` })
}

function goPublish() {
  uni.navigateTo({ url: '/pages/publish/publish?kind=journal' })
}

function goSearch() {
  uni.navigateTo({ url: '/pages/search/search?kind=journal' })
}

function summary(c: ContentItem): string {
  return stripMarkdown(c.markdownBody ?? '')
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px 16px calc(70px + env(safe-area-inset-bottom));
  box-sizing: border-box;
}
.container {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
/* 移动端维持原 flex 列布局（桌面在下方切 grid 双栏） */
.topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.topbar-title {
  font-size: 20px;
  font-weight: 700;
  color: var(--hz-color-text);
}
.topbar-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}
.search-btn {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  border: 1px solid var(--hz-color-border);
  background: var(--hz-color-surface);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--hz-color-text-secondary);
}
.search-icon {
  width: 16px;
  height: 16px;
}
.write-btn {
  display: none;
  align-items: center;
  gap: 4px;
  height: 36px;
  padding: 0 18px;
  border-radius: 18px;
  background: var(--hz-color-primary);
  color: #fff;
  font-size: var(--hz-font-size-sm);
}
.write-plus {
  font-size: 16px;
}
.write-btn.empty-cta {
  display: inline-flex;
}
.fab {
  position: fixed;
  right: 20px;
  bottom: calc(70px + env(safe-area-inset-bottom) + 16px);
  width: 56px;
  height: 56px;
  border-radius: 50%;
  background: var(--hz-color-primary);
  color: #fff;
  font-size: 28px;
  line-height: 56px;
  text-align: center;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.18);
  z-index: 10;
}
.cards {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-sm);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.title {
  font-size: 20px;
  font-weight: 700;
  color: var(--hz-color-text);
  line-height: 1.3;
}
.summary {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
  line-height: 1.5;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.date {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.more {
  display: flex;
  justify-content: center;
  padding: 4px 0 12px;
}
.hint {
  padding: 40px 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
}
.hint.empty {
  min-height: 60vh;
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
    padding: 24px;
  }
  .container {
    max-width: 42rem;
    margin: 0 auto;
    width: 100%;
  }
  /* 桌面双栏 grid */
  .cards {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    gap: 16px;
    align-items: start;
  }
  .card {
    padding: 18px 20px;
  }
  .write-btn {
    display: flex;
  }
  .fab {
    display: none;
  }
  .write-btn.empty-cta {
    display: inline-flex;
  }
}
</style>
