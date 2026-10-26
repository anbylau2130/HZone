<template>
  <view class="page">
    <view class="container">
      <view class="topbar">
        <text class="topbar-title">{{ $t('journals.title') }}</text>
        <view class="topbar-actions">
          <!-- 搜索态：输入框原地接管按钮区（检索时切视图无意义），取消收回 -->
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
          <!-- 单 pill 工具组：视图三格（图标）+ 分隔 + 搜索格；title/aria 保语义，data-view 供 e2e -->
          <view v-else-if="items.length > 0 || keyword" class="toolbar-pill">
            <view
              class="vt"
              :class="{ on: viewMode === 'read' }"
              data-view="read"
              role="tab"
              :aria-label="$t('journals.viewRead')"
              :title="$t('journals.viewRead')"
              @click="setViewMode('read')"
            >
              <svg class="vt-ico" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round">
                <path d="M3 4.5h9M3 8h14M3 11.5h14M3 15h8" />
              </svg>
            </view>
            <view
              class="vt"
              :class="{ on: viewMode === 'card' }"
              data-view="card"
              role="tab"
              :aria-label="$t('journals.viewCard')"
              :title="$t('journals.viewCard')"
              @click="setViewMode('card')"
            >
              <svg class="vt-ico" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round">
                <rect x="3" y="3.5" width="6" height="6" rx="1.4" />
                <rect x="11" y="3.5" width="6" height="6" rx="1.4" />
                <rect x="3" y="10.5" width="6" height="6" rx="1.4" />
                <rect x="11" y="10.5" width="6" height="6" rx="1.4" />
              </svg>
            </view>
            <view
              class="vt"
              :class="{ on: viewMode === 'table' }"
              data-view="table"
              role="tab"
              :aria-label="$t('journals.viewTable')"
              :title="$t('journals.viewTable')"
              @click="setViewMode('table')"
            >
              <svg class="vt-ico" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round">
                <rect x="3" y="4" width="14" height="12" rx="1.4" />
                <path d="M3 10h14M9.5 4v12" />
              </svg>
            </view>
            <view class="pill-sep" />
            <view class="vt search" :title="$t('search.button')" :aria-label="$t('search.button')" @click="toggleSearch">
              <svg class="vt-ico" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round">
                <circle cx="9" cy="9" r="5.5" />
                <path d="M13.2 13.2L17.5 17.5" />
              </svg>
            </view>
          </view>
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

      <view v-if="items.length && viewMode === 'card'" class="cards">
        <!-- 三段式卡：标题 / 日期+天气；整卡点击直达 publish 编辑页（用户要求，与表格视图「编辑」同目标） -->
        <view v-for="c in items" :key="c.id" class="card" @click="editJournal(c)">
          <text class="title">{{ c.title }}</text>
          <view class="meta">
            <text class="date">{{ formatDateTime(c.createdAt) }}</text>
            <view v-if="weatherChip(c)" class="weather">
              <text class="weather-text">{{ weatherChip(c) }}</text>
            </view>
          </view>
        </view>
      </view>

      <!-- 表格视图：行点击不跳转（②C，详情页已移除）；「编辑」直达 publish 编辑页；字数 ≥1000 用 k 单位 -->
      <view v-if="items.length && viewMode === 'table'" class="table">
        <view class="trow thead">
          <text class="tcell c-title">{{ $t('journals.colTitle') }}</text>
          <text class="tcell c-date">{{ $t('journals.colDate') }}</text>
          <text class="tcell c-words">{{ $t('journals.colWords') }}</text>
          <text class="tcell c-op">{{ $t('journals.colActions') }}</text>
        </view>
        <view v-for="c in tableItems" :key="c.id" class="trow">
          <text class="tcell c-title">{{ c.title }}</text>
          <text class="tcell c-date">{{ formatDateTime(c.createdAt) }}</text>
          <text class="tcell c-words">{{ formatWords(wordCount(c)) }}</text>
          <view class="tcell c-op" @click.stop>
            <text class="op" @click="editJournal(c)">{{ $t('common.edit') }}</text>
          </view>
        </view>
      </view>

      <!-- 阅读视图：连续信息流（用户决策），滚动加载替代翻页（JournalReader 自带） -->
      <JournalReader
        v-if="items.length && viewMode === 'read'"
        :items="items"
        :finished="finished"
        :loading="pagination.loading.value"
        @load-more="loadMore"
      />

      <!-- 卡片视图滚动近底部自动加载（useInfiniteScroll），结束后只留提示；
           表格视图走下方经典分页；阅读视图由 JournalReader 自带滚动加载 -->
      <view v-if="items.length && viewMode === 'card' && finished" class="more">
        <text class="hint-text">{{ $t('common.noMore') }}</text>
      </view>

      <!-- 表格视图经典分页：上一页 / 页码 / 下一页（表格不参与滚动加载） -->
      <view v-if="items.length && viewMode === 'table'" class="pager">
        <button
          class="pager-btn"
          :disabled="tablePage <= 1 || tableLoading"
          @click="goTablePage(tablePage - 1)"
        >
          {{ $t('journals.prevPage') }}
        </button>
        <text class="pager-label">{{ $t('journals.pageOf', { x: tablePage, y: tablePages }) }}</text>
        <button
          class="pager-btn"
          :disabled="tablePage >= tablePages || tableLoading"
          @click="goTablePage(tablePage + 1)"
        >
          {{ $t('journals.nextPage') }}
        </button>
      </view>
    </view>

    <AppSidebar current="journals" />
    <ToolDock />
    <MiniPlayer />
    <AppTabBar current="journals" />
    <view class="fab" @click="goPublish">＋</view>
  </view>
