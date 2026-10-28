<template>
  <view class="page">
    <!-- 工具行：类型筛选 + 搜索格；搜索态输入框接管整行（与笔记页同款交互） -->
    <view v-if="searchOpen" class="search-inline">
      <svg class="si-ico" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round">
        <circle cx="9" cy="9" r="5.5" />
        <path d="M13.2 13.2L17.5 17.5" />
      </svg>
      <input
        v-model="keyword"
        class="si-input"
        :placeholder="$t('search.placeholder')"
        confirm-type="search"
        @confirm="refreshNow"
      />
      <text v-if="keyword" class="si-clear" @click="keyword = ''">×</text>
      <text class="si-cancel" @click="closeSearch">{{ $t('common.cancel') }}</text>
    </view>
    <view v-else class="tool-row">
      <view class="filter-wrap">
        <SegmentedFilter :model-value="kind" :options="kinds" @update:model-value="setKind" />
      </view>
      <view v-if="items.length > 0 || keyword" class="search-cell" :title="$t('search.button')" @click="toggleSearch">
        <svg class="si-ico" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round">
          <circle cx="9" cy="9" r="5.5" />
          <path d="M13.2 13.2L17.5 17.5" />
        </svg>
      </view>
    </view>

    <!-- 站内通知入口：tab 角标数的是这里（被评论/被@/好友请求）；未读>0 才出现 -->
    <view v-if="inbox.unreadCount > 0" class="inbox-entry" @click="goInbox">
      <text class="inbox-entry-ico">🔔</text>
      <text class="inbox-entry-label">{{ $t('inbox.title') }}</text>
      <text class="inbox-entry-count">{{ $t('inbox.unreadCount', { n: inbox.unreadCount }) }}</text>
      <text class="inbox-entry-arrow">›</text>
    </view>

    <view v-if="pagination.loading.value && !items.length" class="hint">
      <text class="hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="!items.length" class="hint">
      <text class="hint-text">{{ $t('common.empty') }}</text>
    </view>

    <ContentCard v-for="c in items" :id="'c-' + c.id" :key="c.id" :content="c" @deleted="onCardDeleted" />

    <!-- 滚动近底部自动加载（useInfiniteScroll），结束后只留提示 -->
    <view v-if="items.length && finished" class="more">
      <text class="hint-text">{{ $t('common.noMore') }}</text>
    </view>

    <AppSidebar current="received" />
    <ToolDock />
    <MiniPlayer />
    <AppTabBar current="received" />
  </view>
</template>

