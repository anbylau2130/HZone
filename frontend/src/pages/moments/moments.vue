<template>
  <view class="page">
    <view class="container">
      <view class="topbar">
        <text class="topbar-title">{{ $t('moments.title') }}</text>
        <view class="topbar-actions">
          <view v-if="items.length > 0" class="write-btn" @click="focusComposer">
            <text class="write-plus">＋</text>
            <text>{{ $t('moments.write') }}</text>
          </view>
          <!-- 搜索在页内完成（不再跳搜索页） -->
          <view class="search-btn" @click="toggleSearch">
            <svg class="search-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
              <circle cx="11" cy="11" r="7" />
              <line x1="16.5" y1="16.5" x2="21" y2="21" />
            </svg>
          </view>
        </view>
      </view>

      <!-- 页内搜索条：输入即筛（400ms 防抖），清空/关闭恢复全部 -->
      <view v-if="searchOpen" class="search-row">
        <input
          v-model="keyword"
          class="search-field"
          :placeholder="$t('search.placeholder')"
          confirm-type="search"
          @confirm="refreshNow"
        />
        <view v-if="keyword" class="search-clear" @click="keyword = ''">
          <text class="search-clear-txt">×</text>
        </view>
        <view class="search-close" @click="closeSearch">
          <text class="search-close-txt">{{ $t('common.cancel') }}</text>
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

      <!-- 页内快速写说说（不跳 publish）：文本+表情+图片，发布后直接进下方信息流 -->
      <SayComposer ref="composerRef" @published="onPublished" />

      <view v-if="pagination.loading.value && !items.length" class="hint">
        <text class="hint-text">{{ $t('common.loading') }}</text>
      </view>
      <!-- 搜索无结果：只提示，不给写随记引导 -->
      <view v-else-if="!items.length && keyword.trim()" class="hint">
        <text class="hint-text">{{ $t('common.empty') }}</text>
      </view>
      <view v-else-if="!items.length" class="hint empty">
        <text class="hint-text">{{ $t('moments.empty') }}</text>
        <view class="write-btn empty-cta" @click="focusComposer">
          <text class="write-plus">＋</text>
          <text>{{ $t('moments.write') }}</text>
        </view>
      </view>

      <view v-for="g in groups" :key="g.key" class="day-group">
        <view class="date-col">
          <text class="date-label">{{ g.label }}</text>
        </view>
        <view class="items-col">
          <!-- 正文与整条点击均不跳转（朋友圈式就地阅读）；评论走底部入口展开 -->
          <view v-for="c in g.items" :key="c.id" class="entry">
            <view class="entry-head">
              <text class="time">{{ formatRelativeTime(c.createdAt) }}</text>
              <view class="head-spacer" />
              <CardOwnerActions v-if="isMine(c)" @edit="goEdit(c)" @delete="remove(c)" @share="openShare(c)" />
            </view>

            <!-- 原地编辑：✎ 后该条变成编辑器（不跳 publish 页） -->
            <view v-if="editingId === c.id" class="entry-edit" @click.stop>
              <SayComposer :edit="c" @updated="onSayUpdated" @cancel="editingId = ''" />
            </view>

            <template v-else>
              <text class="text">{{ c.text }}</text>
              <MediaGrid v-if="(c.media ?? []).length" :media="c.media" />
              <RecipientsBar :recipients="c.recipients" />
              <view class="entry-foot">
                <!-- 详情页已移除：点击切换内联评论区（同 ContentCard ①A） -->
                <text class="foot-btn" @click.stop="expandedId = expandedId === c.id ? '' : c.id">
                  💬 {{ $t('detail.commentCount', { n: c.commentCount }) }}
                </text>
                <view class="head-spacer" />
                <view v-if="weatherChip(c)" class="weather-chip">
                  <text class="weather-chip-text">{{ weatherChip(c) }}</text>
                </view>
              </view>
              <!-- 内联评论区：展开才挂载，收起即卸载；评论数变化直接回写该条 -->
              <CardComments
                v-if="expandedId === c.id"
                class="entry-comments"
                :content-id="c.id"
                @count-change="(n) => onCommentCount(c, n)"
              />
            </template>
          </view>
        </view>
      </view>

      <!-- 滚动近底部自动加载（useInfiniteScroll），结束后只留提示 -->
      <view v-if="items.length && finished" class="more">
        <text class="hint-text">{{ $t('common.noMore') }}</text>
      </view>
    </view>

    <AppSidebar current="moments" />
    <ToolDock />
    <MiniPlayer />
    <AppTabBar current="moments" />
    <view class="fab" @click="focusComposer">＋</view>
    <ShareSheet v-model:visible="shareVisible" :content-id="shareId" />
  </view>
</template>

