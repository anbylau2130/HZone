# 15: 整体 e2e 与 UI 测试套件（MVP 验收）

**What to build:** 全部用户故事由进程内 API e2e（WebApplicationFactory）+ Playwright UI e2e 覆盖，全部本地运行（dotnet run + 本地 H5 dev server，无 Docker）。验收 = spec 28 条用户故事全部有测试证据。

**Blocked by:** 11, 13, 14

**Status:** ready-for-agent

- [ ] API e2e：全用户故事场景全绿
- [ ] Playwright UI e2e：核心旅程全绿
- [ ] dotnet test + Playwright 一键跑通
- [ ] MVP 验收：spec 28 条用户故事全部有测试证据
