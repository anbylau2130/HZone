<script setup lang="ts">
import { onLaunch } from '@dcloudio/uni-app'
import { applyTheme } from './theme/applyTheme'

onLaunch(() => {
  applyTheme('light')
})
</script>

<style>
/* design token：全部颜色/圆角/字号由此定义，主题切换只改变量值（PRD 7.x/6.9） */
:root {
  --hz-color-primary: #4f7cff;
  --hz-color-bg: #f7f8fa;
  --hz-color-surface: #ffffff;
  --hz-color-text: #1f2329;
  --hz-color-text-secondary: #646a73;
  --hz-color-border: #e5e6eb;
  --hz-radius-md: 12px;
  --hz-radius-lg: 20px;
  --hz-font-size-base: 15px;
  --hz-font-size-sm: 13px;
  --hz-space-md: 12px;
}
</style>
