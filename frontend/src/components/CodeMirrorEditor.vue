<template>
  <!-- CodeMirror 6 挂载点：纯 DOM 容器，组件本身框架无关（CM 不依赖 vue） -->
  <div ref="host" class="cm-host"></div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { EditorView, placeholder as cmPlaceholder } from '@codemirror/view'
import { basicSetup } from 'codemirror'
import { markdown } from '@codemirror/lang-markdown'
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { tags as t } from '@lezer/highlight'

const props = defineProps<{ modelValue: string; placeholder?: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const host = ref<HTMLElement | null>(null)
let view: EditorView | null = null

// 语法高亮用 token 色全部走 --hz-* 变量，明暗模式自动跟随（applyTheme 重写变量即生效）
const hzHighlight = HighlightStyle.define([
  { tag: t.heading, color: 'var(--hz-color-primary)', fontWeight: '600' },
  { tag: t.strong, fontWeight: '700' },
  { tag: t.emphasis, fontStyle: 'italic' },
  { tag: t.link, color: 'var(--hz-color-interactive)', textDecoration: 'underline' },
  { tag: t.url, color: 'var(--hz-color-text-secondary)' },
  { tag: t.monospace, color: 'var(--hz-color-accent)' },
  { tag: t.quote, color: 'var(--hz-color-text-secondary)' },
  { tag: t.strikethrough, textDecoration: 'line-through' },
  { tag: t.contentSeparator, color: 'var(--hz-color-text-secondary)' }
])

onMounted(() => {
  view = new EditorView({
    doc: props.modelValue,
    extensions: [
      basicSetup,
      markdown(),
      ...(props.placeholder ? [cmPlaceholder(props.placeholder)] : []),
      // 主题化：背景透明融入卡片，光标/选中/行高亮走 token 色
      EditorView.theme({
        '&': { backgroundColor: 'transparent', color: 'var(--hz-color-text)' },
        '.cm-content': { caretColor: 'var(--hz-color-primary)' },
        '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--hz-color-primary)' },
        '.cm-selectionBackground, .cm-content ::selection': {
          backgroundColor: 'color-mix(in srgb, var(--hz-color-primary) 25%, transparent)'
        },
        '.cm-gutters': {
          backgroundColor: 'transparent',
          color: 'var(--hz-color-text-secondary)',
          border: 'none'
        },
        '.cm-activeLine, .cm-activeLineGutter': {
          backgroundColor: 'color-mix(in srgb, var(--hz-color-text) 5%, transparent)'
        },
        '.cm-placeholder': { color: 'var(--hz-color-text-secondary)' }
      }),
      syntaxHighlighting(hzHighlight),
      EditorView.updateListener.of((v) => {
        if (v.docChanged) emit('update:modelValue', v.state.doc.toString())
      })
    ],
    parent: host.value
  })
})

// 外部回填（编辑已有内容）：仅当外部值≠当前 doc 才 dispatch，避免光标跳
watch(
  () => props.modelValue,
  (v) => {
    if (view && v !== view.state.doc.toString()) {
      view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: v } })
    }
  }
)

/** 在当前光标处插入文本（工具栏插图/插视频用） */
function insert(text: string) {
  view?.dispatch(view.state.replaceSelection(text))
}

/** 包裹选中内容（加粗等）：光标落在包裹文本之后 */
function wrap(prefix: string, suffix: string) {
  if (!view) return
  const sel = view.state.selection.main
  const selected = view.state.sliceDoc(sel.from, sel.to)
  const insertText = prefix + selected + suffix
  view.dispatch({
    changes: { from: sel.from, to: sel.to, insert: insertText },
    selection: { anchor: sel.from + insertText.length }
  })
}

/** 在光标所在行首插入（标题用） */
function insertLineStart(text: string) {
  if (!view) return
  const line = view.state.doc.lineAt(view.state.selection.main.head)
  view.dispatch({
    changes: { from: line.from, to: line.from, insert: text },
    selection: { anchor: line.from + text.length }
  })
}

onBeforeUnmount(() => {
  view?.destroy()
  view = null
})

defineExpose({ insert, wrap, insertLineStart })
</script>

<style scoped>
.cm-host {
  height: 40vh;
  min-height: 240px;
}
/* CM 注入的 DOM 在子组件边界外，需 :deep 穿透 */
.cm-host :deep(.cm-editor) {
  height: 100%;
  background: transparent;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-lg);
  overflow: hidden;
  font-size: 15px;
  color: var(--hz-color-text);
}
.cm-host :deep(.cm-scroller) {
  overflow: auto;
  line-height: 1.8;
  font-family: ui-monospace, SFMono-Regular, Consolas, 'Courier New', monospace;
}
/* CM 焦点描边与卡片风格冲突，弱化为 token 边框色 */
.cm-host :deep(.cm-editor.cm-focused) {
  outline: none;
}
</style>
