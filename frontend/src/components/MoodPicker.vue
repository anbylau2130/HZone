<template>
  <!-- 心情选择行（发布页/随记面板共用）：5 档可选可取消 -->
  <view class="mp">
    <text class="mp-label">{{ $t('mood.label') }}</text>
    <view class="mp-row">
      <text
        v-for="m in MOODS"
        :key="m.level"
        class="mp-emoji"
        :class="{ on: modelValue === m.level }"
        @click="pick(m.level)"
      >{{ m.emoji }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
const modelValue = defineModel<number | null>({ default: null })

const MOODS = [
  { level: 1, emoji: '😢' },
  { level: 2, emoji: '😕' },
  { level: 3, emoji: '😐' },
  { level: 4, emoji: '🙂' },
  { level: 5, emoji: '😄' }
]

function pick(level: number) {
  modelValue.value = modelValue.value === level ? null : level
}
</script>

<style scoped>
.mp {
  display: flex;
  align-items: center;
  gap: 10px;
}
.mp-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  flex-shrink: 0;
}
.mp-row {
  display: flex;
  gap: 6px;
}
.mp-emoji {
  font-size: 20px;
  line-height: 1;
  padding: 4px;
  border-radius: var(--hz-radius-md);
  border: 1px solid transparent;
  cursor: pointer;
  opacity: 0.45;
  transition: opacity 0.12s ease, transform 0.12s ease;
}
.mp-emoji.on {
  opacity: 1;
  transform: scale(1.15);
  border-color: var(--hz-color-primary);
  background: color-mix(in srgb, var(--hz-color-primary) 10%, transparent);
}
</style>
