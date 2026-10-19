import { defineConfig, devices } from '@playwright/test'

// UI e2e 全部本地运行：API(dotnet run :5000) + H5(pnpm dev:h5 :5173)，无 Docker（PRD/用户要求）
export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure'
  },
  expect: { timeout: 15_000 },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } }
  ],
  webServer: [
    {
      command: 'cd ../src/HZone.Api && dotnet run --no-build',
      url: 'http://localhost:5000/health',
      reuseExistingServer: true,
      timeout: 120_000
    },
    {
      command: 'cd ../frontend && pnpm dev:h5',
      url: 'http://localhost:5173',
      reuseExistingServer: true,
      timeout: 120_000
    }
  ]
})
