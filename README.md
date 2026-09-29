# MikoBarrier 巫女结界

> **Windows 上的强制自律工具。** 自律期间结束名单里的进程、按域名封锁网站、必要时直接禁用网卡，并吞掉 Alt+Tab / Alt+F4 这类"逃生键"；双进程互保看门狗让它杀不掉。
>
> 但它**不会**阻止你关机或注销，忘记密码还有恢复码兜底——这是用来约束自己的结界，不是把自己锁死。

[![build](https://github.com/xgzshengming/MikoBarrier/actions/workflows/build.yml/badge.svg)](https://github.com/xgzshengming/MikoBarrier/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/xgzshengming/MikoBarrier?label=release&color=D93A2B)](https://github.com/xgzshengming/MikoBarrier/releases/latest)
[![downloads](https://img.shields.io/github/downloads/xgzshengming/MikoBarrier/total?color=D93A2B)](https://github.com/xgzshengming/MikoBarrier/releases)
[![license](https://img.shields.io/github/license/xgzshengming/MikoBarrier?color=D93A2B)](LICENSE)
[![platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)](#系统要求)
[![stars](https://img.shields.io/github/stars/xgzshengming/MikoBarrier?style=social)](https://github.com/xgzshengming/MikoBarrier/stargazers)

**[English](README.en.md)** ·
[常见问题](docs/FAQ.md) ·
[与同类工具对比](docs/对比同类工具.md) ·
[更新日志](CHANGELOG.md) ·
[Gitee 镜像](https://gitee.com/xgzshengming/miko-barrier) ·
[发布与分发](docs/发布与分发.md)

![15 秒演示：展开结界 → Alt+Tab 被吞 → 结束战报](docs/screenshots/demo.gif)

## 快速开始

### 下载

**方式一：夸克网盘（国内推荐，速度快）**

- 链接：<https://pan.quark.cn/s/37cdc61bc577>
- 提取码：`MafQ`
- 分享目录里有整包 zip，也有两个单独的 exe：`MikoBarrier.App.exe` + `MikoBarrier.Guard.exe`（**必须放在同一个目录**）。

**方式二：GitHub Releases（官方源）**

1. 到 [Releases](https://github.com/xgzshengming/MikoBarrier/releases/latest) 下载 `MikoBarrier-<版本>-win-x64.zip`；
2. 解压到任意目录（例如 `D:\MikoBarrier`）；
3. 双击 `MikoBarrier.App.exe`。

**代码镜像**：[Gitee](https://gitee.com/xgzshengming/miko-barrier)（国内访问更快，源码与 GitHub 同步；发版仍以 GitHub Releases 为准）

> 三处文件完全一致，`MikoBarrier-0.6.0-Miko-win-x64.zip` 的 `SHA256`：
>
> ```
> 2cfb772a56bdb38734e0cf9ff3be699b037d1881bd45a5660be8974d95431248
> ```
>
> 想自己核对：PowerShell 里执行 `Get-FileHash .\MikoBarrier-0.6.0-Miko-win-x64.zip -Algorithm SHA256`，结果应与上面一致。网盘链接若失效，请到 [Issues](https://github.com/xgzshengming/MikoBarrier/issues) 反馈。

- 自包含单文件，**不需要预装 .NET 运行时**；
- `MikoBarrier.Guard.exe` 是看门狗，负责自律期间的进程巡逻和 App ↔ Guard 互保，少了它功能不完整；
- 「温和档 / 狠人档」断网和全局键盘封锁需要管理员权限：**系统设置 → 以管理员身份重启**；
- 发布版暂未做代码签名，Windows SmartScreen 可能提示"未知发布者"。请只从 Releases 下载，并核对 `SHA256SUMS.txt`。

### 数据与日志放在哪

程序按这个顺序决定数据根目录 `Root`：

1. 环境变量 `MikoBarrier_HOME`（显式指定，优先级最高，自检时也用它做隔离）；
2. `D:\MikoBarrier` / `F:\MikoBarrier` / `E:\MikoBarrier` 中**已经存在**的那个（尽量不占用 C 盘）；
3. 兜底 `%LocalAppData%\MikoBarrier`。

> 数据目录跟着**这台电脑 + 当前 Windows 用户**走，**不跟 exe 走**：把程序换到别的文件夹、或升级到新版本，统计 / 密码 / 名单都不会丢（同理，解压一份新的到别的目录，读到的还是同一份数据）。想体验全新用户的效果（普通模式 + 新手引导），把环境变量 `MikoBarrier_HOME` 指向一个空目录再启动即可。

| 位置 | 内容 |
| --- | --- |
| `data\config.json` | 密码 / 安全问题哈希、恢复码哈希、名单、主题、策略开关 |
| `data\stats.json` `data\tasks.json` | 统计、待办任务 |
| `data\patrol-log.json` | 拦截战报明细 |
| `data\session.lock` `data\shutdown.flag` | 进行中的自律快照、正常关机标记 |
| `data\incidents\` | 防绕过事件 |
| `data\runtime\` | App ↔ Guard 互保心跳 |
| `data\recovery-code.dat` | DPAPI（CurrentUser）加密的恢复码本机副本 |
| `share\` | 「战绩分享卡」导出的 PNG |
| `logs\` | `app.log` / `guard.log` / `ui-smoke-test.log` |
| `backups\` | 历史快照（含隐私，不会被提交到仓库） |

## 它和普通番茄钟有什么不同

|  | 普通番茄钟 | MikoBarrier |
| --- | --- | --- |
| 计时 | ✅ | ✅ 多轮 / 分轮时长 / 每轮任务 / 结束战报 |
| 拦程序 | ❌ | ✅ 黑名单结束进程，或白名单只放行 |
| 断网 | ❌ | ✅ hosts（温和档）/ 禁用网卡（狠人档） |
| 锁键盘 | ❌ | ✅ 吞掉 Alt+Tab / Alt+Esc / Ctrl+Esc / Alt+F4 / Ctrl+Shift+Esc（Win 键放行） |
| 防杀 | ❌ | ✅ App ↔ Guard 心跳互保，被结束后自动拉起并记 Kill 欠债 |
| 防绕过 | ❌ | ✅ 提前退出欠债、密码冷却、恢复码每日限次、时间回拨保护 |
| 安全出口 | 随时退出 | 关机 / 注销**永远无条件放行**；密码或恢复码可提前结束 |
| 隐私 | 常见云同步 | 无遥测、不联网上传，数据只在本机 |

## 截图

| 主界面 · 巫女模式 | 全屏结界 · 风景背景 |
| --- | --- |
| ![主界面](docs/screenshots/01-focus-miko.png) | ![全屏结界](docs/screenshots/02-overlay-fullscreen.jpg) |

| 全屏结界 · 展开卡片 | 迷你计时条 |
| --- | --- |
| ![全屏结界卡片](docs/screenshots/03-overlay-card.jpg) | ![迷你计时条](docs/screenshots/04-overlay-mini.png) |

| 换装 / 主题 | 修行战绩 |
| --- | --- |
| ![主题设置](docs/screenshots/05-settings-theme.png) | ![修行战绩](docs/screenshots/06-stats.png) |

普通主题下的主界面：[07-focus-normal.png](docs/screenshots/07-focus-normal.png)

## 功能

### 自律

- 预设 5 / 25 / 40 / 60 分钟 + 自定义（1–120 分钟）、多轮（1–12 轮）、中场休息（无 / 5 / 10 分钟）。
- **中断欠债**：主动退出不计入完成次数，剩余时间顺延到下一次的第一轮。
- **中场休息**：自动放开应用屏蔽，但断网档位继续生效。
- 每轮可绑定 0 / 1 / 多个待办任务，支持跨轮复用；同一轮多任务时按任务数均分时长与回合。
- 「每轮单独设置」可为每一轮单独填写 1–120 分钟。
- 结束战报：按任务汇总"A（x 轮 / y 分钟）、B（…）"并累计到任务统计。

### 断网两档

- **温和档**：把网站名单写进 hosts（需要管理员）。
- **狠人档**：hosts + 直接禁用网卡（需要管理员）。
- 开始前确认"一旦开启，休息时间也会断网！"；崩溃 / 重启后自动还原，不会永久断网。

### 密码与找回

- 主密码 16 位（含大小写字母、数字、特殊字符），PBKDF2-SHA512 存储。
- 3 个安全问题，答案忽略大小写与空格、只存哈希；全部答对可重设密码。
- **恢复码**：除哈希外，用 Windows DPAPI（CurrentUser）加密保存本机副本，可在设置页或忘记密码时查看；换 Windows 用户 / 换电脑后无法解密，属预期保护。
- **密码提前退出每日冷却**：第 1 次成功后冷却 1 小时，第 2 次 2 小时，第 3 次后当天冻结（次日 00:00 清零）。
- **恢复码提前退出**：每天 1 次；用恢复码重设密码不限次数，但**不会**解除当天冷却 / 冻结。
- 设置密码处固定提示：**自律开始后无法更改密码，请开启前确认记得密码，以防紧急状况下无法退出。**

### 名单管理（黑 / 白双向）

- 从运行中的程序添加、浏览 exe（可多选）、按文件夹添加（适合 Steam / 游戏目录）。
- 黑名单模式：名单内进程被结束；白名单模式：只放行名单内（及系统保护层）的有窗口程序。
- **一键白名单**：先预览要加入的程序，再走密码门禁；自动去重并跳过系统核心 / 内置组件 / 系统目录程序。
- **白名单体检**：名单页点「白名单体检」或执行 `--whitelist-audit`，输出分类统计与必要宿主自检，报告写入 `logs\whitelist-audit.txt`；也可以把 `app` 目录整体复制到别的电脑，双击 `whitelist-audit.cmd` 做免安装、只读体检（报告在 `audit-home\logs\`）。
- 网站名单：每行一个域名。

<details>
<summary><b>白名单保护模型（点开看细节）</b></summary>

白名单模式的目标是"只允许工作需要的程序，同时绝不把 Windows 锁死"。放行按以下层次判定：

1. **系统核心进程**（`SystemWhitelist`）：explorer、dwm、svchost、输入法、音频引擎、打印 / COM / 计划任务宿主、Defender 等，黑名单 / 白名单模式都永远放行。
2. **内置必需组件**（`ComponentWhitelist`，仅白名单模式生效）：第三方输入法、显卡 / 音频驱动面板、触控板、杀软（火绒 / 360 / 腾讯 / 卡巴 / Avast / Defender 等），避免误伤；黑名单模式仍可由用户主动屏蔽。
3. **Windows 系统目录**（`SystemAllowPolicy`，仅白名单模式生效）：`System32`、`SysWOW64`、`WinSxS`、`SystemApps`、`DriverStore`、`Windows Defender` 等目录里的程序自动放行；`Windows\Temp`、`WindowsApps` 商店应用、下载目录**不在**保护范围内。
4. **用户白名单规则**：文件 / 文件夹 / 进程名三种规则。
5. **启动链路**：白名单里的程序启动的子 / 孙程序一并放行（最多向上追溯 8 层），适配启动器、WebView2、Java / .NET 运行时宿主、反作弊等多进程结构；explorer / cmd / svchost 等系统进程不会成为链路放行根，避免用命令行绕过白名单。

白名单模式只拦截"有窗口"的程序：服务、驱动、COM 宿主、计划任务、DLL 注入载体等后台进程不会被杀。

</details>

### 稳定性与防绕过

- **异常中断**（断电 / 蓝屏 / 无 Kill 事件记录的崩溃）留下快照，下次启动询问"继续还是放弃"；有互保 Kill 事件时自动续跑，不提供放弃入口。
- **App ↔ Guard 双进程互保**：双方每秒写心跳，任何一方被结束 / 失联，另一方几秒内记录 Kill 事件并自动拉起；被 Kill 后 App 带 `--killed-restart` 自动续跑。
- **Kill 递进欠债**：本自然月第 n 次 Kill 罚 5n 分钟（5、10、15…），当月余额封顶 60 分钟；跨月清空。同一会话 15 秒内的 App+Guard 双杀合并算一次。
- **提前退出欠债**：主动中断时把未完成时长记入下一场第一轮，单次封顶 120 分钟，按自然月清空；检测到系统时间回拨时不刷新。
- **关机 / 注销**：先写 `shutdown.flag`、保存状态、还原网络封锁，然后无条件放行，绝不阻拦 Windows。
- 开机自启（可开关）、托盘常驻（可开关）、启动时一次性请求置前（不常驻 Topmost）。
- 统计：今日完成、累计次数、累计自律时长、历史记录、周 / 月汇总、任务累计。

## 巫女模式

全屏计时左下角有一条很淡的「裂缝」，本次计时内累计点击 3 次，会弹出"你引起了巫女小姐的注意！是否进入真·巫女结界？"。确认后：

- 进入巫女模式，裂缝永久隐藏，自动套用第 11 套「真·巫女」主题（白底朱红）；
- 帮助 / 引导逐条重写为巫女口吻（20 个 topic、173 条文案映射）；
- 系统设置 → 外观与启动里可随时切回普通模式。

普通 / 巫女模式都不改变关机、注销、提前结束等安全语义。

## 界面

- 无边框自绘窗口：自绘标题栏 + 最小化 / 最大化 / 关闭，Windows 11 圆角；
- 10 套浅色 / 深色主题 + 解锁后的「真·巫女」，可单独选择界面字体与窗口背景色；
- 自绘下拉框 / 输入框 / 勾选 / 单选 / 对话框，跟随主题；
- 托盘：左键开主界面，右键跟随主题的自绘菜单，关窗静默最小化到托盘；
- 全屏计时：10 张 CC0 / Public Domain 背景图，主卡片可收起成顶部迷你计时条（素材授权见 [docs/素材许可.md](docs/素材许可.md)）。

## 数据与隐私

- **无遥测、不联网上传**；密码 / 安全问题答案只保存 PBKDF2-SHA512 哈希，恢复码本机副本用 DPAPI（CurrentUser）加密。
- `data\`、`logs\`、`backups\` 都在 `.gitignore` 里，不会随源码提交。
- 白名单体检只读取本机进程信息，报告写入本地。

## 常见问题

- **会不会把我锁死？** 不会：关机 / 注销永远无条件放行；提前结束需要密码或恢复码，恢复码在设置页可查看本机副本。
- **忘记密码怎么办？** 用 3 个安全问题重设，或用恢复码重设 / 提前结束。
- **杀软报毒 / SmartScreen 拦截怎么办？** 见 [FAQ](docs/FAQ.md#杀软报毒--smartscreen-拦截怎么办)。
- **能装在别人电脑上管别人吗？** 不行，见下方免责声明；这是给自己用的工具。

更多问题见 **[docs/FAQ.md](docs/FAQ.md)**；和 Cold Turkey、Forest、番茄 ToDo 等的对比见 **[docs/对比同类工具.md](docs/对比同类工具.md)**。

## 开发与自检

环境要求：Windows 10 / 11 + .NET 8 或更新的 SDK（`.NET 10` 可构建 `net8.0-windows`）。没有 SDK 时先运行 `tools\install-dotnet.ps1` 装便携版。

```powershell
# 编译（Core / App / Guard / PatrolVictim）
dotnet build .\MikoBarrier.sln -c Release -v minimal

# 核心逻辑回归（基线 185/185；若检测到 Guard 在运行会安全跳过端到端巡逻）
dotnet run --project .\tools\SmokeTest\SmokeTest.csproj -c Release

# 发布到 .\app\（自包含单文件；该目录已被 .gitignore 排除，分发走 Releases）
powershell -ExecutionPolicy Bypass -File .\tools\publish.ps1

# 界面自检：构造并打开所有窗口 + 功能探针（基线 89 OK / 0 FAIL）
# 注意：会短暂弹出窗口，需可交互桌面；建议隔离 MikoBarrier_HOME
$env:MikoBarrier_HOME = "$env:TEMP\miko-ui-smoke"
.\app\MikoBarrier.App.exe --ui-smoke-test

# 白名单体检（只读、不改配置、不提权）
.\app\MikoBarrier.App.exe --whitelist-audit

# 生成 README 页截图（需要可交互桌面）
powershell -ExecutionPolicy Bypass -File .\tools\screenshots.ps1

# 生成 GitHub 社交预览图 -> docs\social-preview.png
powershell -ExecutionPolicy Bypass -File .\tools\make-social-preview.ps1
```

本机可选环境变量（都不改系统 PATH）：

| 变量 | 作用 |
| --- | --- |
| `MIKOBARRIER_DOTNET` | 指向 `dotnet.exe` 全路径，`tools\publish.ps1` 和开发模式看门狗用它定位主机 |
| `MikoBarrier_HOME` | 强制指定数据根目录；自检用它把数据隔离到临时目录 |
| `MIKOBARRIER_PATROL_VICTIM` | 指定真拦截测试的靶子进程；默认用 `tools\PatrolVictim` 的产物 |
| `NUGET_PACKAGES` | 更换 NuGet 包缓存位置 |

- 贡献前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)；安全漏洞请按 [SECURITY.md](SECURITY.md) 私下报告。
- 更早的交付与自检证据见 [docs/自检与交付记录.md](docs/自检与交付记录.md)；设计文档见 [docs/需求与设计.md](docs/需求与设计.md)。

## 目录结构

```
src\MikoBarrier.App     WPF 界面（自律结界 / 名单管理 / 任务清单 / 自律统计 / 系统设置 / 巫女模式 / 全屏计时 / 托盘 / 自绘控件）
src\MikoBarrier.Core    核心逻辑（状态机、密码与冷却、名单裁决、进程巡逻、互保心跳、Kill 账本、恢复码、网络封锁、任务、崩溃快照）
src\MikoBarrier.Guard   看门狗（进程巡逻 + 互保重启；--patrol / --check / --once）
tools\SmokeTest         核心逻辑回归测试（185 项）
tools\PatrolVictim      真拦截测试用的靶子进程
tools\publish.ps1       发布脚本（自包含单文件 -> app\）
packaging\              winget / scoop 分发清单
docs\                   FAQ、对比、分发、素材授权、设计文档、截图
```

## 系统要求

- Windows 10 / 11（x64）。界面基于 WPF + Win32 自绘，暂不支持 Linux / macOS。
- 运行发布版**不需要**预装 .NET；从源码构建需要 .NET 8 或更新 SDK。
- 「温和档 / 狠人档」断网和「全局键盘封锁」需要管理员权限。

## 卸载与清理

1. 打开 MikoBarrier，在「系统设置」关闭开机自启及提权 / 计划任务相关选项；
2. 从托盘退出程序，删除发布版所在目录；
3. 用过度过断网档位的话，确认退出时已还原 hosts 和网卡（异常残留可在下次启动时自动还原，或手工检查 `C:\Windows\System32\drivers\etc\hosts` 中的 MikoBarrier 标记）；
4. 删除数据根目录（`D:\MikoBarrier` / `F:\MikoBarrier` / `E:\MikoBarrier`，或 `%LocalAppData%\MikoBarrier`）；
5. 提权模式创建过计划任务的话，可在「任务计划程序」删除 `MikoBarrier\App` 与 `MikoBarrier\Guard`，或执行：

   ```powershell
   schtasks /Delete /TN "MikoBarrier\App" /F
   schtasks /Delete /TN "MikoBarrier\Guard" /F
   ```

6. 检查启动文件夹中的 `MikoBarrier.cmd`（`shell:startup`），如仍存在可删除。

## 许可证与免责声明

本项目以 [Apache License 2.0](LICENSE) 开源；第三方组件署名见 [NOTICE](NOTICE)；全屏背景素材来源与授权见 [docs/素材许可.md](docs/素材许可.md)。

MikoBarrier 是**约束自己**的工具，不是家长控制 / 员工监控软件。请只在你自己拥有、且你有权管理的电脑上，对你自己使用。

软件按「原样」提供，不附带任何明示或暗示的担保。作者不对使用或无法使用本软件造成的任何后果负责，包括但不限于：未保存的工作丢失、正当程序被误拦、网络 / 系统设置被修改，以及因强制自律行为导致的任何损失。

> 程序内的**关机 / 注销始终无条件放行**，并会在退出前还原网络封锁；全屏计时中的提前结束需要密码或恢复码。使用前请确认你记得密码，或已保存恢复码。

---

如果这个项目帮到了你，欢迎点个 ⭐ Star——它能让更多和自己拖延较劲的人看到 MikoBarrier。
