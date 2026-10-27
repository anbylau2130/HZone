<template>
  <!-- 分享弹层（共享）：打开即创建外链（7 天）→ 链接可复制。
       ContentCard 与随记页 moments 卡片共用。 -->
  <view v-if="visible" class="share-overlay" @click="$emit('update:visible', false)">
    <view class="share-sheet" @click.stop>
      <view class="share-head">
        <text class="share-title">{{ $t('share.create') }}</text>
        <text class="share-close" @click="$emit('update:visible', false)">{{ $t('common.cancel') }}</text>
      </view>
      <view v-if="url" class="share-body">
        <text class="share-url">{{ url }}</text>
        <button class="share-copy" @click="copy">{{ $t('share.copyLink') }}</button>
      </view>
      <text v-else class="share-hint">{{ $t('common.loading') }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { createShareLink } from '../api/endpoints'

const props = defineProps<{ visible: boolean; contentId: string }>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void }>()

const { t } = useI18n()
const url = ref('')

watch(
  () => props.visible,
  async (v) => {
    if (!v) return
    url.value = ''
    try {
      const link = await createShareLink(props.contentId, 7)
      url.value = `${location.origin}${link.url}`
    } catch {
      emit('update:visible', false)
    }
  }
)

function copy() {
  if (!url.value) return
  uni.setClipboardData({
    data: url.value,
    success: () => uni.showToast({ title: t('share.copiedTip'), icon: 'success' }),
  })
}
</script>

<style scoped>
.share-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: flex-end;
  justify-content: center;
  z-index: 999;
}
.share-sheet {
  width: 100%;
  max-width: 520px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg) var(--hz-radius-lg) 0 0;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.share-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.share-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.share-close {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.share-body {
  display: flex;
  gap: 8px;
  align-items: center;
}
.share-url {
  flex: 1;
  min-width: 0;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
  word-break: break-all;
  background: var(--hz-color-bg);
  border-radius: var(--hz-radius-md);
  padding: 8px 10px;
}
.share-copy {
  flex-shrink: 0;
  height: 32px;
  line-height: 32px;
  padding: 0 16px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: 999px;
}
.share-hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  text-align: center;
  padding: 8px 0;
}
</style>
