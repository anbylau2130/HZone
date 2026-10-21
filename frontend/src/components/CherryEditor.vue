<template>
  <!-- cherry-markdown 挂载点：cherry 框架无关（依赖 dompurify 组件外注入，无 vue），组件只做 v-model 桥接 -->
  <div
    ref="host"
    :id="hostId"
    class="cherry-host"
    :class="{ 'no-preview': !preview }"
  ></div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import Cherry, { MenuHookBase } from 'cherry-markdown'
// @ts-ignore cherry 可选 addon（无类型；自带 mermaid 渲染引擎，零外部导入）
import CherryMermaidPlugin from 'cherry-markdown/dist/addons/cherry-code-block-mermaid-plugin.esm.js'
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
// 公式（katex）/图表（echarts）离线渲染：cherry 经 options.externals 解析这两个库
import Katex from 'katex'
import 'katex/dist/katex.min.css'
import * as ECharts from 'echarts'

const props = defineProps<{ modelValue: string; placeholder?: string; preview?: boolean }>()
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

// ---------- drawIO 画图（cherry drawIo 菜单） ----------
// cherry 0.11.11 的 drawIo 弹层 iframe 期望私有协议（postMessage 包 eventName:
// ready?/setData/getData:success，见包内 dist cherry-markdown.core.js 的 ML 对象），
// 官方只提供 examples/drawio_demo.html 桥接页方案（需自托管整套 drawio_lib 静态资源）。
// 直接指向 embed.diagrams.net 不通（其 embed 协议用 {event:...} 字段，互不相认）。
// 这里生成 blob: 桥接页（零静态资源、零依赖）：内嵌 embed.diagrams.net embed 模式
// iframe（proto=json&export=1&saveAndExit=1），双向翻译两套协议；PNG 导出走 save 事件
// 的 data 字段，桥接页直传 /api/media（blob: 同源，localStorage 直读 hz_token），
// getData:success 回给 cherry 的 base64 即 /media/* 路径——插入产物持久化，不走 base64
// dataURL 落库（会撑爆 DB）。保存按钮是 drawio 编辑器自带的 ✓（saveAndExit），
// cherry 弹层脚下的"确定"由 CSS 隐藏。
function createDrawioBridgeUrl(locale: AppLocale): string {
  const editorUrl = `https://embed.diagrams.net/?embed=1&proto=json&spin=1&export=1&saveAndExit=1&noSaveBtn=1&ui=min&libraries=1&lang=${locale === 'en-US' ? 'en' : 'zh'}`
  const apiBase = `${window.location.origin}`
  // 桥接页脚本保持 ES5 风格字符串拼接（外层模板串里不能用反引号/${}）
  const html = [
    '<!DOCTYPE html><html><head><meta charset="utf-8">',
    '<style>html,body{margin:0;height:100%;overflow:hidden}iframe{border:0;width:100%;height:100%}</style>',
    '</head><body><script>',
    'var EDITOR_URL = ' + JSON.stringify(editorUrl) + ';',
    'var API_BASE = ' + JSON.stringify(apiBase) + ';',
    'var child = null, loaded = false, pendingXml = null, respawned = false;',
    'function spawnChild() {',
    '  loaded = false; respawned = true;',
    '  if (child) child.remove();',
    '  child = document.createElement("iframe");',
    '  child.src = EDITOR_URL;',
    '  document.body.appendChild(child);',
    '}',
    'function toParent(msg) { parent.postMessage(msg, "*"); }',
    'function toChild(msg) { if (child && child.contentWindow) child.contentWindow.postMessage(msg, "*"); }',
    'window.addEventListener("message", function (e) {',
    '  var d = e.data || {};',
    '  // ---- 宿主（cherry 弹层）→ 桥接页 ----',
    '  if (d.eventName === "ready?") { if (loaded) toParent({ eventName: "ready", value: "" }); return; }',
    '  if (d.eventName === "setData") { pendingXml = d.value || ""; if (loaded && pendingXml) toChild({ event: "load", xml: pendingXml }); return; }',
    '  if (d.eventName === "getData") { return; } // cherry 弹层"确定"已隐藏，不依赖该回路',
    '  // ---- embed.diagrams.net（embed 协议）→ 桥接页 ----',
    '  if (d.event === "init") {',
    '    loaded = true; respawned = false;',
    '    toParent({ eventName: "ready", value: "" });',
    '    if (pendingXml) { toChild({ event: "load", xml: pendingXml }); pendingXml = null; }',
    '    return;',
    '  }',
    '  if (d.event === "save") {',
    '    var xml = d.xml || "";',
    '    var png = typeof d.data === "string" && d.data.indexOf("data:image") === 0 ? d.data : "";',
    '    var finish = function (url) {',
    '      toParent({ eventName: "getData:success", value: { xmlData: xml, base64: url || png || "" } });',
    '      spawnChild(); // drawio save 后自毁（exit），重建等下一轮',
    '    };',
    '    if (png) upload(png); else finish("");',
    '    return;',
    '  }',
    '  if (d.event === "exit" && !respawned) { spawnChild(); }',
    '});',
    'function upload(dataUrl) {',
    '  var token = localStorage.getItem("hz_token") || "";',
    '  var bin = atob(dataUrl.slice(dataUrl.indexOf(",") + 1));',
    '  var bytes = new Uint8Array(bin.length);',
    '  for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);',
    '  var fd = new FormData();',
    '  fd.append("file", new Blob([bytes], { type: "image/png" }), "drawio-" + Date.now() + ".png");',
    '  fetch(API_BASE + "/api/media", { method: "POST", headers: token ? { Authorization: "Bearer " + token } : {}, body: fd })',
    '    .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })',
    '    .then(function (a) { finish(a.url); })',
    '    .catch(function () { finish(""); }); // 上传失败兜底：base64 直插（cherry 原生行为，不丢图）',
    '}',
    'spawnChild();',
    '<\/script></body></html>'
  ].join('\n') // 必须换行拼接：脚本里有 // 注释，无缝拼接会把后续代码吞进注释
  return URL.createObjectURL(new Blob([html], { type: 'text/html' }))
}

