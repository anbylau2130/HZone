import { i18n } from '../locale'

const TOKEN_KEY = 'hz_token'
const USER_KEY = 'hz_user'

export interface HZUser {
  id: string
  email: string
  nickname: string
  /** 头像 /media/ 相对路径；null = 未设置（票 05）。 */
  avatarUrl?: string | null
  /** 签名（≤100 字，null = 未设置，moments-wechat 票 01）。 */
  signature?: string | null
  /** 封面 /media/ 相对路径；null = 未设置（moments-wechat 票 01）。 */
  coverUrl?: string | null
  isAdmin: boolean
}

export interface ApiError extends Error {
  status: number
  data: unknown
}

// ---------- 会话存储 ----------

export function getToken(): string {
  return (uni.getStorageSync(TOKEN_KEY) as string) || ''
}

export function getStoredUser(): HZUser | null {
  try {
    const raw = uni.getStorageSync(USER_KEY) as string
    if (!raw) return null
    return typeof raw === 'string' ? (JSON.parse(raw) as HZUser) : (raw as HZUser)
  } catch {
    return null
  }
}

export function saveSession(token: string, user: HZUser) {
  uni.setStorageSync(TOKEN_KEY, token)
  uni.setStorageSync(USER_KEY, JSON.stringify(user))
}

export function clearSession() {
  uni.removeStorageSync(TOKEN_KEY)
  uni.removeStorageSync(USER_KEY)
}

export function saveUser(user: HZUser) {
  uni.setStorageSync(USER_KEY, JSON.stringify(user))
}

// ---------- 错误信息提取（后端为 ProblemDetails：title / errors{field:[...]}） ----------

function extractMessage(data: unknown): string | null {
  if (!data || typeof data !== 'object') return null
  const d = data as Record<string, unknown>
  const errors = d.errors
  if (errors && typeof errors === 'object') {
    const msgs: string[] = []
    for (const v of Object.values(errors as Record<string, unknown>)) {
      if (Array.isArray(v)) msgs.push(...v.map(String))
      else msgs.push(String(v))
    }
    if (msgs.length) return msgs.join('; ')
  }
  if (typeof d.title === 'string') return d.title
  if (typeof d.error === 'string') return d.error
  if (typeof d.message === 'string') return d.message
  return null
}

// ---------- 统一请求封装 ----------

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  data?: Record<string, unknown>
}

export function request<T = unknown>(path: string, options: RequestOptions = {}): Promise<T> {
  const token = getToken()
  return new Promise<T>((resolve, reject) => {
    uni.request({
      url: path,
      method: options.method ?? 'GET',
      data: options.data,
      header: token ? { Authorization: `Bearer ${token}` } : {},
      success: (res) => {
        const status = res.statusCode
        if (status >= 200 && status < 300) {
          resolve(res.data as T)
          return
        }
        // 已登录态失效（401 且带过 token）：清会话并回登录页。
        // 登录接口自身的 401 不带 token，不会触发跳转，正常走错误 toast。
        if (status === 401 && token) {
          clearSession()
          const pages = getCurrentPages()
          const current = pages[pages.length - 1]
          if (!current || current.route !== 'pages/login/login') {
            uni.reLaunch({ url: '/pages/login/login' })
          }
        }
        const message = extractMessage(res.data) ?? i18n.global.t('common.requestFailed')
        uni.showToast({ title: message, icon: 'none' })
        const err = new Error(message) as ApiError
        err.status = status
        err.data = res.data
        reject(err)
      },
      fail: () => {
        const message = i18n.global.t('common.networkError')
        uni.showToast({ title: message, icon: 'none' })
        reject(new Error(message))
      }
    })
  })
}

// ---------- 上传 / 下载 ----------

/**
 * multipart 上传（POST，字段名固定 'file'），带 Bearer；响应按 JSON 解析。
 * 媒体上传（POST /media）与 zip 导入（POST /import）共用。
 */
export function uploadFile<T = unknown>(path: string, filePath: string): Promise<T> {
  const token = getToken()
  return new Promise<T>((resolve, reject) => {
    uni.uploadFile({
      url: path,
      filePath,
      name: 'file',
      header: token ? { Authorization: `Bearer ${token}` } : {},
      success: (res) => {
        const status = res.statusCode
        if (status >= 200 && status < 300) {
          try {
            resolve(JSON.parse(res.data) as T)
          } catch {
            resolve(undefined as unknown as T)
          }
          return
        }
        const message = extractMessage(safeParse(res.data)) ?? i18n.global.t('common.requestFailed')
        uni.showToast({ title: message, icon: 'none' })
        const err = new Error(message) as ApiError
        err.status = status
        err.data = safeParse(res.data)
        reject(err)
      },
      fail: () => {
        const message = i18n.global.t('common.networkError')
        uni.showToast({ title: message, icon: 'none' })
        reject(new Error(message))
      }
    })
  })
}

/** H5 导出下载：带 Authorization 的 fetch → blob → 触发浏览器下载。 */
export async function downloadBlob(path: string, filename: string): Promise<void> {
  // #ifdef H5
  const res = await fetch(path, { headers: { Authorization: `Bearer ${getToken()}` } })
  if (!res.ok) {
    const message = i18n.global.t('common.requestFailed')
    uni.showToast({ title: message, icon: 'none' })
    throw new Error(`export download failed: ${res.status}`)
  }
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  anchor.click()
  URL.revokeObjectURL(url)
  // #endif
}

function safeParse(raw: string): unknown {
  try {
    return JSON.parse(raw)
  } catch {
    return raw
  }
}