</template>

<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import ToolDock from '../../components/ToolDock.vue'
import MiniPlayer from '../../components/MiniPlayer.vue'
import JournalReader from '../../components/JournalReader.vue'
import { usePagination } from '../../composables/usePagination'
import { useInfiniteScroll } from '../../composables/useInfiniteScroll'
import { listMyContents, searchContents, type ContentItem } from '../../api/endpoints'
import { formatDateTime, stripMarkdown } from '../../utils/format'
import { weatherChipText } from '../../utils/weather'

// ---------- 页内检索（与随记页同款）：关键词走 /search（自己的笔记），空=全部 ----------
const searchOpen = ref(false)
const keyword = ref('')

const pagination = usePagination<ContentItem>((offset, limit) => {
  const kw = keyword.value.trim()
  return kw ? searchContents(kw, 'journal', offset, limit) : listMyContents('journal', offset, limit)
})
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

// 输入防抖 400ms 即时筛选（三视图共用）；清空恢复全部
let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(keyword, () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    pagination.refresh()
    // 表格视图数据流独立（经典分页），关键词变化同样回到第 1 页
    if (viewMode.value === 'table') fetchTablePage(1)
  }, 400)
})

// cherry 编辑器懒加载：模块顶层异步组件（同 JournalReader，vite 自动拆 chunk；
// 读侧正文渲染统一走 cherry previewOnly）


// 卡片视图滚动近底部自动加载；表格/阅读视图不触发（阅读由 JournalReader 自带滚动加载）
useInfiniteScroll(() => {
  if (viewMode.value !== 'card') return
  if (!finished.value && !pagination.loading.value) loadMore()
})

// onShow 而非 onLoad：H5 端直接 hash 进入时 onLoad 不触发，onShow 稳定
onShow(() => {
  setNavTitle('journals.title')
  pagination.refresh()
  // 表格视图有独立分页状态：编辑保存回来后刷新当前页（首次进入由 watch(viewMode) 负责）
  if (viewMode.value === 'table' && tableItems.value.length) fetchTablePage(tablePage.value)
})

// ---------- 视图切换：阅读（默认）/卡片/表格，localStorage 记忆 ----------
const VIEW_KEY = 'hz-journals-view'
const savedView = localStorage.getItem(VIEW_KEY)
const viewMode = ref<'card' | 'table' | 'read'>(
  savedView === 'card' || savedView === 'table' ? savedView : 'read'
)

