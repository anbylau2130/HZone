<template>
  <view class="page">
    <view class="container" :class="{ 'container-wide': viewMode === 'read' }">
      <view class="topbar">
        <text class="topbar-title">{{ $t('journals.title') }}</text>
        <view class="topbar-actions">
          <!-- 卡片/表格/阅读视图切换（阅读在前、默认阅读；localStorage 记忆） -->
          <view v-if="items.length > 0" class="view-toggle">
            <text class="vt" :class="{ on: viewMode === 'read' }" @click="setViewMode('read')">
              {{ $t('journals.viewRead') }}
            </text>
            <text class="vt" :class="{ on: viewMode === 'card' }" @click="setViewMode('card')">
              {{ $t('journals.viewCard') }}
            </text>
            <text class="vt" :class="{ on: viewMode === 'table' }" @click="setViewMode('table')">
              {{ $t('journals.viewTable') }}
            </text>
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
        <!-- 三段式卡：标题 / 内容预览（≤200字）/ 日期+天气 -->
        <view v-for="c in items" :key="c.id" class="card" @click="openDetail(c)">
          <text class="title">{{ c.title }}</text>
          <text class="summary">{{ preview(c) }}</text>
          <view class="meta">
            <text class="date">{{ formatDateTime(c.createdAt) }}</text>
            <view v-if="weatherChip(c)" class="weather">
              <text class="weather-text">{{ weatherChip(c) }}</text>
            </view>
          </view>
        </view>
      </view>

      <!-- 表格视图：标题在最前（点击进查阅页），行点击同样进详情；字数 ≥1000 用 k 单位 -->
      <view v-if="items.length && viewMode === 'table'" class="table">
        <view class="trow thead">
          <text class="tcell c-title">{{ $t('journals.colTitle') }}</text>
          <text class="tcell c-date">{{ $t('journals.colDate') }}</text>
          <text class="tcell c-words">{{ $t('journals.colWords') }}</text>
          <text class="tcell c-op">{{ $t('journals.colActions') }}</text>
        </view>
        <view v-for="c in items" :key="c.id" class="trow" @click="openDetail(c)">
          <text class="tcell c-title">{{ c.title }}</text>
          <text class="tcell c-date">{{ formatDateTime(c.createdAt) }}</text>
          <text class="tcell c-words">{{ formatWords(wordCount(c)) }}</text>
          <view class="tcell c-op" @click.stop>
            <text class="op" @click="editJournal(c)">{{ $t('common.edit') }}</text>
          </view>
        </view>
      </view>

      <!-- 阅读视图：单篇沉浸阅读，滑动/按钮翻篇，接近末尾动态加载 -->
      <JournalReader
        v-if="items.length && viewMode === 'read'"
        :items="items"
        :finished="finished"
        :loading="pagination.loading.value"
        @load-more="loadMore"
      />

      <!-- 加载更多行仅卡片/表格视图（阅读视图自带动态加载） -->
      <view v-if="items.length && viewMode !== 'read'" class="more">
        <text v-if="finished" class="hint-text">{{ $t('common.noMore') }}</text>
        <button v-else class="pick" :disabled="pagination.loading.value" @click="loadMore">
          {{ $t('common.loadMore') }}
        </button>
      </view>
    </view>

    <AppSidebar current="journals" />
    <ToolDock />
    <AppTabBar current="journals" />
    <view class="fab" @click="goPublish">＋</view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import ToolDock from '../../components/ToolDock.vue'
import JournalReader from '../../components/JournalReader.vue'
import { usePagination } from '../../composables/usePagination'
import { listMyContents, type ContentItem } from '../../api/endpoints'
import { formatDateTime, stripMarkdown } from '../../utils/format'
import { weatherChipText } from '../../utils/weather'

const pagination = usePagination<ContentItem>((offset, limit) =>
  listMyContents('journal', offset, limit)
)
const { items, finished, loadMore } = pagination

// onShow 而非 onLoad：H5 端直接 hash 进入时 onLoad 不触发，onShow 稳定
onShow(() => {
  setNavTitle('journals.title')
  pagination.refresh()
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
}

function openDetail(c: ContentItem) {
  uni.navigateTo({ url: `/pages/detail/detail?id=${c.id}` })
}

// 表格视图直达编辑页（与创建同一页面）
function editJournal(c: ContentItem) {
  uni.navigateTo({ url: `/pages/publish/publish?id=${c.id}&kind=journal` })
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

// 卡片预览：纯文本 ≤200 字，超出加省略号（配合 CSS 行数截断双保险）
function preview(c: ContentItem): string {
  const text = summary(c).trim()
  return text.length > 200 ? text.slice(0, 200) + '…' : text
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
.summary {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
  line-height: 1.65;
  display: -webkit-box;
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.meta {
  display: flex;
  align-items: center;
  gap: 8px;
  padding-top: 10px;
  margin-top: auto;
  border-top: 1px solid var(--hz-color-border);
}
.date {
  font-size: 12px;
  color: var(--hz-color-text-secondary);
}
.weather {
  margin-left: auto;
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
/* 视图切换：两个小 pill 并排，on 态 primary */
.view-toggle {
  display: inline-flex;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  overflow: hidden;
  background: var(--hz-color-surface);
}
.vt {
  padding: 4px 12px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.vt.on {
  background: var(--hz-color-primary);
  color: #fff;
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
    /* 日志是多列浏览页：放宽到 60rem，卡片按 auto-fill 自适应分列
       （旧的固定 2 栏 × 42rem 每栏仅 21rem，窄卡+多余留白两头不讨好） */
    max-width: 60rem;
    margin: 0 auto;
    width: 100%;
  }
  /* 阅读视图撑满可用宽度（侧边栏/工具坞之间），卡片/表格仍 60rem 居中 */
  .container-wide {
    max-width: none;
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
