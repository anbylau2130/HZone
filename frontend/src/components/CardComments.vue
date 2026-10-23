<template>
  <!-- 内联评论区：详情页移除后，评论统一内联在各内容卡片里（展开时挂载，收起时卸载）。
       样式沿用原 detail.vue 评论区：昵称/时间/文本一行一条，头部加头像。 -->
  <view class="cc">
    <!-- 空态：还没有评论 -->
    <view v-if="!comments.length" class="cc-empty">
      <text class="cc-empty-text">{{ $t('common.empty') }}</text>
    </view>

    <view v-for="c in comments" :key="c.id" class="cc-item">
      <AvatarCircle :nickname="c.author.nickname" :avatar-url="c.author.avatarUrl" :size="28" />
      <view class="cc-main">
        <view class="cc-head">
          <text class="cc-author">{{ c.author.nickname }}</text>
          <text class="cc-time">{{ formatDateTime(c.createdAt) }}</text>
        </view>
        <text class="cc-text">{{ c.text }}</text>
      </view>
    </view>

    <!-- 输入行：文本非空才可发，在途（sending）防重复提交 -->
    <view class="cc-input-row">
      <input v-model="text" class="cc-input" :placeholder="$t('detail.commentPlaceholder')" />
      <button class="cc-send" :disabled="!canSend" @click="send">
        {{ sending ? $t('common.loading') : $t('detail.send') }}
      </button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AvatarCircle from './AvatarCircle.vue'
import { formatDateTime } from '../utils/format'
import { createComment, listComments, type CommentItem } from '../api/endpoints'

const props = defineProps<{ contentId: string }>()

// 评论数变化上抛：父卡片据此同步「评论 n」显示
const emit = defineEmits<{ (e: 'count-change', n: number): void }>()

const comments = ref<CommentItem[]>([])
const total = ref(0)
const text = ref('')
const sending = ref(false)

const canSend = computed(() => !!text.value.trim() && !sending.value)

// 展开即拉取当前评论列表（失败静默，错误提示由请求层统一 toast）
onMounted(async () => {
  try {
    const res = await listComments(props.contentId)
    comments.value = res.items
    total.value = res.total
  } catch {
    // 加载失败留空列表，输入行仍可用
  }
})

async function send() {
  const value = text.value.trim()
  if (!value || sending.value) return
  sending.value = true
  try {
    const created = await createComment(props.contentId, value)
    comments.value = [...comments.value, created]
    text.value = ''
    total.value += 1
    emit('count-change', total.value)
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    sending.value = false
  }
}
</script>

<style scoped>
.cc {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 10px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  background: var(--hz-color-bg);
}
.cc-empty {
  display: flex;
  justify-content: center;
  padding: 4px 0;
}
.cc-empty-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.cc-item {
  display: flex;
  gap: 8px;
  border-bottom: 1px solid var(--hz-color-border);
  padding-bottom: 8px;
}
.cc-item:last-child {
  border-bottom: none;
  padding-bottom: 0;
}
.cc-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.cc-head {
  display: flex;
  justify-content: space-between;
  gap: 8px;
}
.cc-author {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
}
.cc-time {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.cc-text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.cc-input-row {
  display: flex;
  gap: 8px;
  align-items: center;
}
.cc-input {
  flex: 1;
  height: 36px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 12px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  background: var(--hz-color-surface);
}
.cc-send {
  height: 32px;
  line-height: 32px;
  padding: 0 14px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
}
.cc-send[disabled] {
  opacity: 0.55;
}
</style>
