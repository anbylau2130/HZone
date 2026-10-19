import { ref } from 'vue'
import { defineStore } from 'pinia'
import {
  request,
  getToken,
  getStoredUser,
  saveSession,
  saveUser,
  clearSession,
  type HZUser
} from '../api/client'

export interface RegisterPayload {
  inviteCode: string
  email: string
  password: string
  nickname: string
}

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string>(getToken())
  const user = ref<HZUser | null>(getStoredUser())

  function loadSession() {
    token.value = getToken()
    user.value = getStoredUser()
  }

  async function login(email: string, password: string) {
    const res = await request<{ token: string; expiresAt: string; user: HZUser }>(
      '/api/auth/login',
      { method: 'POST', data: { email, password } }
    )
    saveSession(res.token, res.user)
    token.value = res.token
    user.value = res.user
  }

  async function register(payload: RegisterPayload) {
    return request('/api/auth/register', { method: 'POST', data: { ...payload } })
  }

  async function verifyEmail(t: string) {
    return request<{ ok: boolean }>('/api/auth/verify-email', { method: 'POST', data: { token: t } })
  }

  async function forgotPassword(email: string) {
    return request<{ ok: boolean }>('/api/auth/forgot-password', { method: 'POST', data: { email } })
  }

  async function resetPassword(t: string, newPassword: string) {
    return request<{ ok: boolean }>(
      '/api/auth/reset-password',
      { method: 'POST', data: { token: t, newPassword } }
    )
  }

  async function fetchMe() {
    const me = await request<HZUser>('/api/auth/me')
    user.value = me
    saveUser(me)
    return me
  }

  function logout() {
    clearSession()
    token.value = ''
    user.value = null
  }

  return { token, user, loadSession, login, register, verifyEmail, forgotPassword, resetPassword, fetchMe, logout }
})
