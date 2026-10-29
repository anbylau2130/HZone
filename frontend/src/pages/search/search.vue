<template>
  <view class="page">
    <!-- 搜索框 -->
    <view class="gs-input-row">
      <input
        v-model="keyword"
        class="gs-input"
        :placeholder="$t('search2.placeholder')"
        confirm-type="search"
        @confirm="runSearch"
      />
      <view class="gs-btn" @click="runSearch">
        <text class="gs-btn-text">{{ $t('search.button') }}</text>
      </view>
    </view>

    <!-- 类型筛选 -->
    <view class="gs-filter-row">
      <view
        v-for="k in kinds"
        :key="k.value"
        class="gs-chip"
        :class="{ on: kind === k.value }"
        @click="setKind(k.value)"
      >
        <text class="gs-chip-text">{{ k.label }}</text>
      </view>
    </view>

    <!-- 时间 + 标签筛选 -->
    <view class="gs-filter-row">
      <view
        v-for="r in ranges"
        :key="r.value"
        class="gs-chip mini"
        :class="{ on: range === r.value }"
        @click="setRange(r.value)"
      >
        <text class="gs-chip-text">{{ r.label }}</text>
      </view>
      <view v-for="tg in tags" :key="tg" class="gs-chip mini" :class="{ on: tag === tg }" @click="toggleTag(tg)">
        <text class="gs-chip-text">#{{ tg }}</text>
      </view>
    </view>

    <!-- 结果 -->
    <view v-if="!searched && !items.length" class="gs-empty">
      <text class="gs-empty-ico">🔍</text>
      <text class="gs-empty-text">{{ $t('search2.hint') }}</text>
    </view>
    <view v-else-if="searched && !items.length && !loading" class="gs-empty">
      <text class="gs-empty-text">{{ $t('search2.noResults') }}</text>
    </view>

    <view v-for="hit in items" :key="hit.id" class="gs-hit" @click="openHit(hit)">
      <view class="gs-hit-head">
        <text class="gs-badge" :class="hit.type">{{ typeLabel(hit.type) }}</text>
        <text v-if="hit.title" class="gs-title">{{ hit.title }}</text>
      </view>
      <view class="gs-snippet">
        <text
          v-for="(seg, i) in segments(hit.snippet)"
          :key="i"
          class="gs-seg"
          :class="{ kw: seg.isKw }"
        >{{ seg.text }}</text>
      </view>
      <view class="gs-meta">
        <text class="gs-meta-text">{{ hit.author }} · {{ formatDateTime(hit.createdAt) }}</text>
      </view>
    </view>

    <view v-if="items.length && total > items.length" class="gs-more">
      <button class="gs-more-btn" :disabled="loading" @click="loadMoreHits">{{ $t('common.loadMore') }}</button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { useI18n } from 'vue-i18n'
import { searchAll, listMyTags, type SearchAllHit } from '../../api/endpoints'
import { formatDateTime } from '../../utils/format'
import { setNavTitle } from '../../locale'

const { t } = useI18n()

const keyword = ref('')
const kind = ref<'' | 'say' | 'journal' | 'comment'>('')
const range = ref<'any' | '30d' | '1y'>('any')
const tag = ref('')
const tags = ref<string[]>([])
const items = ref<SearchAllHit[]>([])
const total = ref(0)
const loading = ref(false)
const searched = ref(false)

const kinds = [
  { value: '' as const, label: t('search2.all') },
  { value: 'say' as const, label: t('timeline.kindSay') },
  { value: 'journal' as const, label: t('timeline.kindJournal') },
  { value: 'comment' as const, label: t('search2.comment') }
]
const ranges = [
  { value: 'any' as const, label: t('search2.anyTime') },
  { value: '30d' as const, label: t('search2.last30d') },
  { value: '1y' as const, label: t('search2.last1y') }
]

onShow(() => {
  setNavTitle('hub.search')
  // 标签云只拉一次
  if (!tags.value.length) {
    listMyTags()
      .then((r) => (tags.value = r.slice(0, 10).map((x) => x.tag)))
      .catch(() => {})
  }
})

function setKind(k: '' | 'say' | 'journal' | 'comment') {
  if (kind.value === k) return
  kind.value = k
  runSearch()
}

function setRange(r: 'any' | '30d' | '1y') {
  if (range.value === r) return
  range.value = r
  runSearch()
}

function toggleTag(tg: string) {
  tag.value = tag.value === tg ? '' : tg
  runSearch()
}

