<template>
  <view class="seg">
    <view
      v-for="opt in options"
      :key="opt.value"
      class="seg-item"
      :class="{ on: opt.value === modelValue }"
      @click="select(opt.value)"
    >
      <text class="seg-text" :class="{ on: opt.value === modelValue }">{{ $t(opt.label) }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
const props = defineProps<{
  modelValue: string
  options: { value: string; label: string }[]
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

function select(v: string) {
  if (v !== props.modelValue) emit('update:modelValue', v)
}
</script>

<style scoped>
.seg {
  display: flex;
  height: 36px;
  padding: 2px;
  box-sizing: border-box;
  border-radius: 18px;
  background: color-mix(in srgb, var(--hz-color-text) 6%, transparent);
}
.seg-item {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 14px;
}
.seg-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.seg-item.on {
  background: var(--hz-color-surface);
}
.seg-text.on {
  color: var(--hz-color-primary);
  font-weight: 600;
}
</style>
