<template>
  <!-- 播放器传输图标：SVG 几何对称、精确居中。
       替代文字字形（▶⏸⏭☰✕）——它们在字体回退（Segoe UI Symbol/Emoji）下墨迹偏移、
       部分平台 emoji 化，视觉上永远摆不正。 -->
  <svg class="pi" :width="size" :height="size" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <path v-if="name === 'play'" d="M8 5v14l11-7z" />
    <path v-else-if="name === 'pause'" d="M7 5h3.2v14H7zM13.8 5H17v14h-3.2z" />
    <path v-else-if="name === 'prev'" d="M6 6h2v12H6zm3.5 6L18 6v12z" />
    <path v-else-if="name === 'next'" d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z" />
    <path v-else-if="name === 'list'" d="M4 6h16v1.8H4zm0 5.1h16v1.8H4zm0 5.1h16V18H4z" />
    <path v-else-if="name === 'note'" d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" />
    <path v-else-if="name === 'close'" d="M12 10.6 6.4 5 5 6.4l5.6 5.6L5 17.6 6.4 19l5.6-5.6 5.6 5.6 1.4-1.4-5.6-5.6L19 6.4 17.6 5z" />
  </svg>
</template>

<script setup lang="ts">
withDefaults(defineProps<{ name: 'play' | 'pause' | 'prev' | 'next' | 'list' | 'note' | 'close'; size?: number }>(), { size: 14 })
</script>

<style scoped>
.pi {
  display: block;
}
</style>
