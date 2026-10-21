<template>
  <!-- cherry-markdown 挂载点：cherry 框架无关（依赖 dompurify 等，无 vue），组件只做 v-model 桥接 -->
  <div ref="host" :id="hostId" class="cherry-host"></div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import Cherry, { MenuHookBase } from 'cherry-markdown'
// uni postcss 会把 css 里 input/button/textarea/video 标签选择器改写成 uni-*（实测 vditor 同款坑），
// cherry 运行时创建的原生 DOM（隐藏上传 input、CodeMirror textarea 等）全部失配 → 样式坏。
// 用 ?raw 绕过 postcss，运行时原样注入（幂等，多实例只注入一次）。
import cherryCss from 'cherry-markdown/dist/cherry-markdown.css?raw'
// 图标字体：?raw 注入的 css 里相对字体路径会按页面 URL 解析而 404（图标全变方块），
// 用 Vite ?url 拿到打包后的字体地址，注入时替换 @font-face 的 src
import chIconWoff2 from 'cherry-markdown/dist/fonts/ch-icon.woff2?url'
import { uploadFile } from '../api/client'
import { loadMediaUrl } from '../api/media'
import { i18n, type AppLocale } from '../locale'
import { useThemeStore } from '../stores/theme'

const props = defineProps<{ modelValue: string; placeholder?: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const hostId = `cherry-hz-${Date.now().toString(36)}`
const host = ref<HTMLElement | null>(null)
let cherry: Cherry | null = null
let observer: MutationObserver | null = null

const theme = useThemeStore()

// uni locale → cherry locale（初始化取一次，运行中切语言不重建编辑器）
function cherryLocale(locale: AppLocale): 'zh_CN' | 'en_US' {
  return locale === 'en-US' ? 'en_US' : 'zh_CN'
}

// ---------- 媒体鉴权显示 ----------
// 正文 markdown 始终保存 /media/* 相对路径（blob: 落库刷新即失效），
// 但预览区 <img>/<video> 发不了 Authorization 头 → MutationObserver 监听
// cherry 根节点，出现 /media/ src 即换授权 blob url（loadMediaUrl 模块级缓存；
// 换成 blob: 不再命中选择器，不会循环触发）
function swapMediaSrc() {
  if (!host.value) return
  for (const el of Array.from(
    host.value.querySelectorAll('.cherry-previewer img[src^="/media/"], .cherry-previewer video[src^="/media/"]')
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

// ---------- 鉴权直传 ----------
// 复用 client.ts 的 Blob fetch 分支（带 Bearer）；失败请求层已 toast，这里兜底
async function uploadOne(file: File): Promise<string> {
  const asset = await uploadFile<{ id: string; url: string }>('/api/media', file)
  return asset.url
}

function uploadFailedToast() {
  uni.showToast({ title: i18n.global.t('publish.uploadFailed'), icon: 'none' })
}

// cherry 的 fileUpload：返回 url 后由 cherry 自己以 ![name](url) 插入选区
function fileUpload(file: File, callback: (url: string) => void) {
  uploadOne(file)
    .then(callback)
    .catch(uploadFailedToast)
}

function fileUploadMulti(files: File[], callback: (urls: { url: string }[]) => void) {
  Promise.all(
    Array.from(files).map((f) => uploadOne(f).then((url) => ({ url })).catch(() => null))
  ).then((rs) => {
    const ok = rs.filter((r): r is { url: string } => r !== null)
    if (ok.length) callback(ok)
    else uploadFailedToast()
  })
}

// ---------- 自定义「插入视频」菜单 ----------
// onClick 返回 Promise<string>：MenuBase.fire 会等 Promise 完成后把返回串替换进选区
// （缓存机制 cacheOnce 的官方异步路径）；用户取消文件框则 Promise 悬置，不插入
class InsertVideoMenu extends MenuHookBase {
  constructor(cherryOptions: ConstructorParameters<typeof MenuHookBase>[0]) {
    super(cherryOptions)
    this.setName('insertVideo', 'video') // ch-icon-video
  }

  onClick() {
    return new Promise<string>((resolve) => {
      const input = document.createElement('input')
      input.type = 'file'
      input.accept = 'video/*'
      input.style.display = 'none'
      input.onchange = async () => {
        const file = input.files?.[0]
        if (!file) {
          resolve('')
          return
        }
        try {
          const url = await uploadOne(file)
          resolve(`<video src="${url}" controls></video>\n`)
        } catch {
          uploadFailedToast()
          resolve('')
        }
      }
      // 用户取消文件框：浏览器触发 cancel 事件，必须 resolve 否则 Promise 悬置（菜单反复触发文件框）
      input.addEventListener('cancel', () => resolve(''))
      input.click()
    })
  }
}

function injectCherryCss() {
  if (document.getElementById('cherry-css')) return
  const style = document.createElement('style')
  style.id = 'cherry-css'
  // 只替换图标字体的 src（@font-face 唯一；非压缩 css 的 "@font-face {" 带空格，\s* 兼容两种格式）
  style.textContent = cherryCss.replace(
    /@font-face\s*\{[^}]*\}/,
    `@font-face{font-family:"ch-icon";src:url("${chIconWoff2}") format("woff2")}`
  )
  document.head.appendChild(style)
}

onMounted(() => {
  if (!host.value) return
  injectCherryCss()
  cherry = new Cherry({
    id: hostId,
    value: props.modelValue,
    locale: cherryLocale(i18n.global.locale.value as AppLocale),
    // cherry 0.11.11 无 typora 式即时渲染模式（EditorMode 仅 editOnly/previewOnly/
    // edit&preview）——edit&preview 是其内置双栏（编辑|实时预览同框，无分离预览窗）
    editor: {
      defaultModel: 'edit&preview',
      // cherry 会把该值以内联 style 写到挂载点上（内联优先于组件 css），所以高度从这传
      height: '60vh',
      // 0.11 的 placeholder 透传给 CodeMirror 走 editor.codemirror 子配置
      codemirror: { placeholder: props.placeholder }
    },
    toolbars: {
      toolbar: [
        'bold',
        'italic',
        'strikethrough',
        'header',
        '|',
        'quote',
        'ol',
        'ul',
        'checklist',
        '|',
        'code',
        'table',
        'hr',
        '|',
        'image',
        'insertVideo',
        'link',
        '|',
        'undo',
        'redo',
        '|',
        'fullScreen'
      ],
      customMenu: {
        insertVideo: InsertVideoMenu
      },
      // 关闭侧边栏/悬浮目录等重项（任务书：裁掉非白名单功能）
      sidebar: false,
      toc: false,
      bubble: false,
      float: false
    },
    multipleFileSelection: true,
    callback: {
      fileUpload,
      fileUploadMulti
    },
    event: {
      afterChange: (md: string) => emit('update:modelValue', md)
    }
  })

  // afterChange 覆盖不到的 DOM 变化（粘贴图片链接、撤销等）兜底换 src
  observer = new MutationObserver(swapMediaSrc)
  observer.observe(host.value, { childList: true, subtree: true, attributes: true, attributeFilter: ['src'] })
})

// 外部回填（编辑已有内容）：值不同才 setMarkdown，避免光标跳动/回环
//（defineExpose 泛型拿到的是组件实例，这里用结构化类型桥接）
watch(
  () => props.modelValue,
  (v) => {
    if (!cherry || v === cherry.getValue()) return
    cherry.setMarkdown(v)
  }
)

/** 编辑回填入口（外部一次性设置内容用） */
function setMarkdown(v: string) {
  cherry?.setMarkdown(v)
}

onBeforeUnmount(() => {
  stopObserver()
  cherry?.destroy()
  cherry = null
})

defineExpose({ setMarkdown })
</script>

<style scoped>
.cherry-host {
  min-height: 280px;
}
/* cherry 注入的 DOM 在子组件边界外，需 :deep 穿透；颜色全部走 --hz-* 变量，明暗自动跟随 */
.cherry-host :deep(.cherry) {
  height: 100%;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-lg);
  overflow: hidden;
  background: var(--hz-color-surface);
  color: var(--hz-color-text);
}
.cherry-host :deep(.cherry-toolbar) {
  background: var(--hz-color-surface);
  border-bottom: 1px solid var(--hz-color-border);
}
.cherry-host :deep(.cherry-toolbar .ch-icon) {
  color: var(--hz-color-text-secondary);
}
.cherry-host :deep(.cherry-editor),
.cherry-host :deep(.cherry-previewer) {
  background: transparent;
  color: var(--hz-color-text);
  border-color: var(--hz-color-border);
}
/* 编辑区：15px/1.8 等宽，与原 CodeMirror 观感一致 */
.cherry-host :deep(.cherry-editor .cm-content) {
  font-size: 15px;
  line-height: 1.8;
  color: var(--hz-color-text);
  caret-color: var(--hz-color-primary);
  font-family:
    ui-monospace,
    SFMono-Regular,
    Consolas,
    'Courier New',
    monospace;
}
/* cherry 自带的 .cm-editor 白底在暗色下刺眼，压成透明（容器色由 --hz-color-surface 接管） */
.cherry-host :deep(.cherry .cm-editor) {
  background: transparent;
}
.cherry-host :deep(.cherry-previewer) {
  font-size: 15px;
  line-height: 1.8;
  padding: 12px 16px;
  overflow-y: auto;
}
.cherry-host :deep(.cherry-previewer img),
.cherry-host :deep(.cherry-previewer video) {
  max-width: 100%;
}
</style>
