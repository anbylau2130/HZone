<template>
  <!-- 随记页内快速写说说（不跳 publish 页）：轻编辑器 = 文本 + 表情 + 图片。
       传入 edit 即为原地编辑某条随记（保存走 PUT，取消还原） -->
  <view class="composer" :class="{ editing: isEdit }">
    <textarea
      v-if="isEdit"
      v-model="text"
      ref="areaRef"
      class="c-area"
      :maxlength="-1"
      auto-height
    />
    <textarea
      v-else
      v-model="text"
      ref="areaRef"
      class="c-area"
      :placeholder="$t('publish.sayPlaceholder')"
      :maxlength="-1"
      auto-height
    />
    <EmojiPicker v-if="emojiOpen" @select="onEmoji" />
    <view v-if="mediaItems.length" class="c-media">
      <view v-for="m in mediaItems" :key="m.preview" class="c-cell">
        <image class="c-img" :src="m.preview" mode="aspectFill" />
        <text class="c-remove" @click="removeMedia(m.preview)">×</text>
      </view>
    </view>
    <view class="c-bar">
      <view class="c-ico" :class="{ on: emojiOpen }" @click="emojiOpen = !emojiOpen">
        <text class="c-ico-txt">😊</text>
      </view>
      <view class="c-ico" @click="pickImage">
        <text class="c-ico-txt">📷</text>
      </view>
      <view class="c-spacer" />
      <button v-if="isEdit" class="c-cancel" :disabled="busy" @click="emit('cancel')">
        {{ $t('common.cancel') }}
      </button>
      <button class="c-send" :disabled="!canSend || busy" @click="send">
        {{ busy ? $t('common.loading') : isEdit ? $t('common.save') : $t('publish.submit') }}
      </button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import EmojiPicker from './EmojiPicker.vue'
import { createSay, updateContent, type ContentItem } from '../api/endpoints'
import { uploadFile } from '../api/client'
import { loadMediaUrl } from '../api/media'
import { useI18n } from 'vue-i18n'

// 编辑模式：传入已有随记则回填（随记页原地编辑，不跳 publish）；
// 保存走 PUT（recipientIds/weather 不传 = 后端保持原值），新建走 POST
const props = defineProps<{ edit?: ContentItem | null }>()
const emit = defineEmits<{
  (e: 'published', c: ContentItem): void
  (e: 'updated', c: ContentItem): void
  (e: 'cancel'): void
}>()

const { t } = useI18n()
const text = ref('')
const emojiOpen = ref(false)
const busy = ref(false)

interface PickedMedia {
  preview: string
  mediaId?: string
  file?: Blob
}

const mediaItems = ref<PickedMedia[]>([])
const canSend = computed(() => text.value.trim().length > 0 || mediaItems.value.length > 0)
const isEdit = computed(() => !!props.edit)

// 编辑回填：文本 + 已有媒体（带 mediaId 重提交；预览换授权 blob）
watch(
  () => props.edit,
  (c) => {
    if (!c) return
    text.value = c.text ?? ''
    mediaItems.value = []
    for (const m of c.media ?? []) {
      if (m.kind !== 'image' && m.kind !== 'video') continue
      mediaItems.value.push({ preview: m.url, mediaId: m.id })
      loadMediaUrl(m.url)
        .then((u) => {
          const target = mediaItems.value.find((x) => x.mediaId === m.id)
          if (target) target.preview = u
        })
        .catch(() => {})
    }
  },
  { immediate: true }
)

// 本组件实例的内层 textarea（页面可能同时存在新建+编辑两个实例，
// 不能用全局 .c-area 选择器——会插错框）
const areaRef = ref<unknown>(null)
function innerTextarea(): HTMLTextAreaElement | null {
  // #ifdef H5
  const el = (areaRef.value as { $el?: HTMLElement } | null)?.$el ?? (areaRef.value as HTMLElement | null)
  return (el?.querySelector?.('textarea') as HTMLTextAreaElement | null) ?? null
  // #endif
  // #ifndef H5
  return null
  // #endif
}

// 表情插入到光标处，选完即收起
function onEmoji(emoji: string) {
  const ta = innerTextarea()
  if (ta) {
    const s = ta.selectionStart ?? text.value.length
    const e = ta.selectionEnd ?? s
    text.value = text.value.slice(0, s) + emoji + text.value.slice(e)
    emojiOpen.value = false
    nextTick(() => {
      ta.focus()
      const pos = s + emoji.length
      ta.setSelectionRange(pos, pos)
    })
    return
  }
  text.value += emoji
  emojiOpen.value = false
}

const MAX_IMAGES = 9

