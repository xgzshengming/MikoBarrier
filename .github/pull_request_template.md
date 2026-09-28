## 变更内容

<!-- 简要说明这个 PR 做了什么、为什么这么做。 -->

## 关联 Issue

<!-- 例如：Closes #12；没有就写“无”。 -->

## 自检

- [ ] `dotnet build .\MikoBarrier.sln -c Release`
- [ ] `dotnet run --project .\tools\SmokeTest\SmokeTest.csproj -c Release`
- [ ] 如果改了 UI：在隔离数据目录下运行过 `.\app\MikoBarrier.App.exe --ui-smoke-test`
- [ ] 如果改了键盘钩子 / 进程巡逻：额外运行过 `--keyboard-test` 或说明未运行原因
- [ ] 如果改了网络 / 提权 / 计划任务：说明测试环境和还原步骤
- [ ] 没有提交 `data\`、`logs\`、`backups\`、`app\` 或被 `.gitignore` 排除的本机文件
- [ ] 没有提交真实密码、恢复码、邮箱、本机用户名或绝对路径

## 安全与兼容性

- [ ] 不会阻止或明显拖延 Windows 正常关机 / 注销
- [ ] 异常退出时会尽量还原网络 / 键盘钩子等本机状态
- [ ] 已阅读 [SECURITY.md](../SECURITY.md) 中的安全边界说明

## 备注

<!-- 给维护者的额外信息、已知限制、后续计划。 -->
