<template>
  <view class="page">
    <view v-if="content" class="card">
      <view class="head">
        <text class="author">{{ content.author.nickname }}</text>
        <text class="kind">{{ kindLabel }}</text>
        <text class="time">{{ formatDateTime(content.createdAt) }}</text>
        <CardOwnerActions v-if="isAuthor && !editing" @edit="startEdit" @delete="remove" />
      </view>

      <text v-if="contextLine" class="context-line">{{ contextLine }}</text>

      <template v-if="!editing">
        <text v-if="content.kind === 'journal'" class="title">{{ content.title }}</text>
        <view
          v-if="content.kind === 'journal' && content.markdownBody"
          class="md-body"
          v-html="mdHtml"
        />
        <text v-else-if="content.kind !== 'journal'" class="text">{{ content.text }}</text>
        <MediaGrid :media="content.media" />
        <RecipientsBar :recipients="content.recipients" />
      </template>

      <template v-else>
        <input v-if="content.kind === 'journal'" v-model="editTitle" class="field" :placeholder="$t('publish.titlePlaceholder')" />
        <textarea
          v-model="editBody"
          class="area"
          :placeholder="$t('publish.bodyPlaceholder')"
          :maxlength="-1"
          auto-height
        />
        <view class="edit-actions">
          <button class="mini" @click="saveEdit">{{ $t('common.save') }}</button>
          <button class="mini ghost" @click="cancelEdit">{{ $t('common.cancel') }}</button>
        </view>
      </template>

    </view>

    <view v-if="content" class="card">
      <text class="section-title">{{ $t('detail.comments') }}</text>
      <view v-if="!comments.length" class="hint">
        <text class="hint-text">{{ $t('common.empty') }}</text>
      </view>
      <view v-for="c in comments" :key="c.id" class="comment">
        <view class="comment-head">
          <text class="comment-author">{{ c.author.nickname }}</text>
          <text class="comment-time">{{ formatDateTime(c.createdAt) }}</text>
        </view>
        <text class="comment-text">{{ c.text }}</text>
      </view>
      <view class="comment-input-row">
        <input v-model="commentText" class="field" :placeholder="$t('detail.commentPlaceholder')" />
        <button class="mini" :disabled="!commentText.trim()" @click="postComment">
          {{ $t('detail.send') }}
        </button>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { onLoad, onShow } from '@dcloudio/uni-app'
import { marked } from 'marked'
// mermaid 渲染引擎（与编辑器同款本地打包入口；default 导入——render/initialize 只在
// default 导出上；addon 不直接用：其 render 对 mermaid 11 异步版返回 fallback 占位、
// 靠 cherry 内部 asyncRenderHandler 回填，脱离 cherry 不可用）
import Mermaid from 'mermaid/dist/mermaid.esm.min.mjs'
import { setNavTitle } from '../../locale'
import { useI18n } from 'vue-i18n'
import MediaGrid from '../../components/MediaGrid.vue'
import RecipientsBar from '../../components/RecipientsBar.vue'
import CardOwnerActions from '../../components/CardOwnerActions.vue'
import { formatDateTime } from '../../utils/format'
import { formatLunar } from '../../utils/lunar'
import { CONDITION_ICONS, type WeatherCondition } from '../../utils/weather'
import {
  getContent,
  updateContent,
  deleteContent,
  listComments,
  createComment,
  type ContentItem,
  type CommentItem
} from '../../api/endpoints'
import { useAuthStore } from '../../stores/auth'
import { loadMediaUrl } from '../../api/media'

const { t, te } = useI18n()
const auth = useAuthStore()

const contentId = ref('')
const content = ref<ContentItem | null>(null)
const comments = ref<CommentItem[]>([])
const commentText = ref('')
const editing = ref(false)
const editTitle = ref('')
const editBody = ref('')
// 列表卡编辑图标跳转 detail?edit=1：加载完成后直接进编辑态
let autoEdit = false

const isAuthor = computed(() => content.value?.author.id === auth.user?.id)
const kindLabel = computed(() =>
  content.value?.kind === 'journal' ? t('timeline.kindJournal') : t('timeline.kindSay')
)

