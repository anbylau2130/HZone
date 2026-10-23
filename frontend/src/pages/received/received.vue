<template>
  <view class="page">
    <SegmentedFilter :model-value="kind" :options="kinds" @update:model-value="setKind" />

    <view v-if="pagination.loading.value && !items.length" class="hint">
      <text class="hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="!items.length" class="hint">
      <text class="hint-text">{{ $t('common.empty') }}</text>
    </view>

    <ContentCard v-for="c in items" :key="c.id" :content="c" @open="openDetail" @deleted="onCardDeleted" />

    <!-- 滚动近底部自动加载（useInfiniteScroll），结束后只留提示 -->
    <view v-if="items.length && finished" class="more">
      <text class="hint-text">{{ $t('common.noMore') }}</text>
    </view>

    <AppSidebar current="received" />
    <ToolDock />
    <AppTabBar current="received" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import ContentCard from '../../components/ContentCard.vue'
import SegmentedFilter from '../../components/SegmentedFilter.vue'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import ToolDock from '../../components/ToolDock.vue'
import { usePagination } from '../../composables/usePagination'
import { useInfiniteScroll } from '../../composables/useInfiniteScroll'
import { listReceived, type ContentItem, type KindFilter } from '../../api/endpoints'

const kinds: { value: KindFilter; label: string }[] = [
  { value: '', label: 'timeline.filterAll' },
  { value: 'say', label: 'timeline.kindSay' },
  { value: 'journal', label: 'timeline.kindJournal' }
]

const kind = ref<KindFilter>('')

const pagination = usePagination<ContentItem>((offset, limit) =>
  listReceived(kind.value, offset, limit)
)
const { items, finished, loadMore } = pagination

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

function setKind(k: string) {
  const next = k as KindFilter
  if (kind.value === next) return
  kind.value = next
  pagination.refresh()
}

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
  padding: 16px 16px calc(70px + env(safe-area-inset-bottom));
  display: flex;
  flex-direction: column;
  gap: 12px;
  box-sizing: border-box;
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
