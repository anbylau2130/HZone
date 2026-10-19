# 01: 解决方案骨架与测试链路打通

**What to build:** 一个能跑的 .NET 8 解决方案骨架：Api（ASP.NET Core 最小 API）+ Core（领域）+ Infrastructure（EF Core）+ Tests（xUnit）。`GET /health` 返回 200，一条 xUnit 测试通过 `WebApplicationFactory` 进程内起真实 API 断言 `/health`——建立 e2e 测试模式（HTTP 边界 seam）。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [x] `dotnet run` 本地启动，`/health` 返回 200
- [x] xUnit + WebApplicationFactory e2e 测试通过（真实 DI/管道，非 mock 管道）
- [x] `dotnet test` 全绿
