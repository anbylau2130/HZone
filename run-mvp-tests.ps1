# HZone MVP 一键验收：进程内 API e2e（dotnet test）+ Playwright UI e2e，全部本地运行（无 Docker）。
# 用法：powershell -File run-mvp-tests.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host "=== [1/2] dotnet test（API e2e，103 项） ===" -ForegroundColor Cyan
dotnet test "$root/HZone.sln" --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "dotnet test 失败" -ForegroundColor Red; exit 1 }

Write-Host "=== [2/2] Playwright UI e2e（12 项核心旅程） ===" -ForegroundColor Cyan
Push-Location "$root/e2e"
try {
  npx playwright test
  if ($LASTEXITCODE -ne 0) { throw "playwright test 失败" }
} finally {
  Pop-Location
}

Write-Host "=== MVP 验收全绿 ===" -ForegroundColor Green
