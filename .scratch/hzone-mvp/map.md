# HZone MVP 施工档案（Ralph 循环进度）

> 每轮迭代开始时先读本文件 + `git log`，继续 frontier 上的 ticket；完成后更新本文件并提交。

## 状态速览

- **Ticket 01 ✅** 解决方案骨架 + /health + WAF e2e 模式（commit 66cd602）
- **Ticket 02 ✅** 领域实体+EF Core+可见名单单一入口+JWT 管线+可见性 e2e（commit 09e9ce2，5/5 绿）
- **Ticket 03 ✅** 初始化向导+系统配置标记+未初始化 409 防护（commit 675db97，10/10 绿）
- Ticket 03~15：待办（依赖顺序见 issues/*.md 的 Blocked by）
- 前端：uni-app(Vue3) 脚手架已建（frontend/，npm 走 npmmirror，@dcloudio 版本 3.0.0-5020620260917001，vite 5.2.8，vue-i18n+pinia 已装），H5 dev 验证待做
- 环境注意：
  - GitHub 直连失败（degit/clone TLS error）→ npm 走 npmmirror registry
  - 主会话直接 Write 长文件会随机乱码 → 优先委托 executor 子代理写代码，用 dotnet test 兜底
  - SDK 11 RC 装过但 global.json 锁 8.0.426，TFM 必须 net8.0，模板生成的 MapOpenApi（.NET 9 API）不可用
  - 解决方案文件是 HZone.sln

## 验证口径

- 后端：`dotnet test`（WebApplicationFactory 进程内 e2e，HTTP 边界 seam）
- 前端 H5：`pnpm -C frontend dev:h5`（:5173，/api 代理到 :5000）+ Playwright（ticket 15）
- 全部本地，无 Docker
