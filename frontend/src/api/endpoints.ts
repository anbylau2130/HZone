/**
 * 后端 API 薄封装：全部走 client.ts 的 request（Bearer 自动带上，401 统一跳登录）。
 * DTO 形状与后端 ContentContracts.cs / CommentInboxEndpoints.cs 等一一对应（camelCase）。
 */
import { request } from './client'

// ---------- 内容 ----------

export interface AuthorInfo {
  id: string
  nickname: string
}

export interface RecipientInfo {
  userId: string
  nickname: string
}

export interface MediaItem {
  id: string
  kind: 'image' | 'video' | 'audio'
  url: string
  thumbUrl?: string | null
  sizeBytes: number
}

export interface LunarInfo {
  ganzhiYear: string
  monthDay: string
  term: string
}

export interface WeatherInfo {
  city: string | null
  condition: string | null
  tempC: number | null
}

/** 天气快照提交体（采集见 utils/weather.ts）。 */
export interface WeatherSnapshotInput {
  city: string
  condition: string
  tempC: number
}

export interface ContentItem {
  id: string
  kind: 'say' | 'journal'
  author: AuthorInfo
  text?: string | null
  title?: string | null
  markdownBody?: string | null
  recipients: RecipientInfo[]
  createdAt: string
  updatedAt: string
  commentCount: number
  media?: MediaItem[] | null
  lunar?: LunarInfo | null
  weather?: WeatherInfo | null
}

export interface ContentPage {
  items: ContentItem[]
  total: number
  offset: number
  limit: number
}

export type KindFilter = '' | 'say' | 'journal'

