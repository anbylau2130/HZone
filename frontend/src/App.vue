<script setup lang="ts">
// 主题与语言在 main.ts createApp 时恢复（initTheme/restoreLocale），无需 onLaunch
</script>

<style>
/* design token：全部颜色/圆角/字号由此定义，主题切换只改变量值（PRD 7.x/6.9） */
:root {
  --hz-color-primary: #4f7cff;
  --hz-color-accent: #e54d42;
  --hz-color-bg: #f7f8fa;
  --hz-color-surface: #ffffff;
  --hz-color-text: #1f2329;
  --hz-color-text-secondary: #646a73;
  --hz-color-border: #e5e6eb;
  --hz-radius-sm: 8px;
  --hz-radius-md: 12px;
  --hz-radius-lg: 20px;
  --hz-font-size-sm: 13px;
  --hz-font-size-base: 15px;
  --hz-font-size-lg: 18px;
  --hz-space-sm: 8px;
  --hz-space-md: 12px;
  --hz-space-lg: 16px;
}
</style>
