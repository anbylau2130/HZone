<template>
  <!-- 日志正文渲染管线（detail 阅读页与日志"阅读视图"共用）：
       cherry 扩展语法预处理 → marked → mermaid 流程图 → /media 鉴权 blob → 附件下载拦截。
       注意必须用 div 而非 view：view 的 ref 在 uni-app 返回组件实例而非 DOM 元素，
       querySelectorAll/addEventListener 均不可用 -->
  <div ref="root" class="md-body" v-html="html"></div>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { marked } from 'marked'
import DOMPurify from 'dompurify'
// mermaid 渲染引擎懒加载（renderMermaidBlocks 内动态 import）：列表页卡片也复用本组件，
// 静态 import 会给首页/时间线背上 ~1MB 的 mermaid 包；只有正文真有 mermaid 代码块才拉取
// （与编辑器同款本地打包入口；default 导入——render/initialize 只在 default 导出上）
import { loadMediaUrl } from '../api/media'
import { downloadBlob } from '../api/client'

const props = defineProps<{ markdown: string }>()

const root = ref<HTMLElement | null>(null)

// 完整 Markdown。marked 不理解 cherry 的音视频扩展语法（!video[name](url) /
// !audio[alt](url)），读侧转回 HTML；video/audio 都要 controls（audio 无控件即隐形）
const html = computed(() => {
  const md = (props.markdown ?? '')
    // cherry 图片属性块（drawio 产物的 {data-type=drawio data-xml=..}）marked 不认会
    // 泄漏成正文文本——先转成带属性的 img（xml 编码串留 href 供排查，不再展示）
    .replace(
      /!\[([^\]]*)\]\((data:image[^)]+)\)\{data-type=drawio[^}]*\}/g,
      '<img src="$2" alt="$1" data-type="drawio">'
    )
    // cherry 图片 alt 扩展语法（尺寸 #300px#200px / 对齐 #center / 装饰 #border…，规则
    // 对齐 cherry imgAltHelper）：marked 只把 alt 当普通文本，尺寸对齐会丢——转成带
    // style 的 <img>；无任何扩展 token 的图片原样留给 marked
    .replace(/!\[([^\]]*)\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g, (m, alt: string, url: string) => {
      const sizes = alt.match(/#([0-9]+(?:px|em|pt|pc|in|mm|cm|ex|%)|auto)/g) ?? []
      const styles: string[] = []
      if (sizes[0]) styles.push(`width:${sizes[0].slice(1)}`)
      if (sizes[1]) styles.push(`height:${sizes[1].slice(1)}`)
      const align = alt.match(/#(center|right|left|float-right|float-left)/i)?.[1]?.toLowerCase()
      if (align === 'center') styles.push('display:block', 'margin:0 auto')
      else if (align === 'right') styles.push('display:block', 'margin-left:auto')
      else if (align === 'float-right') styles.push('float:right')
      else if (align === 'float-left') styles.push('float:left')
      if (/#(border|B)/.test(alt)) styles.push('border:1px solid #888', 'padding:2px', 'box-sizing:border-box')
      if (/#(shadow|S)/.test(alt)) styles.push('box-shadow:0 2px 15px -5px rgba(0,0,0,.5)')
      if (/#(radius|R)/.test(alt)) styles.push('border-radius:15px')
      if (!styles.length) return m
      const cleanAlt = alt
        .replace(
          /#[0-9]+(?:px|em|pt|pc|in|mm|cm|ex|%)|#auto|#(?:center|right|left|float-right|float-left)|#(?:border|shadow|radius|B|S|R)/gi,
          ''
        )
        .trim()
      return `<img src="${url}" alt="${cleanAlt}" style="${styles.join(';')}">`
    })
    .replace(
      /!(video|audio)\[([^\]]*)\]\(([^)]+)\)/g,
      (_m, tag, alt, url) => `<${tag} src="${url}" controls>${alt}</${tag}>`
    )
  // marked 默认不消毒，产物直出 v-html。日志正文是好友间注入面（好友日志里藏
  // <img onerror=...> 等内联事件即存储型 XSS），渲染前必须消毒。DOMPurify 默认
  // 全量 HTML 白名单已覆盖 img/video/audio/source 标签与 src/alt/controls/style/class
  // 属性，data-* 属性（drawio 的 data-type）与媒体标签上的 data: URI（drawio 产物）
  // 默认放行；mermaid 是消毒后另插的 DOM，不走 v-html 管线，不额外开 id
  const rendered = DOMPurify.sanitize(marked.parse(md) as string)
  // /media 直出的 src 先换 1x1 透明占位（真实地址挪到 data-hz-media）：
  // <img>/<video>/<audio> 发不了 Authorization 头，直接请求必 401（控制台噪音），
  // 占位后由 swapMediaSrcs 带 token 换成 blob src，全程零 401。
  // 顺序：消毒在前、占位替换在后——正则作用于消毒产物，新添的 data-hz-media 不会被剥掉
  const PLACEHOLDER =
    'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7'
  return rendered.replace(
    /src="(\/media\/[^"]*)"/g,
    (_m, url: string) => `src="${PLACEHOLDER}" data-hz-media="${url}"`
  )
})

