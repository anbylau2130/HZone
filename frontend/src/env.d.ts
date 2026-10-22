/// <reference types="vite/client" />
declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<{}, {}, any>
  export default component
}
// mermaid 全量打包单文件入口（mermaid 包 "." 入口 mermaid.core.mjs 有 d3/ts-dedent 等
// 外部依赖，uni vite 插件强制 preserveSymlinks:true 下 pnpm 嵌套依赖解析不了会构建失败）。
// 该文件 render/initialize 只在 default 导出（命名导出仅 5 个布局函数），必须 default 导入
declare module 'mermaid/dist/mermaid.esm.min.mjs' {
  const mermaid: {
    initialize(options: Record<string, unknown>): void
    render(
      id: string,
      text: string,
      container?: HTMLElement
    ): Promise<{ svg: string; bindFunctions?: unknown }>
    mermaidAPI: unknown
  }
  export default mermaid
}
