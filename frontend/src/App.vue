<script setup lang="ts">
// 主题与语言在 main.ts createApp 时恢复（initTheme/restoreLocale），无需 onLaunch
</script>

<style>
/* design token :root 默认值 = 浅色「蜜杏暖阳」基础配色（与 theme/tokens.ts baseColors 同步）。
   主题切换只改变量值（PRD 7.x/6.9），结构 token（圆角/字号/间距）固定不变 */
:root {
  --hz-color-primary: #e9a23b;
  --hz-color-accent: #c2543f;
  --hz-color-interactive: #3e87b5;
  --hz-color-bg: #fbf3e9;
  --hz-color-surface: #fffdf9;
  --hz-color-text: #3b3128;
  --hz-color-text-secondary: #8a7a66;
  --hz-color-border: #efe2ce;
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
