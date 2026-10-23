<template>
  <!-- cherry-markdown 挂载点：cherry 框架无关（依赖 dompurify 组件外注入，无 vue），组件只做 v-model 桥接 -->
  <div
    ref="host"
    :id="hostId"
    class="cherry-host"
    :class="{ 'no-preview': !preview && !previewOnly, 'preview-only': previewOnly }"
  ></div>
</template>

<script lang="ts">
// hostId 的自增序号必须模块作用域：放 script setup 里是实例级的（每实例从 0 重新计数），
// 同毫秒批量挂载的实例（列表懒挂载）会生成相同 id——cherry 按 id 找挂载点，
// 结果全部挂进第一个容器，其余宿主空白（卡片视图只有标题没正文的事故根因）
let hostSeq = 0
</script>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import Cherry, { MenuHookBase } from 'cherry-markdown'
// @ts-ignore cherry 可选 addon（无类型；实例化前经 usePlugin 注册 mermaid 代码块渲染器）
import CherryMermaidPlugin from 'cherry-markdown/dist/addons/cherry-code-block-mermaid-plugin.esm.js'
// mermaid 渲染引擎本地打包（官方 examples/mermaid.html 用 CDN src，本项目离线铁律改为显式
// 注入）。走全量打包单文件入口：包默认入口 mermaid.core.mjs 有 d3/ts-dedent 等外部依赖，
// uni vite 插件强制 preserveSymlinks:true 下 pnpm 嵌套依赖解析不了（构建失败）。
// 必须 default 导入：该文件的 render/initialize 只在 default 导出上（命名导出仅 5 个
// 布局函数），namespace 导入会让 addon 误判"找不到 mermaid"静默回退源码
import Mermaid from 'mermaid/dist/mermaid.esm.min.mjs'
// uni postcss 会把 css 里 input/button/textarea/video 标签选择器改写成 uni-*（实测旧编辑器同款坑），
// cherry 运行时创建的原生 DOM（隐藏上传 input、CodeMirror textarea 等）全部失配 → 样式坏。
// 用 ?raw 绕过 postcss，运行时原样注入（幂等，多实例只注入一次）。
import cherryCss from 'cherry-markdown/dist/cherry-markdown.css?raw'
// 图标字体：?raw 注入的 css 里相对字体路径会按页面 URL 解析而 404（图标全变方块），
// 用 Vite ?url 拿到打包后的字体地址，注入时替换 @font-face 的 src
import chIconWoff2 from 'cherry-markdown/dist/fonts/ch-icon.woff2?url'
import { uploadFile, downloadBlob } from '../api/client'
import { loadMediaUrl } from '../api/media'
import { i18n, type AppLocale } from '../locale'
import { useThemeStore } from '../stores/theme'
// 公式（katex）/图表（echarts）离线渲染：cherry 经 options.externals 解析这两个库
import Katex from 'katex'
import 'katex/dist/katex.min.css'
import * as ECharts from 'echarts'