// ```mermaid 代码块渲染成流程图（读侧直接走 mermaid.render，异步换入 DOM）
let mermaidSeq = 0
let mermaidLib: typeof import('mermaid/dist/mermaid.esm.min.mjs') | null = null
async function renderMermaidBlocks() {
  if (!root.value) return
  const blocks = root.value.querySelectorAll('pre > code.language-mermaid')
  if (!blocks.length) return
  if (!mermaidLib) {
    // 懒加载：首次遇到 mermaid 代码块才拉引擎（default 导出才有 render/initialize）
    const { default: Mermaid } = await import('mermaid/dist/mermaid.esm.min.mjs')
    Mermaid.initialize({ startOnLoad: false })
    mermaidLib = Mermaid
  }
  const Mermaid = mermaidLib
  for (const code of blocks) {
    const src = code.textContent ?? ''
    const id = `mermaid-mb-${++mermaidSeq}`
    try {
      const { svg } = await Mermaid.render(id, src)
      const wrap = document.createElement('div')
      wrap.className = 'mermaid-figure'
      wrap.innerHTML = svg
      ;(code.parentElement as HTMLElement).replaceWith(wrap)
    } catch {
      // 语法错误保留源码展示，不吞内容
    }
  }
}

// 正文里的 /media/*（占位图挂 data-hz-media）：带 Authorization 换 loadMediaUrl
// blob src（模块级缓存；换取后移除标记属性，不会重复处理）
function swapMediaSrcs() {
  if (!root.value) return
  for (const el of Array.from(root.value.querySelectorAll('[data-hz-media]'))) {
    const raw = el.getAttribute('data-hz-media') ?? ''
    el.removeAttribute('data-hz-media')
    loadMediaUrl(raw)
      .then((u) => {
        el.setAttribute('src', u)
      })
      .catch(() => {})
  }
}

watch(html, () => nextTick(() => void renderMermaidBlocks()), { immediate: true })
watch(html, () => nextTick(swapMediaSrcs), { immediate: true })

// 附件链接（[名字](/media/..)）点击 → 带 Bearer 下载（<a> 发不了授权头，默认导航必 401）
function onLinkClick(e: Event) {
  const target = e.target as HTMLElement | null
  const anchor = target?.closest?.('a[href^="/media/"]')
  if (!anchor) return
  e.preventDefault()
  e.stopPropagation()
  const href = anchor.getAttribute('href') ?? ''
  void downloadBlob(href, anchor.textContent?.trim() || 'attachment')
}
onMounted(() => {
  root.value?.addEventListener('click', onLinkClick, true)
})
onBeforeUnmount(() => {
  root.value?.removeEventListener('click', onLinkClick, true)
})
</script>

<style scoped>
.md-body {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  line-height: 1.8;
  word-break: break-word;
  /* flex 子项（阅读卡 .article 是 flex column）：不放开 min-width，
     超宽内容的 min-content 会把自身连同卡片一起撑破（阅读模式溢出 bug） */
  min-width: 0;
  max-width: 100%;
  /* 兜底裁剪：mermaid/表格/raw HTML 等无法预知宽度的元素不溢出卡片 */
  overflow-x: hidden;
  /* 正文媒体防溢出：无约束时大图/大视频会横向撑破卡片 */
  :deep(img),
  :deep(video) {
    max-width: 100%;
    height: auto;
  }
  :deep(audio) {
    width: 100%;
  }
  :deep(figure) {
    margin: 8px 0;
  }
  /* 可横向滚动元素：裁剪兜底之上，仍能完整看到内容 */
  :deep(svg),
  :deep(.mermaid-figure) {
    max-width: 100%;
    height: auto;
  }
  :deep(pre) {
    overflow-x: auto;
  }
  :deep(table) {
    display: block;
    max-width: 100%;
    overflow-x: auto;
  }
}
</style>
