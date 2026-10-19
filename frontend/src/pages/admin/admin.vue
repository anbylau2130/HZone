<template>
  <view class="page">
    <view class="card">
      <text class="title">{{ $t('admin.title') }}</text>
      <text class="hint">{{ $t('admin.todo') }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
// 占位页：管理后台后端（AdminEndpoints，ticket 11）合入后再补页面。
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'

onShow(() => {
  setNavTitle('admin.title')
})
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  box-sizing: border-box;
}
.card {
  width: 86%;
  max-width: 400px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 32px 24px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  align-items: center;
}
.title {
  font-size: 20px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  text-align: center;
}
</style>