// ---------- 自定义媒体插入菜单（image/video 共用） ----------
// 不用 cherry 原生 image/video 菜单：实测缺陷——同一会话第二笔媒体插入会被上一笔的
// cacheOnce 语法包裹/吞掉（cherry 0.11.11 cacheOnce×异步回调交错）。自定义菜单走
// MenuHookBase（官方自定义菜单机制）：input 创建与 click() 在用户手势同一同步栈，
// Promise 只包异步上传回填；resolve(null) 时 fire 跳过选区替换（取消/失败不吞正文）
class InsertMediaMenu extends MenuHookBase {
  private readonly accept: string
  private readonly build: (url: string, name: string) => string

  constructor(cherryOptions: ConstructorParameters<typeof MenuHookBase>[0], kind: 'image' | 'video') {
    super(cherryOptions)
    if (kind === 'image') {
      this.accept = 'image/*'
      this.build = (url, name) => `![${name}](${url})\n`
      this.setName('insertImage', 'image') // ch-icon-image
    } else {
      this.accept = 'video/*'
      this.build = (url) => `<video src="${url}" controls></video>\n`
      this.setName('insertVideo', 'video') // ch-icon-video
    }
  }

  onClick(): Promise<string | null> {
    // 同步段：创建 input、挂回调、click() 全在同一同步栈（cherry 派发链
    // pointerup → _E.onClick → fire → onClick 全程无 await），用户手势必达文件框。
    // Promise 只包异步上传回填；resolve(null) 时 fire 跳过选区替换（取消/失败不吞正文）
    const input = document.createElement('input')
    input.type = 'file'
    input.accept = this.accept
    input.style.display = 'none'
    input.multiple = false
    let settled = false
    const settle = (resolve: (v: string | null) => void, v: string | null) => {
      if (settled) return
      settled = true
      resolve(v)
    }
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

class InsertVideoMenu extends InsertMediaMenu {
  constructor(cherryOptions: ConstructorParameters<typeof MenuHookBase>[0]) {
    super(cherryOptions, 'video')
  }
}

function injectCherryCss() {
  if (document.getElementById('cherry-css')) return
  const style = document.createElement('style')
  style.id = 'cherry-css'
  // 只替换图标字体的 src（@font-face 唯一；非压缩 css 的 "@font-face {" 带空格，\s* 兼容两种格式）
  const cherryDialogCss = `
.cherry-dialog { position: fixed !important; z-index: 3000 !important; }
.cherry-dialog--sure { display: none !important; }
`
  style.textContent =
    cherryCss.replace(
      /@font-face\s*\{[^}]*\}/,
      `@font-face{font-family:"ch-icon";src:url("${chIconWoff2}") format("woff2")}`
    ) + cherryDialogCss
  document.head.appendChild(style)
}

// usePlugin 是静态注册（实例化前调用一次）；HMR/多实例下防重复注册报错
let mermaidPluginRegistered = false
function ensureMermaidPlugin() {
  if (mermaidPluginRegistered) return
  try {
    Cherry.usePlugin(CherryMermaidPlugin)
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
  // drawIO 桥接页（blob: 协议翻译 + PNG 直传 /media），挂载时按当前语言生成
  const drawioBridgeUrl = createDrawioBridgeUrl(i18n.global.locale.value as AppLocale)
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
      // 照抄官方 demo（index-demo.js）的 toolbars 块，仅按 0.11.11 与离线可用性裁剪：
      // drawIo/pdf/word/file 为外部服务不抄；demo 的 customMenuA/B/C 换成 insertVideo
      // （MenuHookBase 自定义菜单，塞进 strikethrough 折叠组）；insert 组用 cherry 原生
      // video（demo 同款，走 callback.fileUpload/fileUploadMulti 鉴权直传）。
      toolbar: [
        'bold',
        'italic',
        { strikethrough: ['strikethrough', 'underline', 'sub', 'sup', 'ruby'] },
        'size',
        '|',
        'color',
        'header',
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
        { insert: ['insertImage', 'audio', 'insertVideo', 'link', 'hr', 'br', 'code', 'inlineCode', 'formula', 'toc', 'table'] },
        'graph',
        'proTable',
        '|',
        'shortcutKey',
        'search',
        'togglePreview'
      ],
      toolbarRight: ['fullScreen', '|', 'wordCount'],
      bubble: ['bold', 'italic', 'underline', 'strikethrough', 'sub', 'sup', 'quote', 'ruby', '|', 'size', 'color'],
      sidebar: ['mobilePreview', 'copy', 'theme', 'codeTheme'],
      toc: { defaultModel: 'full' },
      customMenu: {
        insertImage: InsertImageMenu,
        insertVideo: InsertVideoMenu
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
    // drawIO（cherry 官方机制）：iframeSrc 指向 blob 桥接页；为空则 drawIo 按钮点击无效
    drawioIframeUrl: drawioBridgeUrl,
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
/* 窄屏（<768px）：全量按钮换行多行，cherry 容器固定高（挂载点内联 60vh + overflow:hidden）
   会把溢出的工具行整个裁掉（按钮有布局盒但不可点）——窄屏放开容器高度让工具栏完整
   展开，编辑/预览区改为定高。桌面 (>768px) 保持 60vh 单行 */
@media (max-width: 767px) {
  .cherry-host {
    /* 覆盖 cherry 写在挂载点上的内联 height:60vh */
    height: auto !important;
  }
  .cherry-host :deep(.cherry) {
    height: auto;
  }
  .cherry-host :deep(.cherry .cherry-editor),
  .cherry-host :deep(.cherry .cherry-previewer) {
    /* cherry css 的 calc(100% - var(--height-toolbar)) 在容器 auto 高下失义，改定高 */
    max-height: 55vh !important;
    min-height: 320px !important;
  }
}
</style>
