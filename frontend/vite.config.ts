import { defineConfig } from 'vite'
import uni from '@dcloudio/vite-plugin-uni'

export default defineConfig({
  plugins: [uni()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        // 后端路由无 /api 前缀，前端统一以 /api/* 调用，代理时剥掉前缀
        rewrite: (path) => path.replace(/^\/api/, '')
      },
      // 媒体直读（GET /media/*）：后端路由同样无前缀，原样转发
      '/media': {
        target: 'http://localhost:5000',
        changeOrigin: true
      }
    }
  }
})