<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import { onLoad, onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import ContentCard from '../../components/ContentCard.vue'
import SegmentedFilter from '../../components/SegmentedFilter.vue'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import ToolDock from '../../components/ToolDock.vue'
import MiniPlayer from '../../components/MiniPlayer.vue'
import { usePagination } from '../../composables/usePagination'
import { useInfiniteScroll } from '../../composables/useInfiniteScroll'
import { listReceived, type ContentItem, type KindFilter } from '../../api/endpoints'
import { useInboxStore } from '../../stores/inbox'
import { focusContentCard } from '../../utils/focusContent'

const inbox = useInboxStore()

/** 站内通知页（孤儿页修复：收到页是角标所在地，入口必须在这里）。 */
function goInbox() {
  uni.navigateTo({ url: '/pages/inbox/inbox' })
}

const kinds: { value: KindFilter; label: string }[] = [
  { value: '', label: 'timeline.filterAll' },
  { value: 'say', label: 'timeline.kindSay' },
  { value: 'journal', label: 'timeline.kindJournal' }
]

const kind = ref<KindFilter>('')

// ---------- 页内检索：q 走 /me/received 服务端过滤（随记文本/笔记标题+正文） ----------
const searchOpen = ref(false)
const keyword = ref('')

const pagination = usePagination<ContentItem>((offset, limit) =>
  listReceived(kind.value, offset, limit, keyword.value.trim() || undefined)
)
const { items, finished, loadMore } = pagination

function toggleSearch() {
  searchOpen.value = true
  nextTick(() => {
    document.querySelector('.si-input input')?.focus?.()
  })
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

// 滚动近底部自动加载；回调防重入由 usePagination 短路（loading/finished 已判断）
useInfiniteScroll(() => {
  if (!finished.value && !pagination.loading.value) loadMore()
})

onLoad((query) => {
  setNavTitle('hub.received')
  const q = (query ?? {}) as Record<string, string>
  if (q.kind === 'say' || q.kind === 'journal') {
    kind.value = q.kind
  }
  pagination.refresh()
})

// 站内信跳转定位：?focus=内容id → 滚到该卡高亮闪两下（onShow 稳定读取，只消费一次）
let focusHandled = false
function handleFocusQuery() {
  if (focusHandled) return
  const pages = getCurrentPages()
  const focusId = (pages[pages.length - 1] as { options?: Record<string, string> })?.options?.focus
  if (!focusId) return
  focusHandled = true
  const unwatch = watch(items, (list) => {
    if (!list.length) return
    unwatch()
    focusContentCard(focusId)
  })
  setTimeout(() => focusContentCard(focusId), 800)
}
onShow(() => handleFocusQuery())

function setKind(k: string) {
  const next = k as KindFilter
  if (kind.value === next) return
  kind.value = next
  pagination.refresh()
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
  padding: 16px 16px calc(70px + env(safe-area-inset-bottom));
  display: flex;
  flex-direction: column;
  gap: 12px;
  box-sizing: border-box;
}
/* 工具行：筛选左、搜索格右；搜索态由 search-inline 接管整行 */
.tool-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.filter-wrap {
  flex: 1;
  min-width: 0;
}
.search-cell {
  width: 32px;
  height: 32px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  cursor: pointer;
}
/* 站内通知入口：未读>0 时出现的引导行 */
.inbox-entry {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  border: 1px solid var(--hz-color-primary);
  border-radius: var(--hz-radius-md);
  background: var(--hz-color-surface);
  cursor: pointer;
}
.inbox-entry-ico {
  font-size: 15px;
  line-height: 1;
}
.inbox-entry-label {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.inbox-entry-count {
  flex: 1;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
}
.inbox-entry-arrow {
  font-size: 18px;
  color: var(--hz-color-text-secondary);
  line-height: 1;
}
.search-inline {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 32px;
  border: 1px solid var(--hz-color-primary);
  border-radius: 999px;
  padding: 0 6px 0 12px;
  background: var(--hz-color-surface);
}
.si-ico {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
}
.search-cell .si-ico {
  color: var(--hz-color-text-secondary);
}
.search-inline .si-ico {
  color: var(--hz-color-text-secondary);
}
.si-input {
  flex: 1;
  min-width: 0;
  height: 100%;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  background: transparent;
}
.si-clear {
  width: 20px;
  height: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: var(--hz-color-bg);
  font-size: 13px;
  color: var(--hz-color-text-secondary);
  flex-shrink: 0;
  cursor: pointer;
}
.si-cancel {
  flex-shrink: 0;
  padding: 0 6px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  cursor: pointer;
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
@media (min-width: 768px) {
  .page {
    margin-left: 220px;
    padding-left: max(16px, calc((100% - 220px - 48rem) / 2));
    padding-right: max(16px, calc((100% - 220px - 48rem) / 2));
  }
}
/* ≥1200 三栏壳：右侧为 300px 工具 dock 留位，内容在侧栏与 dock 左侧居中 */
@media (min-width: 1200px) {
  .page {
    margin-right: 300px;
    padding-left: max(16px, calc((100% - 220px - 300px - 48rem) / 2));
    padding-right: max(16px, calc((100% - 220px - 300px - 48rem) / 2));
  }
}
</style>
