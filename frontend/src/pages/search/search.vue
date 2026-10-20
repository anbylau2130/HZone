<template>
  <view class="page">
    <view class="search-row">
      <input
        v-model="query"
        class="field"
        :placeholder="$t('search.placeholder')"
        confirm-type="search"
        @confirm="doSearch"
      />
      <button class="pick" :disabled="!query.trim()" @click="doSearch">
        {{ $t('search.button') }}
      </button>
    </view>

    <view class="filters">
      <button
        v-for="k in kinds"
        :key="k.value"
        class="filter"
        :class="{ on: kind === k.value }"
        @click="setKind(k.value)"
      >
        {{ $t(k.label) }}
      </button>
    </view>

    <view v-if="searched && pagination.loading.value && !items.length" class="hint">
      <text class="hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="searched && !items.length" class="hint">
      <text class="hint-text">{{ $t('common.empty') }}</text>
    </view>

    <ContentCard v-for="c in items" :key="c.id" :content="c" @open="openDetail" />

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
import ContentCard from '../../components/ContentCard.vue'
import { usePagination } from '../../composables/usePagination'
import { searchContents, type ContentItem, type KindFilter } from '../../api/endpoints'
import { onShow, onLoad } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'

onShow(() => {
  setNavTitle('hub.search')
})

const kinds: { value: KindFilter; label: string }[] = [
  { value: '', label: 'timeline.filterAll' },
  { value: 'say', label: 'timeline.kindSay' },
  { value: 'journal', label: 'timeline.kindJournal' }
]

const query = ref('')
const kind = ref<KindFilter>('')
const searched = ref(false)

onLoad((opts) => {
  const k = opts?.['kind']
  if (k === 'say' || k === 'journal') kind.value = k
})

const pagination = usePagination<ContentItem>((offset, limit) =>
  searchContents(query.value.trim(), kind.value, offset, limit)
)
const { items, finished, loadMore } = pagination

function doSearch() {
  if (!query.value.trim()) return
  searched.value = true
  pagination.refresh()
}

function setKind(k: KindFilter) {
  if (kind.value === k) return
  kind.value = k
  if (searched.value) {
    pagination.refresh()
  }
}

function openDetail(c: ContentItem) {
  uni.navigateTo({ url: `/pages/detail/detail?id=${c.id}` })
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
.search-row {
  display: flex;
  gap: 8px;
}
.field {
  flex: 1;
  height: 40px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 12px;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  background: var(--hz-color-surface);
}
.filters {
  display: flex;
  gap: 8px;
}
.filter {
  flex: 1;
  height: 36px;
  line-height: 36px;
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  border: 1px solid var(--hz-color-border);
}
.filter.on {
  background: var(--hz-color-primary);
  color: #fff;
  border-color: var(--hz-color-primary);
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
  height: 38px;
  line-height: 38px;
  padding: 0 18px;
  background: var(--hz-color-surface);
  color: var(--hz-color-primary);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  border: 1px solid var(--hz-color-border);
}
</style>
