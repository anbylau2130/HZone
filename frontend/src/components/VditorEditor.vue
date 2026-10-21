<template>
  <!-- vditor 挂载点：vditor 框架无关（仅依赖 diff-match-patch，无 vue），组件只做 v-model 桥接 -->
  <div ref="host" class="vd-host"></div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import Vditor from 'vditor'
// uni-app 的 postcss 会把 css 里的 input/button/video/textarea 标签选择器改写成 uni-*，
// vditor 运行时创建的原生 DOM（隐藏上传 input、video、复制弹层等）全部失配 → 样式坏。
// 用 ?raw 绕过 postcss，运行时注入原始 css（幂等，多实例只注入一次）。
import vditorCss from 'vditor/dist/index.css?raw'
import { uploadFile } from '../api/client'
import { loadMediaUrl } from '../api/media'
import { i18n, type AppLocale } from '../locale'
import { useThemeStore } from '../stores/theme'

const props = defineProps<{ modelValue: string; placeholder?: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const host = ref<HTMLElement | null>(null)
let vditor: Vditor | null = null
let observer: MutationObserver | null = null

const theme = useThemeStore()

// uni locale → vditor lang（初始化时取一次，运行中切换语言不重建编辑器）
function vditorLang(locale: AppLocale): 'zh_CN' | 'en_US' {
  return locale === 'en-US' ? 'en_US' : 'zh_CN'
}

// ---------- 媒体鉴权显示 ----------
// 正文里的 /media/* 图片/视频（markdown 源始终保存相对路径），<img>/<video> 发不了
// Authorization 头 → MutationObserver 监听编辑区 DOM，出现 /media/ src 即换授权 blob url
// （loadMediaUrl 模块级缓存；换上 blob: src 不再命中选择器，不会循环触发）
function swapMediaSrc() {
  if (!host.value) return
  for (const el of Array.from(
    host.value.querySelectorAll('img[src^="/media/"], video[src^="/media/"]')
  )) {
    const raw = el.getAttribute('src') ?? ''
    loadMediaUrl(raw)
      .then((u) => {
        if (el.getAttribute('src') === raw) el.setAttribute('src', u)
      })
      .catch(() => {})
  }
}

function stopObserver() {
  observer?.disconnect()
  observer = null
}

// ---------- 鉴权直传 /api/media，返回 url 以 markdown 插入光标处 ----------
// 复用 client.ts 的 Blob fetch 分支（带 Bearer）；失败请求层已 toast，这里兜底 + vditor.tip
async function uploadAndInsert(files: File[], kind: 'image' | 'video') {
  try {
    for (const f of files) {
      const asset = await uploadFile<{ id: string; url: string }>('/api/media', f)
      if (kind === 'image') {
        vditor?.insertValue(`![${f.name}](${asset.url})\n`)
      } else {
        vditor?.insertValue(`<video src="${asset.url}" controls></video>\n`)
      }
    }
  } catch {
    uni.showToast({ title: i18n.global.t('publish.uploadFailed'), icon: 'none' })
  }
}

// vditor upload.handler：返回 null 表示自行处理插入（返回字符串会被当作错误信息展示）
function uploadHandler(files: File[]): null {
  uploadAndInsert(files, 'image')
  return null
}

// 自定义「插入视频」菜单：原生 input 选文件 → 直传 → 插入 <video>（vditor 无内置视频项）
function pickVideo() {
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = 'video/*'
  input.onchange = () => {
    const files = Array.from(input.files ?? [])
    if (files.length) uploadAndInsert(files, 'video')
  }
  input.click()
}

const VIDEO_ICON =
  '<svg viewBox="0 0 32 32" width="16" height="16"><path fill="currentColor" d="M12 21.5v-11l9 5.5-9 5.5zM4 6h24v20H4V6zm2 2v16h20V8H6z"/></svg>'

// vditor css 原样注入（uni postcss 会改写标签选择器，见顶部 import 注释）；多次挂载幂等
function injectVditorCss() {
  if (document.getElementById('vditor-css')) return
  const style = document.createElement('style')
  style.id = 'vditor-css'
  style.textContent = vditorCss
  document.head.appendChild(style)
}

onMounted(() => {
  if (!host.value) return
  injectVditorCss()
  vditor = new Vditor(host.value, {
    mode: 'ir', // 即时渲染（typora 式，输入即见排版）
    theme: theme.mode === 'dark' ? 'dark' : 'classic',
    lang: vditorLang(i18n.global.locale.value as AppLocale),
    placeholder: props.placeholder,
    height: '40vh',
    minHeight: 280,
    cache: { enable: false }, // 内容由 v-model 管理，localStorage 缓存会回灌旧稿
    preview: {
      // current 置空 → setContentTheme 直接跳过，避免向 CDN 拉 content-theme css（离线环境）；
      // 编辑区排版由下方 --hz-* 变量覆盖样式接管，明暗跟随主题
      theme: { current: '' }
    },
    toolbar: [
      'headings',
      'bold',
      'italic',
      'strike',
      '|',
      'quote',
      'list',
      'ordered-list',
      'check',
      '|',
      'code',
      'inline-code',
      'table',
      'line',
      '|',
      'upload',
      {
        name: 'insert-video',
        icon: VIDEO_ICON,
        tip: i18n.global.t('publish.insertVideo'),
        tipPosition: 'ne',
        click: pickVideo
      },
      'link',
      '|',
      'undo',
      'redo',
      '|',
      'fullscreen'
    ],
    upload: {
      accept: 'image/*',
      multiple: true,
      handler: uploadHandler
    },
    input: (v) => emit('update:modelValue', v),
    afterChange: () => swapMediaSrc()
  })

  // afterChange 覆盖不到的所有 DOM 变化（粘贴、撤销等）兜底换 src
  observer = new MutationObserver(swapMediaSrc)
  observer.observe(host.value, { childList: true, subtree: true, attributes: true, attributeFilter: ['src'] })
})

// 外部回填（v-model 反向）：值不同才 setValue，避免光标跳动/回环
watch(
  () => props.modelValue,
  (v) => {
    if (!vditor || v === vditor.getValue()) return
    vditor.setValue(v)
  }
)

// 暗色跟随：stores/theme 切换时同步 vditor 工具栏主题（编辑区颜色走 --hz-* 变量自动跟随）
watch(
  () => theme.mode,
  (m) => {
    vditor?.setTheme(m === 'dark' ? 'dark' : 'classic')
  }
)

/** 编辑回填入口（外部一次性设置内容用） */
function setMarkdown(v: string) {
  vditor?.setValue(v)
}

onBeforeUnmount(() => {
  stopObserver()
  vditor?.destroy()
  vditor = null
})

defineExpose({ setMarkdown })
</script>

<style scoped>
.vd-host {
  min-height: 280px;
}
/* vditor 注入的 DOM 在子组件边界外，需 :deep 穿透；颜色全部走 --hz-* 变量，明暗自动跟随 */
.vd-host :deep(.vditor) {
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-lg);
  overflow: hidden;
  background: var(--hz-color-surface);
}
.vd-host :deep(.vditor-toolbar) {
  background: var(--hz-color-surface);
  border-bottom-color: var(--hz-color-border);
  color: var(--hz-color-text);
}
.vd-host :deep(.vditor-toolbar--pin),
.vd-host :deep(.vditor-toolbar-item button) {
  color: var(--hz-color-text-secondary);
}
.vd-host :deep(.vditor-ir .vditor-reset) {
  font-size: 15px;
  line-height: 1.8;
  color: var(--hz-color-text);
  background: transparent;
  caret-color: var(--hz-color-primary);
  font-family:
    ui-monospace,
    SFMono-Regular,
    Consolas,
    'Courier New',
    monospace;
}
/* IR 未聚焦态的占位/标记颜色跟随文本色 */
.vd-host :deep(.vditor-ir .vditor-reset a) {
  color: var(--hz-color-interactive);
}
.vd-host :deep(.vditor-reset img) {
  max-width: 100%;
}
.vd-host :deep(.vditor-ir pre.vditor-reset) {
  background: transparent;
}
/* 选中/高亮跟随主题主色 */
.vd-host :deep(.vditor-ir .vditor-reset ::selection) {
  background: color-mix(in srgb, var(--hz-color-primary) 25%, transparent);
}
</style>