<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import MediaGrid from '../../components/MediaGrid.vue'
import RecipientsBar from '../../components/RecipientsBar.vue'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import ToolDock from '../../components/ToolDock.vue'
import MiniPlayer from '../../components/MiniPlayer.vue'
import ProfileHeader from '../../components/ProfileHeader.vue'
import CardOwnerActions from '../../components/CardOwnerActions.vue'
import ShareSheet from '../../components/ShareSheet.vue'
import CardComments from '../../components/CardComments.vue'
import SayComposer from '../../components/SayComposer.vue'
import { usePagination } from '../../composables/usePagination'
import { useInfiniteScroll } from '../../composables/useInfiniteScroll'
import {
  listMyContents,
  searchContents,
  deleteContent,
  uploadCover,
  type ContentItem
} from '../../api/endpoints'
import { formatRelativeTime, groupByCnDate } from '../../utils/format'
import { weatherChipText } from '../../utils/weather'
import { useAuthStore } from '../../stores/auth'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const auth = useAuthStore()

// ---------- 页内搜索（不跳搜索页）：关键词走 /search（自己的随记），空=全部 ----------
const searchOpen = ref(false)
const keyword = ref('')

const pagination = usePagination<ContentItem>((offset, limit) => {
  const kw = keyword.value.trim()
  return kw ? searchContents(kw, 'say', offset, limit) : listMyContents('say', offset, limit)
})
const { items, finished, loadMore } = pagination

// 滚动近底部自动加载；回调防重入由 usePagination 短路（loading/finished 已判断）
useInfiniteScroll(() => {
  if (!finished.value && !pagination.loading.value) loadMore()
})

function toggleSearch() {
  searchOpen.value = !searchOpen.value
  if (searchOpen.value) {
    nextTick(() => {
      document.querySelector('.search-field input')?.focus?.()
    })
  }
}

function closeSearch() {
  searchOpen.value = false
  if (keyword.value) {
    keyword.value = ''
    pagination.refresh()
  }
}

function refreshNow() {
  pagination.refresh()
}

// 输入防抖 400ms 即时筛选；清空恢复全部
let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(keyword, () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => pagination.refresh(), 400)
})

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

// 内联评论区展开态：同一时刻至多展开一条（存内容 id，收起置空）
const expandedId = ref('')

// 发送评论成功：把最新评论数回写该条（卡片「💬 n」同步）
function onCommentCount(c: ContentItem, n: number) {
  c.commentCount = n
}

// ✎ 原地编辑当前条（不跳 publish）；日志类仍去 publish（cherry 编辑器）
const editingId = ref('')

// 分享外链（ShareSheet 共享组件，与 ContentCard 同款）
const shareVisible = ref(false)
const shareId = ref('')

function openShare(c: ContentItem) {
  shareId.value = c.id
  shareVisible.value = true
}

function goEdit(c: ContentItem) {
  editingId.value = c.id
}

// 原地保存成功：本地替换该条内容
function onSayUpdated(updated: ContentItem) {
  items.value = items.value.map((x) => (x.id === updated.id ? updated : x))
  editingId.value = ''
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

// 写随记入口：滚到页内编辑器并聚焦（不再跳 publish 页）
const composerRef = ref<InstanceType<typeof SayComposer> | null>(null)

function focusComposer() {
  uni.pageScrollTo({ scrollTop: 0, duration: 200 })
  composerRef.value?.focus()
}

// 快速发布成功：插到信息流最前（搜索态下不插——结果集由关键词决定）
function onPublished(c: ContentItem) {
  if (!keyword.value.trim()) items.value = [c, ...items.value]
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
/* 页内搜索条 */
.search-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.search-field {
  flex: 1;
  height: 36px;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  padding: 0 14px;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  background: var(--hz-color-surface);
}
.search-clear {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: var(--hz-color-bg);
  cursor: pointer;
  flex-shrink: 0;
}
.search-clear-txt {
  font-size: 16px;
  color: var(--hz-color-text-secondary);
}
.search-close {
  flex-shrink: 0;
  padding: 4px 10px;
  cursor: pointer;
}
.search-close-txt {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
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
}
.entry:last-child {
  border-bottom: none;
  padding-bottom: 0;
}
.entry-edit {
  margin-top: 6px;
}
/* 内联评论区与正文留一档间距 */
.entry-comments {
  margin-top: 8px;
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
  /* 随记正文 16px（比基准大一档）；可选中复制（悬停显示文本光标而非手型） */
  font-size: 16px;
  color: var(--hz-color-text);
  white-space: pre-wrap;
  word-break: break-word;
  cursor: auto;
  user-select: text;
  -webkit-user-select: text;
}
.entry-foot {
  margin-top: 6px;
  display: flex;
  align-items: center;
  gap: 8px;
}
.foot-btn {
  cursor: pointer;
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