// compact：时间线内联编辑用精简菜单，渲染引擎与发布页同一 cherry 保证观感一致；
// height：编辑器高度（cherry 以内联 style 写到挂载点，不传走默认 60vh）；
// previewOnly：纯预览模式（读侧正文渲染统一走 cherry，用户决策），高度自适应内容
const props = defineProps<{
  modelValue: string
  placeholder?: string
  preview?: boolean
  compact?: boolean
  height?: string
  previewOnly?: boolean
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

// 同毫秒多实例会撞 id（cherry 按 id 找挂载点，撞了就挂进别人容器，对方空白）——加自增序号
let hostSeq = 0
const hostId = `cherry-hz-${Math.random().toString(36).slice(2)}`
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
    host.value.querySelectorAll(
      '.cherry-previewer img[src^="/media/"], .cherry-previewer video[src^="/media/"], .cherry-previewer audio[src^="/media/"]'
    )
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

// ---------- drawIO 画图（cherry drawIo 菜单，照抄官方 examples/drawio_demo.html 集成） ----------
// 官方方案：drawioIframeUrl 指向自托管 drawio grapheditor 静态页（src/static/drawio/，
// 资源取自 cherry-markdown 仓库 examples/assets/{drawio_lib,mxgraph}，随 H5 构建原样拷贝），
// 页内 drawio-bridge.js 实现 cherry 私有 postMessage 协议（ready/setData/getData）。
// 导出产物是官方原生形态：![](data:image/png;base64,..){data-type=drawio data-xml=..}
// ——base64 dataURL 必须保留在正文里：cherry 的"预览区点击图片重新编辑"内部正则
// 只认 data:image/..;base64 形态，换成 /media 路径会静默失去再编辑能力。

// ---------- 自定义媒体插入菜单（image/audio/video/file 共用） ----------
// 不用 cherry 原生 image/video/audio 菜单，两个原因：
// ① 原生缺陷——同一会话第二笔媒体插入会被上一笔的 cacheOnce 语法包裹/吞掉
// （cherry 0.11.11 cacheOnce×异步回调交错）；
// ② 原生 kO() 用未挂载 DOM 的 input 直接 .click()——iOS Safari/微信 WebView 会
// 静默不弹文件框（真机"点了没反应"根因）。自定义菜单走 MenuHookBase（官方自定义
// 菜单机制），input 先挂到 body（fixed 定位移出视口，display:none 同样不可点）
// 再 click()，全程留在用户手势同步栈；resolve(null) 时 fire 跳过选区替换
// 附件扩展名白名单与后端 MediaEndpoints.FileExtensionAllowlist 对齐
const FILE_ACCEPT = '.pdf,.zip,.txt,.csv,.md,.doc,.docx,.xls,.xlsx,.ppt,.pptx'

class InsertMediaMenu extends MenuHookBase {
  private readonly accept: string
  private readonly build: (url: string, name: string) => string

  constructor(cherryOptions: ConstructorParameters<typeof MenuHookBase>[0], kind: 'image' | 'audio' | 'video' | 'file') {
    super(cherryOptions)
    if (kind === 'image') {
      this.accept = 'image/*'
      this.build = (url, name) => `![${name}](${url})\n`
      this.setName('insertImage', 'image') // ch-icon-image
    } else if (kind === 'audio') {
      this.accept = 'audio/*'
      // 语法对齐 cherry 原生音频：!audio[名字](url)（cherry 音频无专属图标，官方同用 video）
      this.build = (url, name) => `!audio[${name}](${url})\n`
      this.setName('insertAudio', 'video')
    } else if (kind === 'file') {
      this.accept = FILE_ACCEPT
      // 附件以普通链接插入；/media 需要鉴权，读侧/预览侧点击走 downloadBlob（见下）
      this.build = (url, name) => `[${name}](${url})\n`
      this.setName('insertFile', 'link')
    } else {
      this.accept = 'video/*'
      this.build = (url) => `<video src="${url}" controls></video>\n`
      this.setName('insertVideo', 'video') // ch-icon-video
    }
  }

  onClick(): Promise<string | null> {
    // 同步段：创建 input、挂 body、挂回调、click() 全在同一同步栈（cherry 派发链
    // pointerup → onClick → fire → onClick 全程无 await），用户手势必达文件框。
    // Promise 只包异步上传回填；resolve(null) 时 fire 跳过选区替换（取消/失败不吞正文）
    const input = document.createElement('input')
    input.type = 'file'
    input.accept = this.accept
    // 真机铁律：input 必须在文档流内才可 click()——fixed 移出视口保持不可见又可交互
    input.style.cssText = 'position:fixed;left:-9999px;top:0;width:1px;height:1px;opacity:0;'
    input.multiple = false
    document.body.appendChild(input)
    let settled = false
    const settle = (resolve: (v: string | null) => void, v: string | null) => {
      if (settled) return
      settled = true
      input.remove() // settle 后立即摘除，不留游离节点
      resolve(v)
    }
    // cherry fire() 的回填会把刚插入的文本整段设为选区（为 cacheOnce 二次点击设计），
    // 连续插入时下一笔会把"仍被选中的上一笔"整个替换掉（图片→音频→视频→流程图连插互吞，
    // 实测实锤）。afterClickCb 在回填后执行一次：把选区折叠成光标，落在插入文本之后
    this.registerAfterClickCb(() => {
      const view = (this as unknown as { editor?: { editor?: { view?: { state?: { selection?: { main?: { to?: number } } }; dispatch: (tr: unknown) => void } } } }).editor
        ?.editor?.view
      const pos = view?.state?.selection?.main?.to
      if (view && typeof pos === 'number') view.dispatch({ selection: { anchor: pos } })
    })
    const done = new Promise<string | null>((resolve) => {
      input.onchange = () => {
        const file = input.files?.[0]
        if (!file) {
          settle(resolve, null)
          return
        }
        uploadOne(file)
          .then((url) => settle(resolve, this.build(url, file.name)))
          .catch(() => {
            uploadFailedToast()
            settle(resolve, null)
          })
      }
      // 用户取消文件框：浏览器触发 cancel 事件，必须 resolve 否则 Promise 悬置（菜单反复触发文件框）
      input.addEventListener('cancel', () => settle(resolve, null))
    })
    // click() 在 Promise 构造语句之后、同一同步栈内执行（用户手势链最末环）
    input.click()
    return done
  }
}

class InsertImageMenu extends InsertMediaMenu {
  constructor(cherryOptions: ConstructorParameters<typeof MenuHookBase>[0]) {
    super(cherryOptions, 'image')
  }
}

class InsertAudioMenu extends InsertMediaMenu {
  constructor(cherryOptions: ConstructorParameters<typeof MenuHookBase>[0]) {
    super(cherryOptions, 'audio')
  }
}

class InsertVideoMenu extends InsertMediaMenu {
  constructor(cherryOptions: ConstructorParameters<typeof MenuHookBase>[0]) {
    super(cherryOptions, 'video')
  }
}

class InsertFileMenu extends InsertMediaMenu {
  constructor(cherryOptions: ConstructorParameters<typeof MenuHookBase>[0]) {
    super(cherryOptions, 'file')
  }
}

// ---------- 打开本地 .md（官方 File 菜单的真机不可用版自研替代） ----------
// 官方 File 菜单走 kO()（未挂 DOM input.click()，真机静默失败）；这里同媒体菜单
// 套路：input 挂 body 同步 click()，FileReader 读文本后整篇替换（编辑器有内容
// 时先确认），与"打开文件"语义一致（官方是插入到光标处）
class OpenLocalFileMenu extends MenuHookBase {
  constructor(cherryOptions: ConstructorParameters<typeof MenuHookBase>[0]) {
    super(cherryOptions)
    this.setName('openLocalFile', 'folder-open')
  }

  onClick(): Promise<string | null> {
    return new Promise((resolve) => {
      const input = document.createElement('input')
      input.type = 'file'
      input.accept = '.md,.markdown,.txt'
      input.style.cssText = 'position:fixed;left:-9999px;top:0;width:1px;height:1px;opacity:0;'
      document.body.appendChild(input)
      let settled = false
      const settle = (v: string | null) => {
        if (settled) return
        settled = true
        input.remove()
        resolve(v)
      }
      input.addEventListener('cancel', () => settle(null))
      input.onchange = () => {
        const file = input.files?.[0]
        if (!file) {
          settle(null)
          return
        }
        const cherryApi = (
          this as unknown as { $cherry?: { getValue(): string; setMarkdown(v: string): void } }
        ).$cherry
        file
          .text()
          .then((text) => {
            if (!cherryApi) {
              settle(null)
              return
            }
            const replace = () => cherryApi.setMarkdown(text)
            if (cherryApi.getValue().trim()) {
              uni.showModal({
                confirmText: i18n.global.t('common.confirm'),
                cancelText: i18n.global.t('common.cancel'),
title: i18n.global.t('publish.openLocal'),
                content: i18n.global.t('publish.openLocalConfirm'),
                success: (res) => {
                  if (res.confirm) replace()
                  settle(null)
                }
              })
            } else {
              replace()
              settle(null)
            }
          })
          .catch(() => settle(null))
      }
      input.click()
    })
  }
}

function injectCherryCss() {
  if (document.getElementById('cherry-css')) return
  const style = document.createElement('style')
  style.id = 'cherry-css'
  // 只替换图标字体的 src（@font-face 唯一；非压缩 css 的 "@font-face {" 带空格，\s* 兼容两种格式）
  const cherryDialogCss = `
.cherry-dialog { position: fixed !important; z-index: 3000 !important; }
`
  style.textContent =
    cherryCss.replace(
      /@font-face\s*\{[^}]*\}/,
      `@font-face{font-family:"ch-icon";src:url("${chIconWoff2}") format("woff2")}`
    ) + cherryDialogCss
  document.head.appendChild(style)
}

