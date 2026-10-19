<template>
  <view v-if="recipients.length" class="bar">
    <text class="label">{{ $t('detail.recipients') }}</text>
    <view class="chips">
      <text v-for="r in recipients" :key="r.userId" class="chip">{{ r.nickname }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import type { RecipientInfo } from '../api/endpoints'

defineProps<{ recipients: RecipientInfo[] }>()
</script>

<style scoped>
.bar {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.chips {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}
.chip {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
  background: var(--hz-color-bg);
  border-radius: var(--hz-radius-md);
  padding: 2px 8px;
}
</style>
