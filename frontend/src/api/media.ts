/**
 * 媒体加载：后端 /media/* 需要 Bearer（<image>/<video> 标签发不了请求头），
 * H5 下用 fetch 带 Authorization 拉成 blob URL 再喂给 <image>/<video>。
 * 演示与开发期的已知简化：blob URL 缓存在内存，页面刷新即失效重拉。
 */
import { getToken } from './client'

const cache = new Map<string, Promise<string>>()

export function loadMediaUrl(path: string): Promise<string> {
  const cached = cache.get(path)
  if (cached) return cached

  const task = fetch(path, {
    headers: { Authorization: `Bearer ${getToken()}` }
  })
    .then(async (res) => {
      if (!res.ok) {
        throw new Error(`media load failed: ${res.status}`)
      }
      const blob = await res.blob()
      return URL.createObjectURL(blob)
    })
    .catch((err) => {
      cache.delete(path) // 失败不缓存，允许重试
      throw err
    })
  cache.set(path, task)
  return task
}
