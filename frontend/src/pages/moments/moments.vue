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

      <ProfileHeader
        is-self
        :nickname="auth.user?.nickname ?? ''"
        :avatar-url="auth.user?.avatarUrl"
        :signature="auth.user?.signature"
        :cover-url="auth.user?.coverUrl"
        @cover-pick="pickCover"
      />

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

      <view v-for="g in groups" :key="g.key" class="day-group">
        <view class="date-col">
          <text class="date-label">{{ g.label }}</text>
        </view>
        <view class="items-col">
          <view v-for="c in g.items" :key="c.id" class="entry" @click="openDetail(c)">
            <view class="entry-head">
              <text class="time">{{ formatRelativeTime(c.createdAt) }}</text>
              <view class="head-spacer" />
              <CardOwnerActions v-if="isMine(c)" @edit="goEdit(c)" @delete="remove(c)" />
            </view>
            <text class="text">{{ c.text }}</text>
            <MediaGrid v-if="(c.media ?? []).length" :media="c.media" />
            <RecipientsBar :recipients="c.recipients" />
            <view class="entry-foot">
              <text class="foot-btn">{{ $t('detail.commentCount', { n: c.commentCount }) }}</text>
              <view class="head-spacer" />
              <view v-if="weatherChip(c)" class="weather-chip">
                <text class="weather-chip-text">{{ weatherChip(c) }}</text>
              </view>
            </view>
          </view>
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
import { computed } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import MediaGrid from '../../components/MediaGrid.vue'
import RecipientsBar from '../../components/RecipientsBar.vue'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import ToolDock from '../../components/ToolDock.vue'
import ProfileHeader from '../../components/ProfileHeader.vue'
import CardOwnerActions from '../../components/CardOwnerActions.vue'
import { usePagination } from '../../composables/usePagination'
import { listMyContents, deleteContent, uploadCover, type ContentItem } from '../../api/endpoints'
import { formatRelativeTime, groupByCnDate } from '../../utils/format'
import { weatherChipText } from '../../utils/weather'
import { useAuthStore } from '../../stores/auth'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const auth = useAuthStore()

const pagination = usePagination<ContentItem>((offset, limit) => listMyContents('say', offset, limit))
const { items, finished, loadMore } = pagination

// 中国区日期倒序分组（moments-wechat 票 02）
const groups = computed(() => groupByCnDate(items.value))

// 作者本人内容才有编辑/删除；此处恒真（listMyContents 只返回自己的）
function isMine(c: ContentItem) {
  return c.author.id === auth.user?.id
}

// 天气 chip 文案（现象 i18n 回退原文 + 温度 + 城市）
function weatherChip(c: ContentItem) {
  return weatherChipText(c.weather ?? null)
}

function goEdit(c: ContentItem) {
  // 日志：直达 publish 编辑页（与创建同一页面）；随记：detail 内联改短文本
  const url =
    c.kind === 'journal'
      ? '/pages/publish/publish?id=' + c.id + '&kind=journal'
      : '/pages/detail/detail?id=' + c.id + '&edit=1'
  uni.navigateTo({ url })
}

function remove(c: ContentItem) {
  uni.showModal({
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
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
  // 封面/签名可能在设置页改过，回页同步最新资料
  auth.fetchMe()
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

// 封面上传（仅本人可点）：H5 原生 input[type=file]（同设置页头像模式），App 走 uni.chooseImage
function pickCover() {
  // #ifdef H5
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = 'image/*'
  input.onchange = () => {
    const file = input.files?.[0]
    if (file) uploadCoverFile(file)
  }
  input.click()
  // #endif
  // #ifndef H5
  uni.chooseImage({
    count: 1,
    success: (res) => {
      uploadCoverFile(res.tempFilePaths[0])
    }
  })
  // #endif
}

async function uploadCoverFile(path: string | Blob) {
  try {
    await uploadCover(path)
    await auth.fetchMe()
    uni.showToast({ title: t('settings.coverUpdated'), icon: 'success' })
  } catch {
    // 错误提示由请求层统一 toast
  }
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px 0 calc(70px + env(safe-area-inset-bottom));
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
  padding: 0 16px;
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
.day-group {
  display: flex;
  gap: 12px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 14px;
}
.date-col {
  width: 72px;
  flex-shrink: 0;
}
.date-label {
  font-size: 14px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.items-col {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.entry {
  border-bottom: 1px solid var(--hz-color-border);
  padding-bottom: 12px;
  cursor: pointer;
}
.entry:last-child {
  border-bottom: none;
  padding-bottom: 0;
}
.entry-head {
  display: flex;
  align-items: center;
  gap: 8px;
}
.head-spacer {
  flex: 1;
}
.time {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.text {
  margin-top: 4px;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.entry-foot {
  margin-top: 6px;
  display: flex;
  align-items: center;
  gap: 8px;
}
.foot-btn {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
}
.weather-chip {
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--hz-color-bg);
  border: 1px solid var(--hz-color-border);
}
.weather-chip-text {
  font-size: 12px;
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
    padding: 24px 0;
  }
  .container {
    max-width: 48rem;
    margin: 0 auto;
    width: 100%;
  }
  .day-group {
    padding: 20px;
  }
  .write-btn {
    display: flex;
  }
  .fab {
    display: none;
  }
}
@media (min-width: 1200px) {
  .page {
    margin-right: 300px;
  }
}
</style>
