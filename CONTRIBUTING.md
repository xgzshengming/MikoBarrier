# 参与贡献

感谢你对 MikoBarrier 感兴趣。提交 issue 或 PR 之前，请先花几分钟阅读下面的说明。

## 项目定位与安全边界

MikoBarrier 是**给自己用的强制自律工具**，不是家长控制 / 员工监控软件。它会在用户明确开启自律后：

- 安装全局键盘钩子，拦截 `Alt+Tab`、`Alt+Esc`、`Ctrl+Esc`、`Alt+F4`、`Ctrl+Shift+Esc` 等组合键（`Win` 键保留放行）；
- 结束黑名单中的前台进程；
- 必要时修改 `hosts`、禁用网卡；
- 写入开机启动项 / 计划任务，并以管理员权限运行部分功能。

所有贡献都必须保留以下底线：

- **关机 / 注销始终无条件放行**；
- 崩溃或异常退出后应尽量还原网络封锁和系统状态；
- 不引入内核驱动、不绕过 Windows 安全机制；
- 不把工具改造成可以偷偷监控他人的软件。

## 开发环境

- Windows 10 / 11 x64；
- .NET 8 或更新的 SDK（例如 .NET 10；`global.json` 已允许 `latestMajor`），或先运行 `tools\install-dotnet.ps1` 安装便携版；
- 推荐使用 Visual Studio 2022 或 VS Code + C# Dev Kit。

仓库根目录下常用命令：

```powershell
# 构建（Core / App / Guard / PatrolVictim）
dotnet build .\MikoBarrier.sln -c Release -v minimal

# 核心逻辑回归（发布前请确保全绿）
dotnet run --project .\tools\SmokeTest\SmokeTest.csproj -c Release

# 生成发布版到 .\app\（自包含单文件）
powershell -ExecutionPolicy Bypass -File .\tools\publish.ps1
```

## 自检要求

- **核心逻辑改动**：必须跑 `tools\SmokeTest`，PR 中说明结果；如果无法运行，请在 PR 里写明确原因。
- **UI 改动**：在隔离数据目录下运行 UI 自检：
  ```powershell
  $env:MikoBarrier_HOME = Join-Path $env:TEMP ('MikoBarrier-UI-' + [guid]::NewGuid())
  .\app\MikoBarrier.App.exe --ui-smoke-test
  ```
- **键盘钩子 / 进程巡逻 / 网络 / 提权改动**：除了 SmokeTest，还应在注释里说明你实际验证过的场景和还原方法。
- 运行测试前请先退出正在自律的 MikoBarrier，避免自检程序误杀真实看门狗或写入真实配置。

## 代码与文档约定

- 语言：C# 12 / .NET 8 / WPF；已有代码以文件作用域命名空间、可空引用类型和集合表达式为主，请保持一致。
- 注释：解释“为什么”，不要复述代码；涉及系统行为时写清楚权限要求和还原方式。
- 提交信息：推荐 Conventional Commits，例如 `fix(guard): ...`、`docs: ...`。
- 不要在仓库里提交任何真实个人数据，包括但不限于：
  - `data\`、`logs\`、`backups\`、`app\` 下的运行时文件；
  - 密码、恢复码、安全问题答案；
  - 真实进程名 / 文件路径 / 用户名 / 邮箱。
- 新增第三方代码或素材：必须同步更新 `NOTICE` 和 `docs\素材许可.md`，并确认许可证允许再分发。

## 提交 PR

1. 从 `main` 创建分支，尽量保持一个 PR 只做一件事；
2. 按上面的要求完成构建和自检；
3. 在 PR 描述中写清楚：变更内容、关联 issue、测试结果、已知限制；
4. 如果修改了用户可见行为，请同时更新 `README.md` 和 `CHANGELOG.md`。

## 安全漏洞

请**不要**用公开 issue 报告安全漏洞。报告方式见 [SECURITY.md](SECURITY.md)。

## 行为准则

参与本项目即表示你同意遵守 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)。