// usePlugin 是静态注册（实例化前调用一次）；HMR/多实例下防重复注册报错。
// 官方 examples/mermaid.html 方案：usePlugin(插件, {mermaid}) 显式注入渲染引擎
//（官方 demo 用 engine.syntax.codeBlock.mermaid.src 走 CDN，本项目离线铁律改为
// 本地打包 mermaid 包直接注入，两路在插件内等价——hasExplicitMermaid 优先）
let mermaidPluginRegistered = false
function ensureMermaidPlugin() {
  if (mermaidPluginRegistered) return
  try {
    Cherry.usePlugin(CherryMermaidPlugin, {
      mermaid: Mermaid,
      showSourceToolbar: true
    })
    mermaidPluginRegistered = true
  } catch (e) {
    console.warn('[CherryEditor] mermaid plugin register skipped:', e)
    mermaidPluginRegistered = true
  }
}

onMounted(() => {
  if (!host.value) return
  injectCherryCss()
  // echarts：cherry 内置 table-chart / echarts 代码块插件只从 window.echarts 解析
  //（externals 不生效），挂到 window 才能让图表离线渲染
  ;(window as unknown as { echarts: typeof ECharts }).echarts = ECharts
  ensureMermaidPlugin()
  cherry = new Cherry({
    id: hostId,
    value: props.modelValue,
    locale: cherryLocale(i18n.global.locale.value as AppLocale),
    // cherry 0.11.11 无 typora 式即时渲染模式（EditorMode 仅 editOnly/previewOnly/
    // edit&preview）——edit&preview 是其内置双栏（编辑|实时预览同框，无分离预览窗）
    editor: {
      // previewOnly（读侧正文渲染统一走 cherry，用户决策）：纯预览无编辑区，工具栏/编辑区
      // 由 cherry 自行隐藏；其余模式默认带实时预览（用户要求编辑时能直接看到渲染结果，
      // 与发布页一致）；预览开关仍可切换。发布页同为 edit&preview 双栏
      // TODO(安全): cherry 预览他人内容的 XSS 面需单独评估
      defaultModel: props.previewOnly ? 'previewOnly' : 'edit&preview',
      // cherry 会把该值以内联 style 写到挂载点上（内联优先于组件 css），所以高度从这传；
      // previewOnly 传 auto（cherry 源码：挂载点无内联 height 时才写该值，'auto' 合法），
      // 卡片/阅读页不能像编辑器那样定高 60vh，必须跟随内容伸展
      height: props.previewOnly ? 'auto' : props.height ?? '60vh',
      // 0.11 的 placeholder 透传给 CodeMirror 走 editor.codemirror 子配置；纯预览无输入框，不传
      codemirror: props.previewOnly ? undefined : { placeholder: props.placeholder }
    },
    // previewOnly：工具栏全空（cherry 在 previewOnly 下本就隐藏 toolbar，这里双保险）；
    // TODO(安全): cherry 预览他人内容的 XSS 面需单独评估
    toolbars: props.previewOnly
      ? { toolbar: [], toolbarRight: [], sidebar: [], bubble: [] }
      : props.compact
      ? {
          // compact：时间线内联编辑用精简菜单，渲染引擎与发布页同一 cherry 保证观感一致
          toolbar: [
            'bold',
            'italic',
            '|',
            'header',
            '|',
            'ol',
            'ul',
            'quote',
            '|',
            // insertImage 为自研菜单（customMenu 注册，真机可用）；其余均为 cherry 内置名
            'insertImage',
            'link',
            'inlineCode',
            'code',
            '|',
            'togglePreview',
            'wordCount'
          ],
          toolbarRight: ['wordCount'],
          sidebar: [],
          customMenu: {
            insertImage: InsertImageMenu
          }
        }
      : {
          // 照抄官方 demo（index-demo.js）的 toolbars 块，仅按 0.11.11 与离线可用性裁剪：
          // pdf/word/file 为外部服务不抄；demo 的 customMenuA/B/C 换成自研媒体菜单。
          // insert 组官方是 ['image','audio','video',...]——原生菜单真机不可弹文件框
          //（kO() 未挂 DOM input）且有第二笔插入吞字缺陷，三个都换成自研
          // insertImage/insertAudio/insertVideo（customMenu 注册，见下）
          toolbar: [
        'bold',
        'italic',
        { strikethrough: ['strikethrough', 'underline', 'sub', 'sup', 'ruby'] },
        'size',
        '|',
        'color',
        'header',
        '|',
        'drawIo',
        'openLocalFile',
        '|',
        'ol',
        'ul',
        'checklist',
        'panel',
        'timeline',
        'align',
        'detail',
        '|',
        'formula',
        { insert: ['insertImage', 'insertAudio', 'insertVideo', 'insertFile', 'link', 'hr', 'br', 'code', 'inlineCode', 'formula', 'toc', 'table'] },
        'graph',
        'proTable',
        '|',
        'shortcutKey',
        'search',
        'togglePreview'
      ],
      toolbarRight: ['export', '|', 'fullScreen', '|', 'wordCount'],
      bubble: ['bold', 'italic', 'underline', 'strikethrough', 'sub', 'sup', 'quote', 'ruby', '|', 'size', 'color'],
      sidebar: ['mobilePreview', 'copy', 'theme', 'codeTheme'],
      toc: { defaultModel: 'full' },
      customMenu: {
        insertImage: InsertImageMenu,
        insertAudio: InsertAudioMenu,
        insertVideo: InsertVideoMenu,
        insertFile: InsertFileMenu,
        openLocalFile: OpenLocalFileMenu
      }
    },
    // 公式走 katex（默认 MathJax 无本地库不渲染）；katex 经 externals 挂载
    engine: {
      syntax: {
        mathBlock: { engine: 'katex' },
        inlineMath: { engine: 'katex' }
      }
    },
    // katex 本地 externals：公式离线可用（cherry 从 options.externals 解析，无则回退 window.katex）
    externals: {
      katex: Katex
    },
    // false：走 cherry 单文件菜单路径（xv 直插 replaceSelection，无 cacheOnce 二次回填）。
    // true（多选）路径实测第二笔媒体插入会被上一笔的缓存语法包裹/吞掉（cherry 0.11.11
    // cacheOnce 与异步回调交错的缺陷），多图场景用多次点击或粘贴替代
    multipleFileSelection: false,
    // drawIO（cherry 官方机制，examples/drawio_demo.html 同款）：iframeSrc 指向自托管
    // drawio grapheditor 静态页（src/static/drawio/，随 H5 构建原样拷贝）；为空则按钮点击无效
    drawioIframeUrl: '/static/drawio/index.html',
    drawioIframeStyle: 'border: none; width: 100%; height: 100%;',
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
  // 初次渲染发生在 new Cherry() 构造期内（同步完成），MutationObserver 挂上时预览 DOM
  // 已就绪、不会再有 src 变更事件——previewOnly 读侧（内容仅初始化渲染一次）必须补一次
  // 手动扫描，否则正文里的 /media/ 图/音视频永远不换授权 blob src（e2e browser-tour 实锤）
  swapMediaSrc()

  // 附件链接（插入文件产出的 [名字](/media/..)）带不上鉴权头：预览区点击改走
  // downloadBlob（带 Bearer fetch → blob → 浏览器下载），捕获阶段拦截默认导航
  host.value.addEventListener('click', onHostClick, true)
})

// 预览区 /media 附件链接 → 授权下载（图片/音视频已换 blob src，不进这条路径）
function onHostClick(e: Event) {
  const target = e.target as HTMLElement | null
  const anchor = target?.closest?.('a[href^="/media/"]')
  if (!anchor) return
  e.preventDefault()
  e.stopPropagation()
  const href = anchor.getAttribute('href') ?? ''
  void downloadBlob(href, anchor.textContent?.trim() || 'attachment')
}

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
/* 移动端隐藏 markdown 工具栏（用户要求）：触屏打字为主，格式工具用得少，
   还给正文更多纵向空间；cherry 0.11 的工具栏自身不响应窄屏，只能 CSS 收掉 */
@media (max-width: 767px) {
  /* 移动端不适合源码编辑（用户决策）：编辑页只显示渲染预览 */
  .cherry-host :deep(.cherry-toolbar),
  .cherry-host :deep(.cherry-editor) {
    display: none !important;
  }
  /* 用户主动关掉的预览（no-preview）仍尊重，不强行展开 */
  .cherry-host:not(.no-preview) :deep(.cherry-previewer) {
    display: block !important;
    /* 双栏分宽是 cherry 运行时算的（内联 50%），编辑面板隐藏后必须收回全宽，
       否则内容挤在半栏里（用户反馈"内容显示太小"） */
    width: 100% !important;
    max-width: 100% !important;
    border: none !important;
    padding: 0 4px !important;
  }
}

/* 预览开关：CSS 类隐藏预览窗（不依赖 cherry 内部 API），编辑栏自动占满 */
.cherry-host.no-preview :deep(.cherry-previewer) {
  display: none;
}
.cherry-host.no-preview :deep(.cherry-editor) {
  /* cherry 以内联 style 写 width:50%（同 height 内联套路），必须 !important 覆盖 */
  width: 100% !important;
}
/* 全屏态：cherry 默认 .cherry.fullscreen z-index 99，低于 uni-app 页面头/导航层，
   还原（minscreen）按钮会被日志标题栏遮挡——提到 uni 弹层层级之上 */
.cherry-host :deep(.cherry.fullscreen) {
  z-index: 3000;
}
/* cherry 浮层默认 z-index 极低（dropdown 13 / fullscreen 99），uni 页面头 998、
   页面 overlay 999——极端层叠下弹层会被页面元素盖住，表现为"点了没反应"。
   全部提到 3000 层（uni toast 在 9999 仍在其上，不被遮） */
.cherry-host :deep(.cherry-dropdown),
.cherry-host :deep(.cherry-dialog) {
  z-index: 3000;
}
/* 全量工具栏（28 个一级按钮）在 1280 内容宽下超出 cherry 默认 0 12px 按钮内边距
   的单行容量——压到 0 6px 保持单行不换行（图标间仍留 ≥12px 间距） */
.cherry-host :deep(.cherry-toolbar .cherry-toolbar-button) {
  padding: 0 6px;
}
/* 工具栏溢出横滚（移动端一行放不下时），隐藏滚动条保持观感 */
.cherry-host :deep(.cherry-toolbar .toolbar-left) {
  overflow-x: auto;
  scrollbar-width: none;
}
.cherry-host :deep(.cherry-toolbar .toolbar-left::-webkit-scrollbar) {
  display: none;
}
/* ---------- previewOnly（读侧正文渲染统一 cherry previewOnly，用户决策） ---------- */
/* 纯预览：去编辑器外框观感（无边框/阴影/圆角），透明背景融入宿主卡片 */
.cherry-host.preview-only {
  min-height: 0;
}
.cherry-host.preview-only :deep(.cherry) {
  height: auto;
  min-height: 0;
  border: none;
  border-radius: 0;
  box-shadow: none;
  background: transparent;
}
/* 预览窗高度跟随内容（cherry 的 min/max-height calc(100% - toolbar) 在容器 auto 高下失义，
   显式放开）；宽占满（单栏预览）；可选中复制（读侧正文能力与 MarkdownBody 时代一致） */
.cherry-host.preview-only :deep(.cherry-previewer) {
  width: 100% !important;
  height: auto;
  max-height: none;
  min-height: 0;
  overflow-y: visible;
  border-left: none;
  padding: 0;
  font-size: var(--hz-font-size-base);
  line-height: 1.8;
  cursor: auto;
  user-select: text;
  -webkit-user-select: text;
}
/* 窄屏（<768px）：全量按钮换行多行，cherry 容器固定高（挂载点内联 60vh + overflow:hidden）
   会把溢出的工具行整个裁掉（按钮有布局盒但不可点）——窄屏放开容器高度让工具栏完整
   展开，编辑/预览区改为定高。桌面 (>768px) 保持 60vh 单行。
   previewOnly 无工具栏且需高度自适应，排除在定高逻辑之外 */
@media (max-width: 767px) {
  .cherry-host:not(.preview-only) {
    /* 覆盖 cherry 写在挂载点上的内联 height:60vh */
    height: auto !important;
  }
  .cherry-host:not(.preview-only) :deep(.cherry) {
    height: auto;
  }
  .cherry-host:not(.preview-only) :deep(.cherry .cherry-editor),
  .cherry-host:not(.preview-only) :deep(.cherry .cherry-previewer) {
    /* cherry css 的 calc(100% - var(--height-toolbar)) 在容器 auto 高下失义，改定高 */
    max-height: 55vh !important;
    min-height: 320px !important;
  }
}
</style>