// H5 同步建 input 选图（挂 body 手势链路确定可开，与 publish 页同套路）
function pickImage() {
  const remaining = MAX_IMAGES - mediaItems.value.length
  if (remaining <= 0) {
    uni.showToast({ title: t('publish.maxImagesReached'), icon: 'none' })
    return
  }
  // #ifdef H5
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = 'image/*'
  input.multiple = true
  input.style.cssText = 'position:fixed;left:-9999px;top:0;width:1px;height:1px;opacity:0;'
  document.body.appendChild(input)
  input.onchange = () => {
    const files = Array.from(input.files ?? []).slice(0, remaining)
    input.remove()
    for (const f of files) {
      const preview = URL.createObjectURL(f)
      mediaItems.value.push({ preview, file: f })
    }
  }
  input.addEventListener('cancel', () => input.remove())
  input.click()
  // #endif
  // #ifndef H5
  uni.chooseImage({
    count: remaining,
    success: (res) => {
      for (const p of res.tempFilePaths) mediaItems.value.push({ preview: p })
    }
  })
  // #endif
}

function removeMedia(preview: string) {
  mediaItems.value = mediaItems.value.filter((m) => m.preview !== preview)
  // #ifdef H5
  if (preview.startsWith('blob:')) URL.revokeObjectURL(preview)
  // #endif
}

async function send() {
  if (!canSend.value || busy.value) return
  busy.value = true
  try {
    // 补传未上传的本地图片（选完即传，此处一般只剩刚选的）
    for (const m of mediaItems.value) {
      if (!m.mediaId) {
        const asset = await uploadFile<{ id: string }>('/api/media', m.file ?? m.preview)
        m.mediaId = asset.id
      }
    }
    const mediaIds = mediaItems.value.map((m) => m.mediaId).filter((id): id is string => !!id)

    if (props.edit) {
      // 原地保存：媒体整体重绑（移除格子=解绑）；recipientIds/weather 不传，后端保持原值
      const updated = await updateContent(props.edit.id, { text: text.value, mediaIds })
      uni.showToast({ title: t('publish.saved'), icon: 'success' })
      emit('updated', updated)
      return
    }

    const created = await createSay(text.value, [], mediaIds)
    // 成功即清空，准备下一条
    text.value = ''
    for (const m of mediaItems.value) {
      // #ifdef H5
      if (m.preview.startsWith('blob:')) URL.revokeObjectURL(m.preview)
      // #endif
    }
    mediaItems.value = []
    emojiOpen.value = false
    uni.showToast({ title: t('publish.success'), icon: 'success' })
    emit('published', created)
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    busy.value = false
  }
}

defineExpose({
  focus() {
    innerTextarea()?.focus()
  }
})
</script>

<style scoped>
/* 轻编辑器：与 feed 卡片同底色的浅色框，弱化"表单感"强化"随手记" */
.composer {
  background: var(--hz-color-surface);
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-lg);
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.c-area {
  width: 100%;
  min-height: 64px;
  font-size: 16px;
  line-height: 1.6;
  color: var(--hz-color-text);
}
.c-media {
  display: grid;
  grid-template-columns: repeat(4, 72px);
  gap: 6px;
}
.c-cell {
  position: relative;
  width: 72px;
  height: 72px;
  border-radius: var(--hz-radius-md);
  overflow: hidden;
}
.c-img {
  width: 100%;
  height: 100%;
}
.c-remove {
  position: absolute;
  right: 2px;
  top: 0;
  width: 20px;
  height: 20px;
  line-height: 20px;
  text-align: center;
  font-size: 14px;
  color: #fff;
  background: rgba(0, 0, 0, 0.55);
  border-radius: 0 0 0 8px;
  cursor: pointer;
}
.c-bar {
  display: flex;
  align-items: center;
  gap: 6px;
}
.c-ico {
  width: 34px;
  height: 34px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--hz-radius-md);
  cursor: pointer;
  transition: background 0.12s ease;
}
.c-ico:hover {
  background: var(--hz-color-bg);
}
.c-ico.on {
  background: var(--hz-color-bg);
}
.c-ico-txt {
  font-size: 19px;
  line-height: 1;
}
.c-spacer {
  flex: 1;
}
.c-send {
  height: 34px;
  line-height: 34px;
  padding: 0 20px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: 999px;
}
.c-send[disabled] {
  opacity: 0.55;
}
/* 编辑态：主色调描边突出"正在编辑这条"；取消按钮弱化 */
.composer.editing {
  border-color: var(--hz-color-primary);
}
.c-cancel {
  height: 34px;
  line-height: 34px;
  padding: 0 16px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-bg);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
}
</style>