function setViewMode(mode: 'card' | 'table' | 'read') {
  viewMode.value = mode
  localStorage.setItem(VIEW_KEY, mode)
  // 切到表格视图拉第 1 页（表格走经典分页，独立状态，与滚动加载流互不干扰）
  if (mode === 'table') fetchTablePage(1)
}

// ---------- 表格视图经典分页：独立 page/total/items 状态（复用 listMyContents
// fetcher，offset=(page-1)*pageSize），与卡片/阅读的 usePagination 滚动加载流互不干扰 ----------
const TABLE_PAGE_SIZE = 20
const tablePage = ref(1)
const tableTotal = ref(0)
const tableItems = ref<ContentItem[]>([])
const tableLoading = ref(false)
const tablePages = computed(() => Math.max(1, Math.ceil(tableTotal.value / TABLE_PAGE_SIZE)))

async function fetchTablePage(page: number) {
  tableLoading.value = true
  try {
    const kw = keyword.value.trim()
    const res = kw
      ? await searchContents(kw, 'journal', (page - 1) * TABLE_PAGE_SIZE, TABLE_PAGE_SIZE)
      : await listMyContents('journal', (page - 1) * TABLE_PAGE_SIZE, TABLE_PAGE_SIZE)
    tableItems.value = res.items
    tableTotal.value = res.total
    tablePage.value = page
  } finally {
    tableLoading.value = false
  }
}

function goTablePage(p: number) {
  // 边界外 / 在途请求时忽略（按钮本身已 disabled，这里是兜底防重入）
  if (p < 1 || p > tablePages.value || tableLoading.value) return
  fetchTablePage(p)
}

// 表格视图初始进入（localStorage 恢复）或切换时拉取第一页；onShow 里另有编辑返回后的当前页刷新
watch(viewMode, (v) => {
  if (v === 'table') fetchTablePage(1)
}, { immediate: true })

// 表格视图直达编辑页（与创建同一页面）
function editJournal(c: ContentItem) {
  uni.navigateTo({ url: `/pages/publish/publish?id=${c.id}&kind=journal` })
}

function goPublish() {
  uni.navigateTo({ url: '/pages/publish/publish?kind=journal' })
}

// 底部天气 chip（现象图标+温度，与随记页同款）
function weatherChip(c: ContentItem): string {
  return weatherChipText(c.weather ?? null)
}

function wordCount(c: ContentItem): number {
  return stripMarkdown(c.markdownBody ?? '').length
}