/** 时间范围 → from（后端按中国区自然日口径换算）。 */
function dateRange(): { from?: string } {
  if (range.value === 'any') return {}
  const days = range.value === '30d' ? 30 : 365
  const d = new Date(Date.now() - days * 86_400_000)
  const pad = (n: number) => String(n).padStart(2, '0')
  return { from: `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}` }
}

async function fetchPage(offset: number) {
  const kw = keyword.value.trim()
  if (!kw) return
  loading.value = true
  try {
    const { from } = dateRange()
    const res = await searchAll({ q: kw, kind: kind.value, tag: tag.value || undefined, from, offset })
    total.value = res.total
    items.value = offset === 0 ? res.items : items.value.concat(res.items)
    searched.value = true
  } catch {
    // 请求层已 toast
  } finally {
    loading.value = false
  }
}

function runSearch() {
  if (!keyword.value.trim()) return
  void fetchPage(0)
}

function loadMoreHits() {
  void fetchPage(items.value.length)
}

/** 片段按关键词切段（首个忽略大小写命中），前端渲染高亮。 */
function segments(snippet: string): { text: string; isKw: boolean }[] {
  const kw = keyword.value.trim()
  if (!kw) return [{ text: snippet, isKw: false }]
  const idx = snippet.indexOf(kw)
  if (idx < 0) return [{ text: snippet, isKw: false }]
  return [
    { text: snippet.slice(0, idx), isKw: false },
    { text: kw, isKw: true },
    { text: snippet.slice(idx + kw.length), isKw: false }
  ].filter((s) => s.text.length > 0)
}

function typeLabel(type: string) {
  return type === 'comment'
    ? t('search2.comment')
    : type === 'journal'
      ? t('timeline.kindJournal')
      : t('timeline.kindSay')
}

/** 命中跳转：定位到对应内容（自己的 → 随记/笔记页；别人的/评论 → 收到页）。 */
function openHit(hit: SearchAllHit) {
  const focus = `focus=${hit.contentId}`
  if (hit.targetKind === 'journal') {
    uni.navigateTo({ url: hit.isMine ? `/pages/journals/journals?${focus}` : `/pages/received/received?${focus}` })
  } else {
    uni.navigateTo({ url: hit.isMine ? `/pages/moments/moments?${focus}` : `/pages/received/received?${focus}` })
  }
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.gs-input-row {
  display: flex;
  gap: 8px;
}
.gs-input {
  flex: 1;
  height: 40px;
  padding: 0 14px;
  border: 1px solid var(--hz-color-primary);
  border-radius: 999px;
  background: var(--hz-color-surface);
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.gs-btn {
  flex-shrink: 0;
  height: 40px;
  padding: 0 18px;
  border-radius: 999px;
  background: var(--hz-color-primary);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.gs-btn-text {
  color: #fff;
  font-size: var(--hz-font-size-sm);
}
.gs-filter-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.gs-chip {
  padding: 3px 12px;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  background: var(--hz-color-surface);
  cursor: pointer;
}
.gs-chip.mini {
  padding: 2px 9px;
}
.gs-chip.on {
  border-color: var(--hz-color-primary);
  background: color-mix(in srgb, var(--hz-color-primary) 10%, transparent);
}
.gs-chip-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
.gs-chip.on .gs-chip-text {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.gs-hit {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  cursor: pointer;
}
.gs-hit-head {
  display: flex;
  align-items: center;
  gap: 8px;
}
.gs-badge {
  flex-shrink: 0;
  font-size: 11px;
  padding: 1px 8px;
  border-radius: 4px;
  color: var(--hz-color-primary);
  border: 1px solid var(--hz-color-primary);
}
.gs-badge.comment {
  color: var(--hz-color-text-secondary);
  border-color: var(--hz-color-border);
}
.gs-title {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.gs-snippet {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
}
.gs-seg {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  line-height: 1.6;
}
.gs-seg.kw {
  color: var(--hz-color-primary);
  font-weight: 700;
  background: color-mix(in srgb, var(--hz-color-primary) 12%, transparent);
  border-radius: 3px;
}
.gs-meta {
  display: flex;
}
.gs-meta-text {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
}
.gs-empty {
  padding: 60px 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
}
.gs-empty-ico {
  font-size: 40px;
}
.gs-empty-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.gs-more {
  display: flex;
  justify-content: center;
  padding: 8px 0 20px;
}
.gs-more-btn {
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
