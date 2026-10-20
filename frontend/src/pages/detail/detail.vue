<template>
  <view class="page">
    <view v-if="content" class="card">
      <view class="head">
        <text class="author">{{ content.author.nickname }}</text>
        <text class="kind">{{ kindLabel }}</text>
        <text class="time">{{ formatDateTime(content.createdAt) }}</text>
      </view>

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

      <view v-if="isAuthor && !editing" class="owner-actions">
        <button class="mini" @click="startEdit">{{ $t('common.edit') }}</button>
        <button class="mini ghost danger" @click="remove">{{ $t('common.delete') }}</button>
      </view>
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
import { onLoad } from '@dcloudio/uni-app'
import { marked } from 'marked'
import { setNavTitle } from '../../locale'
import { useI18n } from 'vue-i18n'
import MediaGrid from '../../components/MediaGrid.vue'
import RecipientsBar from '../../components/RecipientsBar.vue'
import { formatDateTime } from '../../utils/format'
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

const { t } = useI18n()
const auth = useAuthStore()

const contentId = ref('')
const content = ref<ContentItem | null>(null)
const comments = ref<CommentItem[]>([])
const commentText = ref('')
const editing = ref(false)
const editTitle = ref('')
const editBody = ref('')

const isAuthor = computed(() => content.value?.author.id === auth.user?.id)
const kindLabel = computed(() =>
  content.value?.kind === 'journal' ? t('timeline.kindJournal') : t('timeline.kindSay')
)

// 日志正文渲染完整 Markdown（marked 默认转义输出）
const mdHtml = computed(() => marked.parse(content.value?.markdownBody ?? '') as string)

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
  } catch {
    // 错误提示由请求层统一 toast；不可见/不存在时留在本页
  }
}

function startEdit() {
  if (!content.value) return
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
.owner-actions {
  display: flex;
  gap: 8px;
  border-top: 1px solid var(--hz-color-border);
  padding-top: 10px;
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
}
@media (min-width: 768px) {
  .page {
    max-width: calc(42rem + 32px);
    margin: 0 auto;
    padding: 24px 16px;
  }
  .md-body {
    font-size: 15px;
  }
}
</style>
