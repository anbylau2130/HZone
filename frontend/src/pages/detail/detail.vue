<template>
  <view class="page">
    <view v-if="content" class="card">
      <view class="head">
        <text class="author">{{ content.author.nickname }}</text>
        <text class="kind">{{ kindLabel }}</text>
        <text class="time">{{ formatDateTime(content.createdAt) }}</text>
        <!-- 编辑一律跳 publish 编辑页（与创建同页），本页纯阅读 -->
        <CardOwnerActions v-if="isAuthor" @edit="startEdit" @delete="remove" />
      </view>

      <text v-if="contextLine" class="context-line">{{ contextLine }}</text>

      <text v-if="content.kind === 'journal'" class="title">{{ content.title }}</text>
      <MarkdownBody
        v-if="content.kind === 'journal' && content.markdownBody"
        :markdown="content.markdownBody"
      />
      <text v-else-if="content.kind !== 'journal'" class="text">{{ content.text }}</text>
      <MediaGrid :media="content.media" />
      <RecipientsBar :recipients="content.recipients" />

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
import { computed, ref } from 'vue'
import { onLoad, onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import { useI18n } from 'vue-i18n'
import MediaGrid from '../../components/MediaGrid.vue'
import MarkdownBody from '../../components/MarkdownBody.vue'
import RecipientsBar from '../../components/RecipientsBar.vue'
import CardOwnerActions from '../../components/CardOwnerActions.vue'
import { formatDateTime } from '../../utils/format'
import { formatLunar } from '../../utils/lunar'
import { CONDITION_ICONS, type WeatherCondition } from '../../utils/weather'
import {
  getContent,
  deleteContent,
  listComments,
  createComment,
  type ContentItem,
  type CommentItem
} from '../../api/endpoints'
import { useAuthStore } from '../../stores/auth'

const { t, te } = useI18n()
const auth = useAuthStore()

const contentId = ref('')
const content = ref<ContentItem | null>(null)
const comments = ref<CommentItem[]>([])
const commentText = ref('')

const isAuthor = computed(() => content.value?.author.id === auth.user?.id)
const kindLabel = computed(() =>
  content.value?.kind === 'journal' ? t('timeline.kindJournal') : t('timeline.kindSay')
)

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
  load()
})

async function load() {
  if (!contentId.value) return
  try {
    content.value = await getContent(contentId.value)
    comments.value = (await listComments(contentId.value)).items
    loadedOnce = true
  } catch {
    // 错误提示由请求层统一 toast；不可见/不存在时留在本页
  }
}

// 编辑跳 publish 页（创建同页），返回本页时 onLoad 不会重跑——onShow 刷新正文
let loadedOnce = false
onShow(() => {
  if (loadedOnce) load()
})

function startEdit() {
  if (!content.value) return
  // 编辑一律复用创建页：日志用 cherry 全套编辑器，说说用轻编辑器（含表情包）
  uni.navigateTo({
    url: `/pages/publish/publish?id=${contentId.value}&kind=${content.value.kind}`
  })
}

function remove() {
  uni.showModal({
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
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
/* 正文渲染样式随 MarkdownBody 组件走（.md-body） */
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