// 字数展示：≥1000 用 k 单位（1 位小数去尾零，≥100k 取整），避免长数字撑宽列
function formatWords(n: number): string {
  if (n < 1000) return String(n)
  const k = n / 1000
  const s = k >= 100 ? String(Math.round(k)) : k.toFixed(1).replace(/\.0$/, '')
  return `${s}k`
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
/* 工具 pill：视图三格（图标）+ 分隔 + 搜索格，统一 32px 高 */
.toolbar-pill {
  display: inline-flex;
  align-items: center;
  height: 32px;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  overflow: hidden;
  background: var(--hz-color-surface);
}
.vt {
  width: 34px;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  color: var(--hz-color-text-secondary);
  transition: background 0.15s ease, color 0.15s ease;
}
.vt:hover {
  color: var(--hz-color-text);
}
.vt.on {
  background: var(--hz-color-primary);
  color: #fff;
}
.vt-ico {
  width: 16px;
  height: 16px;
  display: block;
}
.pill-sep {
  width: 1px;
  height: 16px;
  background: var(--hz-color-border);
  margin: 0 2px;
}
/* 搜索态：输入框接管按钮区（同一行），高度与 pill 一致 */
.search-inline {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 32px;
  flex: 1;
  min-width: 0;
  max-width: 260px;
  border: 1px solid var(--hz-color-primary);
  border-radius: 999px;
  padding: 0 6px 0 12px;
  background: var(--hz-color-surface);
}
.si-ico {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
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
  /* 悬浮写日志：全端常驻（此前桌面隐藏，用户够不到入口） */
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
  cursor: pointer;
}
.cards {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
/* 三段式卡片：标题（1行）→ 预览（≤200字，行数截断）→ 日期+天气（细线分隔）。
   呼吸感来自分区而非大间距：内容区上下 10px，meta 有独立 hairline */
.card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-sm);
  border: 1px solid transparent;
  padding: 16px 18px 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  cursor: pointer;
  transition: border-color 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;
}
.card:hover {
  border-color: var(--hz-color-border);
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.07);
  transform: translateY(-1px);
}
.title {
  font-size: 17px;
  font-weight: 650;
  color: var(--hz-color-text);
  line-height: 1.45;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
/* cherry 懒挂载占位：挂载前撑住最小高度防滚动跳变 */
.md-preview {
  min-height: 60px;
}
/* 网格卡紧凑观感：cherry 预览小档字号/行距（顶层 :deep——uni 会把嵌套 :deep 编译成
   落错层的 data-v 规则，MarkdownBody 教训：一律写顶层） */
.md-preview :deep(.cherry-previewer) {
  font-size: var(--hz-font-size-sm);
  line-height: 1.6;
}
.md-preview :deep(.cherry-markdown > :first-child) {
  margin-top: 0;
}
/* 日期+天气紧贴标题下方（同一信息块，不再底部吸附加分隔线） */
.meta {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 4px;
}
.date {
  font-size: 12px;
  color: var(--hz-color-text-secondary);
}
.weather {
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--hz-color-bg);
}
.weather-text {
  font-size: 12px;
  color: var(--hz-color-text-secondary);
}
.more {
  display: flex;
  justify-content: center;
  padding: 4px 0 12px;
}
/* 表格分页条：上一页 / 页码 / 下一页 */
.pager {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 4px 0 12px;
}
.pager-btn {
  height: 32px;
  line-height: 32px;
  padding: 0 14px;
  background: var(--hz-color-surface);
  color: var(--hz-color-interactive);
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
}
.pager-btn[disabled] {
  opacity: 0.45;
}
.pager-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
}
/* 表格视图：view 网格模拟（跨端不用 <table>） */
.table {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-sm);
  overflow: hidden;
}
.trow {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  border-bottom: 1px solid var(--hz-color-border);
  cursor: pointer;
}
.trow:last-child {
  border-bottom: none;
}
.trow.thead {
  background: var(--hz-color-bg);
  cursor: default;
}
.trow.thead .tcell {
  color: var(--hz-color-text-secondary);
  font-size: var(--hz-font-size-sm);
}
.tcell {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.c-title {
  flex: 1;
  min-width: 0;
  font-weight: 600;
}
.c-date {
  flex: 0 0 132px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.c-words {
  flex: 0 0 48px;
  text-align: right;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.c-op {
  flex: 0 0 auto;
  display: flex;
  gap: 10px;
}
.op {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
  cursor: pointer;
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
    padding: 24px;
  }
  .container {
    /* 日志是多列浏览页：放宽到 60rem，卡片按 auto-fill 自适应分列
       （旧的固定 2 栏 × 42rem 每栏仅 21rem，窄卡+多余留白两头不讨好） */
    /* 三种视图全部撑满可用宽度（侧边栏/工具坞之间）——用户要求卡片/表格跟进阅读视图 */
    max-width: none;
    margin: 0 auto;
    width: 100%;
  }
  .cards {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
    gap: 16px;
    align-items: stretch;
  }
  .card {
    padding: 18px 20px 14px;
  }
  .write-btn {
    display: none;
  }
  /* 桌面 fab 常驻：抬离底部栏；≥1200 再左移避开右侧 300px 工具坞 */
  .fab {
    bottom: 28px;
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
  .fab {
    right: 324px;
  }
}
</style>
