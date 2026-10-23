<template>
  <!-- 日志阅读视图：连续信息流（用户决策），滚动加载替代翻页——
       所有已加载日志竖向堆叠，滚近底部自动拉下一页并追加到流里 -->
  <view class="reader">
    <view v-for="item in items" :key="item.id" class="article">
      <text class="r-title">{{ item.title }}</text>
      <view class="r-meta">
        <text class="r-meta-item">{{ formatDateTime(item.createdAt) }}</text>
        <text v-if="lunarLine(item)" class="r-meta-item">{{ lunarLine(item) }}</text>
        <text v-if="weatherLine(item)" class="r-meta-item">{{ weatherLine(item) }}</text>
      </view>
      <view class="r-divider" />
      <!-- 读侧统一 cherry previewOnly（用户决策），与编辑器渲染完全一致；
           一篇一个 cherry 实例，IntersectionObserver 懒挂载——视口 ±300px 内才实例化，
           挂载前占位 min-height 防跳变（用 div：view 的 ref 拿不到 DOM 元素） -->
      <div
        v-if="(item.markdownBody ?? '').trim()"
        :ref="(el) => registerHost(item.id, el)"
        class="md-host"
      >
        <AsyncCherryEditor
          v-if="visibleIds.has(item.id)"
          :model-value="item.markdownBody ?? ''"
          preview-only
        />
      </div>

      <!-- 内联评论（①A）：详情页已移除，阅读流内直接展开评论区；
           评论组件独立于正文懒挂载逻辑，展开时直接挂载 -->
      <view class="r-cmt-row">
        <text class="r-cmt-btn" @click="toggleComments(item.id)">{{
          $t('detail.commentCount', { n: item.commentCount })
        }}</text>
      </view>
      <CardComments
        v-if="expandedIds.has(item.id)"
        :content-id="item.id"
        @count-change="(n) => onCommentCount(item, n)"
      />
    </view>

    <view v-if="loading" class="r-hint">
      <text class="r-hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="finished" class="r-hint">
      <text class="r-hint-text">{{ $t('common.noMore') }}</text>
    </view>

    <!-- 回顶：正文很长（滚动够深）才现身，平滑滚回 -->
    <view v-show="showTop" class="r-top" @click="toTop">
      <text class="r-top-text">Top</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { defineAsyncComponent, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { formatDateTime } from '../utils/format'
import { formatLunar } from '../utils/lunar'
import { CONDITION_ICONS, type WeatherCondition } from '../utils/weather'
import CardComments from './CardComments.vue'
import type { ContentItem } from '../api/endpoints'

const props = defineProps<{
  items: ContentItem[]
  finished: boolean
  loading: boolean
}>()
const emit = defineEmits<{ (e: 'load-more'): void }>()

// cherry 编辑器懒加载：模块顶层异步组件，vite 自动拆 chunk（读侧正文渲染统一走 cherry previewOnly）
const AsyncCherryEditor = defineAsyncComponent(() => import('./CherryEditor.vue'))

function lunarLine(item: ContentItem) {
  return formatLunar(item.lunar)
}

function weatherLine(item: ContentItem) {
  const w = item.weather
  if (!w?.city) return ''
  const icon = CONDITION_ICONS[w.condition as WeatherCondition] ?? ''
  const temp = w.tempC !== null && w.tempC !== undefined ? Math.round(w.tempC) + '°C' : ''
  return [icon, w.city, temp].filter(Boolean).join(' ')
}

// ---------- 正文懒挂载：IntersectionObserver（同 ContentCard 做法）----------
// 单个观察器盯所有文章的占位 div，进过一次视口即挂载且保持不卸载（回滚不重渲染）
const visibleIds = reactive(new Set<string>())
const hostIds = new Map<HTMLElement, string>()
let bodyIo: IntersectionObserver | null = null

function registerHost(id: string, el: unknown) {
  const host = el as HTMLElement | null
  if (!host || hostIds.has(host)) return
  hostIds.set(host, id)
  if (visibleIds.has(id)) return
  bodyIo ??= new IntersectionObserver(
    (entries) => {
      for (const e of entries) {
        if (!e.isIntersecting) continue
        const hit = hostIds.get(e.target as HTMLElement)
        if (hit) visibleIds.add(hit)
        bodyIo?.unobserve(e.target)
      }
    },
    { rootMargin: '300px' } // 提前约半屏开始渲染，滚动到位时正文已就绪
  )
  bodyIo.observe(host)
}

onBeforeUnmount(() => bodyIo?.disconnect())

// ---------- 内联评论（①A）：点击「评论 n」展开/收起，收起即卸载 CardComments ----------
const expandedIds = reactive(new Set<string>())

function toggleComments(id: string) {
  if (expandedIds.has(id)) {
    expandedIds.delete(id)
  } else {
    expandedIds.add(id)
  }
}

// 发送评论成功：把最新评论数回写该篇（meta 下方「评论 n」同步）
function onCommentCount(item: ContentItem, n: number) {
  item.commentCount = n
}

// ---------- 滚动加载 + 回顶：同一个 window scroll 监听 ----------
// 阅读模式=连续信息流（用户决策），滚动加载替代翻页；距底 ≤300px 自动拉下一页
const showTop = ref(false)
function checkLoadMore() {
  const distance = document.documentElement.scrollHeight - window.innerHeight - window.scrollY
  // emit 前先判断 finished/loading 防重复；loadMore 本身也有防重入
  if (distance <= 300 && !props.finished && !props.loading) {
    emit('load-more')
  }
}
function onScroll() {
  showTop.value = window.scrollY > 600
  checkLoadMore()
}
function toTop() {
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

// 兜底：初次加载/追加后内容不足一屏时滚动事件不触发，主动查一次距底
watch([() => props.items.length, () => props.finished, () => props.loading], checkLoadMore)

onMounted(() => {
  window.addEventListener('scroll', onScroll, { passive: true })
})
onBeforeUnmount(() => {
  window.removeEventListener('scroll', onScroll)
})
</script>

<style scoped>
.reader {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
/* 阅读卡：大留白 + 大标题，沉浸感优先 */
.article {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 24px 22px 18px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.r-title {
  font-size: 22px;
  font-weight: 700;
  line-height: 1.4;
  color: var(--hz-color-text);
}
.r-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}
.r-meta-item {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.r-divider {
  height: 1px;
  background: var(--hz-color-border);
}
/* cherry 懒挂载占位：挂载前撑住最小高度防滚动跳变 */
.md-host {
  min-height: 60px;
}
/* 内联评论入口：与 meta 行同字号，手型提示可点 */
.r-cmt-row {
  border-top: 1px solid var(--hz-color-border);
  padding-top: 8px;
}
.r-cmt-btn {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
  cursor: pointer;
}
.r-hint {
  display: flex;
  justify-content: center;
  padding: 2px 0 8px;
}
.r-hint-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
/* 回顶按钮：悬浮右下（写日志 fab 上方），淡入淡出 */
.r-top {
  position: fixed;
  right: 20px;
  bottom: calc(70px + env(safe-area-inset-bottom) + 88px);
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: var(--hz-color-surface);
  border: 1px solid var(--hz-color-border);
  box-shadow: 0 2px 10px rgba(0, 0, 0, 0.14);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  z-index: 10;
  transition: opacity 0.2s ease, transform 0.2s ease;
}
.r-top-text {
  font-size: 12px;
  font-weight: 600;
  color: var(--hz-color-text-secondary);
}
.r-top:hover {
  transform: translateY(-2px);
}
@media (min-width: 768px) {
  .article {
    padding: 32px 36px 24px;
  }
  .r-title {
    font-size: 24px;
  }
  .r-top {
    bottom: 100px;
  }
}
@media (min-width: 1200px) {
  /* 与写日志 fab 同列对齐（避开右侧 300px 工具坞） */
  .r-top {
    right: 324px;
  }
}
</style>
