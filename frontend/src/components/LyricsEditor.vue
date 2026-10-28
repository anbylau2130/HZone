<template>
  <!-- 歌词录入弹层（面板/抽屉共用）：粘贴或键入 lrc 原文；保存/清除。
       打开时回填该曲目现有歌词草稿 -->
  <view v-if="visible" class="le-overlay" @click="close">
    <view class="le-sheet" @click.stop>
      <view class="le-head">
        <text class="le-title">{{ $t('player.lyricsTitle') }}</text>
        <text class="le-cancel" @click="close">{{ $t('common.cancel') }}</text>
      </view>
      <text class="le-name">{{ trackTitle }}</text>
      <textarea
        v-model="draft"
        class="le-area"
        :placeholder="$t('player.lyricsPlaceholder')"
        :maxlength="-1"
        auto-height
      />
      <view class="le-actions">
        <button class="le-mini ghost" @click="onClear">{{ $t('weather.clear') }}</button>
        <button class="le-mini" :disabled="saving" @click="onSave">{{ $t('common.confirm') }}</button>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { usePlayerStore } from '../stores/player'

const props = defineProps<{ visible: boolean; trackId: string; trackTitle: string; initial: string | null }>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void }>()

const { t } = useI18n()
const player = usePlayerStore()
const draft = ref('')
const saving = ref(false)

watch(
  () => props.visible,
  (open) => {
    if (open) draft.value = props.initial ?? ''
  }
)

function close() {
  emit('update:visible', false)
}

async function onSave() {
  saving.value = true
  try {
    await player.setLyrics(props.trackId, draft.value)
    uni.showToast({ title: t('publish.saved'), icon: 'success' })
    close()
  } catch {
    // 请求层已 toast
  } finally {
    saving.value = false
  }
}

async function onClear() {
  saving.value = true
  try {
    await player.setLyrics(props.trackId, '')
    draft.value = ''
    uni.showToast({ title: t('publish.saved'), icon: 'success' })
    close()
  } catch {
    // 请求层已 toast
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.le-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: flex-end;
  justify-content: center;
  z-index: 999;
}
.le-sheet {
  width: 100%;
  max-width: 560px;
  max-height: 78vh;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg) var(--hz-radius-lg) 0 0;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.le-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.le-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.le-cancel {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.le-name {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.le-area {
  width: 100%;
  min-height: 160px;
  max-height: 44vh;
  padding: 10px;
  box-sizing: border-box;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  background: var(--hz-color-bg);
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  font-variant-numeric: tabular-nums;
}
.le-actions {
  display: flex;
  gap: 8px;
}
.le-mini {
  flex: 1;
  height: 38px;
  line-height: 38px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
}
.le-mini.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
.le-mini[disabled] {
  opacity: 0.55;
}
</style>
