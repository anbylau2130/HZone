<template>
  <view class="page">
    <view class="container">
      <view class="topbar">
        <text class="topbar-title">{{ $t('moments.title') }}</text>
        <view class="topbar-actions">
          <view v-if="items.length > 0" class="write-btn" @click="goPublish">
            <text class="write-plus">＋</text>
            <text>{{ $t('moments.write') }}</text>
          </view>
          <view class="search-btn" @click="goSearch">
            <svg class="search-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
              <circle cx="11" cy="11" r="7" />
              <line x1="16.5" y1="16.5" x2="21" y2="21" />
            </svg>
          </view>
        </view>
      </view>

      <view v-if="pagination.loading.value && !items.length" class="hint">
        <text class="hint-text">{{ $t('common.loading') }}</text>
      </view>
      <view v-else-if="!items.length" class="hint empty">
        <text class="hint-text">{{ $t('moments.empty') }}</text>
        <view class="write-btn empty-cta" @click="goPublish">
          <text class="write-plus">＋</text>
          <text>{{ $t('moments.write') }}</text>
        </view>
      </view>

      <view
        v-for="c in items"
        :key="c.id"
        class="card"
        @click="openDetail(c)"
      >
        <view class="head">
          <AvatarCircle :nickname="c.author.nickname" :avatar-url="c.author.avatarUrl" :size="44" />
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
          <view class="meta-spacer" />
          <CardOwnerActions v-if="isMine(c)" @edit="goEdit(c)" @delete="remove(c)" />
        </view>
      </view>

      <view v-if="items.length" class="more">
        <text v-if="finished" class="hint-text">{{ $t('common.noMore') }}</text>
        <button v-else class="pick" :disabled="pagination.loading.value" @click="loadMore">
          {{ $t('common.loadMore') }}
        </button>
      </view>
    </view>

    <AppSidebar current="moments" />
    <ToolDock />
    <AppTabBar current="moments" />
    <view class="fab" @click="goPublish">＋</view>
  </view>
</template>

<script setup lang="ts">
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import MediaGrid from '../../components/MediaGrid.vue'
import RecipientsBar from '../../components/RecipientsBar.vue'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import ToolDock from '../../components/ToolDock.vue'
import { usePagination } from '../../composables/usePagination'
import { listMyContents, deleteContent, type ContentItem } from '../../api/endpoints'
import { formatRelativeTime } from '../../utils/format'
import CardOwnerActions from '../../components/CardOwnerActions.vue'
import AvatarCircle from '../../components/AvatarCircle.vue'
import { useAuthStore } from '../../stores/auth'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const auth = useAuthStore()

const pagination = usePagination<ContentItem>((offset, limit) => listMyContents('say', offset, limit))
const { items, finished, loadMore } = pagination

// 作者本人可见编辑/删除图标；他人内容只有评论入口
function isMine(c: ContentItem) {
  return c.author.id === auth.user?.id
}

function goEdit(c: ContentItem) {
  uni.navigateTo({ url: '/pages/detail/detail?id=' + c.id + '&edit=1' })
}

function remove(c: ContentItem) {
  uni.showModal({
    title: t('common.delete'),
    content: t('detail.deleteConfirm'),
    success: async (res) => {
      if (!res.confirm) return
      try {
        await deleteContent(c.id)
        uni.showToast({ title: t('detail.deleted'), icon: 'success' })
        items.value = items.value.filter((x) => x.id !== c.id)
      } catch {
        // 错误提示由请求层统一 toast
      }
    }
  })
}

// onShow 而非 onLoad：H5 端直接 hash 进入时 onLoad 不触发（timeline 同款坑），onShow 稳定
onShow(() => {
  setNavTitle('moments.title')
  pagination.refresh()
})

function openDetail(c: ContentItem) {
  uni.navigateTo({ url: `/pages/detail/detail?id=${c.id}` })
}

function goPublish() {
  uni.navigateTo({ url: '/pages/publish/publish?kind=say' })
}

function goSearch() {
  uni.navigateTo({ url: '/pages/search/search?kind=say' })
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
  display: flex;
  align-items: center;
  gap: 10px;
}
.foot-btn {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
}
.meta-spacer {
  flex: 1;
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
    max-width: 48rem;
    margin: 0 auto;
    width: 100%;
  }
  .card {
    padding: 20px;
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
/* ≥1200 三栏壳：右侧为 300px 工具 dock 留位，容器在剩余空间居中 */
@media (min-width: 1200px) {
  .page {
    margin-right: 300px;
  }
}
</style>
