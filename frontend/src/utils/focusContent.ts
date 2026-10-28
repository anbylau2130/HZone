/**
 * 通知跳转定位：滚动到内容卡并高亮闪两下（站内信 focus 参数消费端共用）。
 * 列表异步分页加载，轮询等元素出现（最多 ~8s）。
 */
export function focusContentCard(contentId: string): void {
  // #ifdef H5
  const tryFocus = (attempts: number) => {
    const el = document.getElementById(`c-${contentId}`)
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'center' })
      el.classList.add('focus-flash')
      setTimeout(() => el.classList.remove('focus-flash'), 2400)
      return
    }
    if (attempts < 25) setTimeout(() => tryFocus(attempts + 1), 320)
  }
  tryFocus(0)
  // #endif
}
