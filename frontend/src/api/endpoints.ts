/**
 * 后端 API 薄封装：全部走 client.ts 的 request（Bearer 自动带上，401 统一跳登录）。
 * DTO 形状与后端 ContentContracts.cs / CommentInboxEndpoints.cs 等一一对应（camelCase）。
 */
import { request, uploadFile } from './client'

// ---------- 内容 ----------

export interface AuthorInfo {
  id: string
  nickname: string
  /** 票 05 接真头像 */
  avatarUrl?: string | null
  /** 签名（moments-wechat 票 01 下发） */
  signature?: string | null
  /** 封面 /media/ 相对路径（moments-wechat 票 01 下发） */
  coverUrl?: string | null
}

export interface RecipientInfo {
  userId: string
  nickname: string
}

export interface MediaItem {
  id: string
  kind: 'image' | 'video' | 'audio' | 'file'
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

/** 只传显式要改的字段（null/undefined = 不修改）；mediaIds/recipientIds/weather 传了即整体替换。 */
export function updateContent(
  id: string,
  payload: {
    text?: string
    title?: string
    markdownBody?: string
    mediaIds?: string[]
    recipientIds?: string[]
    weather?: WeatherSnapshotInput
  }
) {
  return request<ContentItem>(`/api/contents/${id}`, { method: 'PUT', data: { ...payload } })
}

export function deleteContent(id: string) {
  return request<void>(`/api/contents/${id}`, { method: 'DELETE' })
}

function listContents(
  path: string,
  kind: KindFilter,
  offset: number,
  limit: number,
  date?: string,
  q?: string
) {
  return request<ContentPage>(`${path}${qs({ kind, offset, limit, date, q: q || undefined })}`)
}

/** date=yyyy-MM-dd：热力格点格下钻按天过滤（后端按中国区本地日取 UTC 区间）。 */
export function listMyContents(
  kind: KindFilter,
  offset: number,
  limit: number,
  date?: string
) {
  return listContents('/api/me/contents', kind, offset, limit, date)
}

export function listReceived(kind: KindFilter, offset: number, limit: number, q?: string) {
  return listContents('/api/me/received', kind, offset, limit, undefined, q)
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

export interface HeatmapCell {
  date: string
  count: number
}

export interface Footprint {
  city: string
  firstAt: string
}

export interface MeStats {
  totals: {
    says: number
    journals: number
    friends: number
  }
  streak: number
  today: LunarInfo
  /** 近 90 天（含今天）按天计数，缺天补 0，恒 90 项旧→新。 */
  heatmap: HeatmapCell[]
  /** 天气快照城市足迹（同城连续去重），倒序最近在上。 */
  footprints: Footprint[]
  /** 月度回顾（中国区本月）：篇数/有天气的不同城市数/最常天气现象（null 可）。 */
  monthly: {
    says: number
    journals: number
    cities: number
    topCondition: string | null
  }
}

/** GET /me/stats：四卡数字 + 连续记录天数 + 今日农历 + 热力格 + 足迹 + 月度回顾（后端推导）。 */
export function getMyStats() {
  return request<MeStats>('/api/me/stats')
}

// ---------- 那年今天 / 互动摘要（homepage-v2 ticket 06） ----------

export interface OnThisDayItem {
  id: string
  kind: 'say' | 'journal'
  title: string | null
  summary: string | null
  year: number
}

/** GET /me/on-this-day：往年同月同日的内容（倒序限 5 条），无则空数组。 */
export function getOnThisDay() {
  return request<OnThisDayItem[]>('/api/me/on-this-day')
}

export interface InteractionSummary {
  comments: number
  mentions: number
}

/** GET /me/interactions/summary：近 7 天我内容收到的评论数 + 我被 @ 的内容数。 */
export function getInteractionSummary() {
  return request<InteractionSummary>('/api/me/interactions/summary')
}

// ---------- 好友 ----------

export interface FriendInfo {
  id: string
  nickname: string
  email: string
  avatarUrl?: string | null
  signature?: string | null
  coverUrl?: string | null
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

// ---------- 头像（composer-polish 票 05） ----------

/** POST /me/avatar：multipart 上传图片，服务端压缩存储并更新头像；返回 /media/ 相对路径。 */
export function uploadAvatar(file: string | Blob) {
  return uploadFile<{ avatarUrl: string }>('/api/me/avatar', file)
}

/** DELETE /me/avatar：清空头像 → 204。 */
export function clearAvatar() {
  return request('/api/me/avatar', { method: 'DELETE' })
}

// ---------- 个人资料（moments-wechat 票 01/02） ----------

/** PUT /me/profile：整体替换签名（null = 清空），≤100 字。 */
export function updateMyProfile(signature: string | null) {
  return request<{ signature: string | null }>('/api/me/profile', {
    method: 'PUT',
    data: { signature }
  })
}

/** POST /me/cover：multipart 上传封面图，服务端压缩存储并更新封面；返回 /media/ 相对路径。 */
export function uploadCover(file: string | Blob) {
  return uploadFile<{ coverUrl: string }>('/api/me/cover', file)
}

/** DELETE /me/cover：清空封面 → 204。 */
export function clearCover() {
  return request('/api/me/cover', { method: 'DELETE' })
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

// ---------- 播放列表（账号级，每用户一张）+ 音频库 ----------

export interface PlaylistItem {
  id: string
  sourceKind: 'media' | 'url'
  title: string
  url: string | null
  mediaAssetId: string | null
  /** media 项的资产文件名（拼 /media/{storedName} 播放路径，前端 blob 换取用）。 */
  mediaStoredName: string | null
  sortOrder: number
  createdAt: string
}

export interface AudioAssetInfo {
  id: string
  originalName: string
  storedName: string
  sizeBytes: number
  createdAt: string
}

export function listPlaylist() {
  return request<{ items: PlaylistItem[]; total: number }>('/api/me/playlist')
}

export function addPlaylistItem(payload: {
  sourceKind: 'media' | 'url'
  title?: string
  url?: string
  mediaAssetId?: string
}) {
  return request<PlaylistItem>('/api/me/playlist', { method: 'POST', data: payload })
}

/** 整体重排：完整 id 序列（与现集合一致），服务端按下标重编顺序。 */
export function reorderPlaylist(ids: string[]) {
  return request<{ ok: boolean }>('/api/me/playlist/reorder', { method: 'PUT', data: { ids } })
}

export function removePlaylistItem(id: string) {
  return request<{ ok: boolean }>(`/api/me/playlist/${id}`, { method: 'DELETE' })
}

/** 音频库：本人上传的音频资产（新→旧），播放列表「从音频库添加」用。 */
export function listMyAudioAssets() {
  return request<{ items: AudioAssetInfo[]; total: number }>('/api/me/media')
}

// ---------- 服务器音乐库（admin 配置磁盘目录，全员默认曲库） ----------

export interface ServerAudioFile {
  name: string
  sizeBytes: number
}

export function getServerMusicLibrary() {
  return request<{ musicDir: string | null; files: ServerAudioFile[] }>(
    '/api/me/player/server-library'
  )
}

/** 配置/清除服务器音乐目录（空串清除；仅 admin，目录必须已存在）。 */
export function setServerMusicDir(musicDir: string) {
  return request<{ musicDir: string | null }>('/api/player/music-dir', {
    method: 'PUT',
    data: { musicDir }
  })
}
