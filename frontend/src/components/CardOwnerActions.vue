<template>
  <view class="acts" @click.stop>
    <view
      class="icon-btn"
      :aria-label="$t('common.edit')"
      :title="$t('common.edit')"
      @click="$emit('edit')"
    >
      <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M12 20h9" />
        <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z" />
      </svg>
    </view>
    <view
      class="icon-btn danger"
      :aria-label="$t('common.delete')"
      :title="$t('common.delete')"
      @click="$emit('delete')"
    >
      <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="3 6 5 6 21 6" />
        <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
      </svg>
    </view>
  </view>
</template>

<script setup lang="ts">
// 卡片/详情共用的作者操作入口：编辑 ✎ + 删除 🗑 图标（仅作者本人可见，由父级控制渲染）。
// @click.stop 防止冒泡到卡片整体的 open 跳转。
defineEmits<{ (e: 'edit'): void; (e: 'delete'): void }>()
</script>

<style scoped>
.acts {
  display: flex;
  gap: 6px;
  flex-shrink: 0;
}
.icon-btn {
  width: 28px;
  height: 28px;
  border-radius: var(--hz-radius-md);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--hz-color-text-secondary);
  transition: color 0.15s ease;
}
.icon {
  width: 16px;
  stroke-width: 2;
}
.icon-btn:hover {
  color: var(--hz-color-interactive);
}
.icon-btn.danger:hover {
  color: #e54d42;
}
</style>