// 日志正文渲染完整 Markdown。marked 不理解 cherry 的音视频扩展语法
//（编辑器原生 video 项插入 !video[name](url) / !audio[alt](url)），读侧转回 HTML；
// video/audio 都要 controls（audio 无控件即隐形）
const mdHtml = computed(() => {
  const md = (content.value?.markdownBody ?? '')
    // cherry 图片属性块（drawio 产物的 {data-type=drawio data-xml=..}）marked 不认会
    // 泄漏成正文文本——先转成带属性的 img（xml 编码串留 href 供排查，不再展示）
    .replace(
      /!\[([^\]]*)\]\((data:image[^)]+)\)\{data-type=drawio[^}]*\}/g,
      '<img src="$2" alt="$1" data-type="drawio">'
    )
    .replace(
      /!(video|audio)\[([^\]]*)\]\(([^)]+)\)/g,
      (_m, tag, alt, url) => `<${tag} src="${url}" controls>${alt}</${tag}>`
    )
  return marked.parse(md) as string
})

// ```mermaid 代码块渲染成流程图（读侧直接走 mermaid.render，异步换入 DOM）
let mermaidSeq = 0
let mermaidReady = false
async function renderMermaidBlocks() {
  const blocks = document.querySelectorAll('.md-body pre > code.language-mermaid')
  if (!blocks.length) return
  if (!mermaidReady) {
    Mermaid.initialize({ startOnLoad: false })
    mermaidReady = true
  }
  for (const code of blocks) {
    const src = code.textContent ?? ''
    const id = `mermaid-hz-${++mermaidSeq}`
    try {
      const { svg } = await Mermaid.render(id, src)
      const wrap = document.createElement('div')
      wrap.className = 'mermaid-figure'
      wrap.innerHTML = svg
      ;(code.parentElement as HTMLElement).replaceWith(wrap)
    } catch {
      // 语法错误保留源码展示，不吞内容
    }
  }
}
// 正文里的 /media/*（插图/音频/video 标签）带不上 Authorization 头——渲染后换成
// loadMediaUrl 的授权 blob url（模块级缓存；换成 blob: 后不再命中选择器，不会循环）
function swapMediaSrcs() {
  for (const el of Array.from(
    document.querySelectorAll('.md-body img[src^="/media/"], .md-body video[src^="/media/"], .md-body audio[src^="/media/"]')
  )) {
    const raw = el.getAttribute('src') ?? ''
    loadMediaUrl(raw)
      .then((u) => {
        if (el.getAttribute('src') === raw) el.setAttribute('src', u)
      })
      .catch(() => {})
  }
}

watch(mdHtml, () => nextTick(() => void renderMermaidBlocks()), { immediate: true })
watch(mdHtml, () => nextTick(swapMediaSrcs), { immediate: true })

// 农历+天气全显条（homepage-v2）：`乙巳年 腊月初一 · 大雪 ｜ 杭州 · 晴 · 12°C`
const contextLine = computed(() => {
  const lunar = formatLunar(content.value?.lunar)
  const w = content.value?.weather
  const weatherText = w?.city ? formatWeather(w) : null
  if (!lunar && !weatherText) return ''
  return [lunar, weatherText].filter(Boolean).join(' ｜ ')
})

function conditionLabel(condition: string): string {
  return te(`weather.conditions.${condition}`) ? t(`weather.conditions.${condition}`) : condition
}

function formatWeather(w: NonNullable<ContentItem['weather']>): string {
  const parts = [w.city ?? '']
  if (w.condition) {
    parts.push(conditionLabel(w.condition))
    if (w.tempC !== null && w.tempC !== undefined) {
      parts.push(`${Math.round(w.tempC)}°C`)
    }
  }
  return `${CONDITION_ICONS[w.condition as WeatherCondition] ?? ''} ${parts.filter(Boolean).join(' · ')}`.trim()
}

onLoad((query) => {
  setNavTitle('detail.title')
  const q = (query ?? {}) as Record<string, string>
  contentId.value = q.id ?? ''
  autoEdit = q.edit === '1'
  load()
})