/** 拼查询串（跳过空值）。 */
function qs(params: Record<string, string | number | undefined>): string {
  const parts: string[] = []
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === '') continue
    parts.push(`${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
  }
  const s = parts.join('&')
  return s ? `?${s}` : ''
}

export function createSay(
  text: string,
  recipientIds: string[],
  mediaIds?: string[],
  weather?: WeatherSnapshotInput
) {
  return request<ContentItem>('/api/contents/says', {
    method: 'POST',
    data: { text, recipientIds, mediaIds: mediaIds ?? [], weather }
  })
}

export function createJournal(
  title: string,
  markdown: string,
  recipientIds: string[],
  weather?: WeatherSnapshotInput
) {
  return request<ContentItem>('/api/contents/journals', {
    method: 'POST',
    data: { title, markdown, recipientIds, weather }
  })
}

export function getContent(id: string) {
  return request<ContentItem>(`/api/contents/${id}`)
}

/** 只传显式要改的字段（null/undefined = 不修改）。 */
export function updateContent(
  id: string,
  payload: { text?: string; title?: string; markdownBody?: string }
) {
  return request<ContentItem>(`/api/contents/${id}`, { method: 'PUT', data: { ...payload } })
}

export function deleteContent(id: string) {
  return request<void>(`/api/contents/${id}`, { method: 'DELETE' })
}

function listContents(path: string, kind: KindFilter, offset: number, limit: number) {
  return request<ContentPage>(`${path}${qs({ kind, offset, limit })}`)
}

export function listMyContents(kind: KindFilter, offset: number, limit: number) {
  return listContents('/api/me/contents', kind, offset, limit)
}

export function listReceived(kind: KindFilter, offset: number, limit: number) {
  return listContents('/api/me/received', kind, offset, limit)
}

export function listUserContents(
  userId: string,
  kind: KindFilter,
  offset: number,
  limit: number
) {
  return listContents(`/api/users/${userId}/contents`, kind, offset, limit)
}

export function searchContents(q: string, kind: KindFilter, offset: number, limit: number) {
  return request<ContentPage>(`/api/search${qs({ q, kind, offset, limit })}`)
}

// ---------- 我的统计（首页 ticket 04） ----------

export interface MeStats {
  totals: {
    says: number
    journals: number
    friends: number
  }
  streak: number
  today: LunarInfo
}

/** GET /me/stats：四卡数字 + 连续记录天数 + 今日农历（后端推导，前端不实现农历）。 */
export function getMyStats() {
  return request<MeStats>('/api/me/stats')
}

// ---------- 好友 ----------

export interface FriendInfo {
  id: string
  nickname: string
  email: string
}

export interface FriendRequestItem {
  id: string
  requester?: AuthorInfo
  addressee?: AuthorInfo
}

export interface FriendRequests {
  incoming: FriendRequestItem[]
  outgoing: FriendRequestItem[]
}

export function listFriends() {
  return request<FriendInfo[]>('/api/friends')
}

export function listFriendRequests() {
  return request<FriendRequests>('/api/friends/requests')
}

export function sendFriendRequest(targetUserId: string) {
  return request<{ id: string }>('/api/friends/requests', {
    method: 'POST',
    data: { targetUserId }
  })
}

export function acceptFriendRequest(id: string) {
  return request<{ ok: boolean }>(`/api/friends/requests/${id}/accept`, { method: 'POST' })
}

export function declineFriendRequest(id: string) {
  return request<{ ok: boolean }>(`/api/friends/requests/${id}/decline`, { method: 'POST' })
}

export function removeFriend(userId: string) {
  return request<void>(`/api/friends/${userId}`, { method: 'DELETE' })
}

// ---------- 评论 ----------

export interface CommentItem {
  id: string
  contentId: string
  author: AuthorInfo
  text: string
  createdAt: string
}

export function listComments(contentId: string) {
  return request<{ items: CommentItem[]; total: number }>(`/api/contents/${contentId}/comments`)
}

export function createComment(contentId: string, text: string) {
  return request<CommentItem>(`/api/contents/${contentId}/comments`, {
    method: 'POST',
    data: { text }
  })
}

// ---------- 站内信 ----------

export interface InboxItem {
  id: string
  type: 'mentioned' | 'commented' | 'friendRequest'
  contentId?: string | null
  contentKind?: 'say' | 'journal' | null
  previewText?: string | null
  createdAt: string
  readAt?: string | null
}

export function listInbox(offset: number, limit: number) {
  return request<{ items: InboxItem[]; unreadCount: number; total: number }>(
    `/api/inbox${qs({ offset, limit })}`
  )
}

export function getUnreadCount() {
  return request<{ unreadCount: number }>('/api/inbox/unread-count')
}

export function markInboxRead(id: string) {
  return request<{ ok: boolean }>(`/api/inbox/${id}/read`, { method: 'POST' })
}

// ---------- PAT ----------

export interface PatItem {
  id: string
  name: string
  createdAt: string
  revokedAt?: string | null
}

export function listPats() {
  return request<{ items: PatItem[]; total: number }>('/api/pats')
}

export function createPat(name: string) {
  return request<{ id: string; name: string; token: string; createdAt: string }>('/api/pats', {
    method: 'POST',
    data: { name }
  })
}

export function revokePat(id: string) {
  return request<{ ok: boolean }>(`/api/pats/${id}`, { method: 'DELETE' })
}

// ---------- 初始化向导 ----------

let setupStatusCache: boolean | null = null

/** GET /setup/status（模块级缓存：初始化是一次性状态，避免各入口页重复探测）。 */
export async function getSetupStatus(): Promise<boolean> {
  if (setupStatusCache !== null) return setupStatusCache
  const res = await request<{ initialized: boolean }>('/api/setup/status')
  setupStatusCache = res.initialized
  return setupStatusCache
}

/** 初始化完成后更新缓存，避免入口守卫再探测。 */
export function markSetupInitialized() {
  setupStatusCache = true
}

/** POST /setup/admin：创建首个管理员并完成初始化（已初始化时后端返回 409）。 */
export function createAdmin(email: string, password: string, nickname: string) {
  return request<{ id: string; email: string; nickname: string }>('/api/setup/admin', {
    method: 'POST',
    data: { email, password, nickname }
  })
}

// ---------- 导入导出 / 批量 @ ----------

export function batchRecipients(contentIds: string[], addRecipientIds: string[]) {
  return request<{ updated: number }>('/api/contents/batch-recipients', {
    method: 'POST',
    data: { contentIds, addRecipientIds }
  })
}