async function load() {
  if (!contentId.value) return
  try {
    content.value = await getContent(contentId.value)
    comments.value = (await listComments(contentId.value)).items
    loadedOnce = true
    if (autoEdit) startEdit()
  } catch {
    // 错误提示由请求层统一 toast；不可见/不存在时留在本页
  }
}

// 编辑/保存跳到 publish 页，返回本页时 onLoad 不会重跑——onShow 刷新正文
let loadedOnce = false
onShow(() => {
  if (loadedOnce) load()
})

function startEdit() {
  if (!content.value) return
  // 日志：编辑复用创建页（cherry 全套编辑器），与"发布日志"同一页面
  if (content.value.kind === 'journal') {
    uni.navigateTo({ url: '/pages/publish/publish?id=' + contentId.value + '&kind=journal' })
    return
  }
  editing.value = true
  editTitle.value = content.value.title ?? ''
  editBody.value = content.value.kind === 'journal'
    ? (content.value.markdownBody ?? '')
    : (content.value.text ?? '')
}

function cancelEdit() {
  editing.value = false
}

async function saveEdit() {
  if (!content.value) return
  try {
    const payload =
      content.value.kind === 'journal'
        ? { title: editTitle.value, markdownBody: editBody.value }
        : { text: editBody.value }
    content.value = await updateContent(contentId.value, payload)
    editing.value = false
    uni.showToast({ title: t('common.saved'), icon: 'success' })
  } catch {
    // 错误提示由请求层统一 toast
  }
}

function remove() {
  uni.showModal({
    title: t('common.delete'),
    content: t('detail.deleteConfirm'),
    success: async (res) => {
      if (!res.confirm) return
      try {
        await deleteContent(contentId.value)
        uni.showToast({ title: t('detail.deleted'), icon: 'success' })
        setTimeout(() => {
          uni.navigateBack()
        }, 600)
      } catch {
        // 错误提示由请求层统一 toast
      }
    }
  })
}

async function postComment() {
  const text = commentText.value.trim()
  if (!text) return
  try {
    const created = await createComment(contentId.value, text)
    comments.value = [...comments.value, created]
    commentText.value = ''
    if (content.value) {
      content.value = { ...content.value, commentCount: content.value.commentCount + 1 }
    }
  } catch {
    // 错误提示由请求层统一 toast
  }
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
.card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.head {
  display: flex;
  align-items: baseline;
  gap: 8px;
}
.author {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.kind {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
}
.time {
  margin-left: auto;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.context-line {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.title {
  font-size: 17px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.edit-actions {
  display: flex;
  gap: 8px;
}
.section-title {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.comment {
  border-bottom: 1px solid var(--hz-color-border);
  padding-bottom: 8px;
}
.comment:last-child {
  border-bottom: none;
}
.comment-head {
  display: flex;
  justify-content: space-between;
  margin-bottom: 4px;
}
.comment-author {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
}
.comment-time {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.comment-text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.field {
  flex: 1;
  height: 38px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 12px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  background: var(--hz-color-bg);
}
.area {
  width: 100%;
  min-height: 160px;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.mini {
  height: 34px;
  line-height: 34px;
  padding: 0 14px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
}
.mini.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
.mini.danger {
  color: #e54d42;
}
.comment-input-row {
  display: flex;
  gap: 8px;
  align-items: center;
}
.md-body {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  line-height: 1.8;
  word-break: break-word;
  /* 正文媒体防溢出：无约束时大图/大视频会横向撑破卡片（编辑器预览区自带约束，读侧此前没有） */
  :deep(img),
  :deep(video) {
    max-width: 100%;
    height: auto;
  }
  :deep(audio) {
    width: 100%;
  }
  :deep(figure) {
    margin: 8px 0;
  }
}
@media (min-width: 768px) {
  .page {
    max-width: calc(48rem + 32px);
    margin: 0 auto;
    padding: 24px 16px;
  }
  .md-body {
    font-size: 15px;
  }
}
</style>
