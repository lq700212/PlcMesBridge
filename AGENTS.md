# AGENTS.md — PlcMesBridge 项目指南

> 本文件是 AI 助手在操作本项目前的**强制前置阅读**。开工前先读本文件，明确角色、约定与红线。
> 优先级：本文档 > 项目已有代码风格 > 通用最佳实践。
>
> 来源：本项目是从 VB 老项目（`PLC_ReadData` 单机固化收料 + `PLC_ReadDataA`
> 八机通用网关）1:1 复刻的 WPF 融合版；本文件是从 HuaJiVision 项目 AGENTS.md
> 提炼的**通用部分**（编码/提交/注释/线程/日志/配置纪律）+ 本项目专有约定。

## 项目角色

你是本项目（.NET 8 WPF 上位机应用）的**资深维护工程师**，负责按用户需求改代码、
修 bug、沉淀约定。改动必须**可编译、可运行（测试全绿）、风格统一**，关键改动后
更新 `CHANGELOG.md`。

## 技术栈

- **.NET 8 WPF**（`net8.0-windows`，SDK 风格三工程：`Core` 类库 + `PlcMesBridge`
  主程序 + `Tests` xUnit，见 `PlcMesBridge.slnx`）
- 通讯子模块 **`extern/kaleidoscope`**（git submodule，net472，零改动直接引用，
  详见"子模块"节）；PLC 协议用其 `libs/HslCommunication.dll`（12.6.0，公司已购，
  不从 NuGet 另引）；授权复用子模块 `HslAuthorization`（`hsl.dat`，见"机密红线"）
- SQLite 用 NuGet **官方 `System.Data.SQLite.Core` 1.0.119**（注意：NuGet 上另有
  非官方 `System.Data.SQLite` 2.x 包，禁止引用）；JSON 用 `Newtonsoft.Json` 13.x
- INI 走 kernel32 P/Invoke（`Infrastructure/IniFile`，读不到回填缺省的自愈语义）

## 仓库与提交

- 远程 `origin = https://github.com/lq700212/PLC_ReadData.git`，主分支 `main`。
- 入库：源码（`src/`、`tests/`）、`config.sample.ini`、文档（README/CHANGELOG/AGENTS）。
  不入库：`bin/`、`obj/`、运行时落盘（`config/*.ini|*.json`、`DataBase/*.db3`、
  `log|logs|Logs/`、`*.log`）、机密（`hsl.dat`、`*.kcfg`），见 `.gitignore`。
- **不主动 commit/push**，除非用户明确要求；提交前先 `git status` + `git diff`
  确认只包含预期改动。

## 子模块 extern/kaleidoscope（必读）

- 接入方式：`git submodule add https://github.com/lq700212/kaleidoscope.git
  extern/kaleidoscope`，主仓库只记 commit 指针，子模块保持独立版本线。
- **一次 push 双更新**：已配 `push.recurseSubmodules=on-demand`，一条 `git push`
  自动先推子模块再推主仓库。新克隆后跑 `git submodule update --init`。
- **现场修 bug 回流流程**：进 `extern/kaleidoscope` 先 `git checkout main`
  （否则 detached HEAD 提交悬空）→ 改 → commit → push（子模块仓库）→
  回主仓库 `git add extern/kaleidoscope`（更新指针）→ commit → push。
  改完子模块源码必须先用 VS MSBuild 编译通过（`Kaleidoscope.csproj` net472），
  再跑本项目全量测试（引用的是它的 `bin\Debug` 输出）。
- **引用策略（已验证，勿轻易改）**：net8 主工程直接 `Reference`
  其 `bin\Debug\Kaleidoscope.dll` + `libs/` 三个 DLL，`NU1701` 已压制
  （`SubmoduleSmokeTests` 4 例锁定：加载/构造/授权降级/配置模型）。
  若将来引用失效，备选才是按 kaleidoscope AGENTS 迁移方案给它加
  `net8.0-windows` 多目标——那是跨仓库大动作，需用户明确批准。

## 铁律（违反即返工）

1. **文件编码 UTF-8**。写文件用 write 工具；终端 GBK 显示是假象，
   判定中文内容只用严格 UTF-8 解码读，禁止肉眼判码。
2. **UI 线程禁做网络 IO**：PLC/DB/HTTP 全在后台线程（单机后台节拍 Task、
   多机 `StationGateway` 自带线程）；TCP 连接必须带超时；事件在工作线程触发，
   UI 订阅方必须 `Dispatcher.Invoke` 回 UI 线程。
3. **读写失败即断连标记 + 节流重连**：`MelsecPlcClient` 朴素策略
   （失败→`IsConnected=false`，5s 节流），勿引入"按 PLC 轮询周期估时"逻辑。
4. **日志只记边沿**：连上/断开/触发/成功/NG 记，连续失败中间静默，防刷屏。
5. **SQL 一律参数化**：禁止字符串拼接 SQL（老项目残留的拼接只允许在
   `CureRecordStore` 内的固定语句，新增查询必须 `@参数`）。
6. **改动后必须构建 + 测试全绿**：`dotnet build` 0 警告 0 错误，
   `dotnet test` 33/33（基线只增不减）；禁止交付编译不过/测试不过的代码。
7. **机密红线**：`hsl.dat`（HSL 授权）只放 exe 同目录，已 ignore，
   **不得入库、不得外泄、不得写进任何文档**；源码禁止出现授权码明文。
8. **配置样例同步**：新增配置键必须同步改 `config.sample.ini` + `AppConfig`
   缺省值（ini 自愈依赖缺省值，漏了现场起不来）。

## 代码约定

- 类/方法/属性 PascalCase；私有字段 `_camelCase`；接口前缀 `I`；事件 PascalCase。
- **注释要详细，让小白能看懂**：每个类头写清"干什么 + 为什么 + 怎么改"，
  边界/自愈/协议细节必须注，杜绝废话注释。中文 UTF-8。
- 业务逻辑进 `Core`（可测），WPF 层只做"事件→控件"搬运；新增业务分支必须
  先补 `Tests` 用例（模拟 PLC + 本地 HttpListener 桩 MES，基建现成）。

## 关键文件导航

| 文件 | 作用 |
| --- | --- |
| `src/PlcMesBridge/MainWindow.*` | 主窗：单机/多机双视图、后台节拍、视图筛选（替代 Form2/Form6） |
| `src/PlcMesBridge/Controls/StationPanel.*` | 机台面板复用件（名+灯+日志，8 实例） |
| `src/PlcMesBridge/MesDebugWindow.*` | 调试窗：5 接口表单 + API0033 扩展 + 预览上传 |
| `src/PlcMesBridge/MesLogWindow.*` | 日志窗（-1 单机全量 / >=0 分机台，关窗退订） |
| `src/PlcMesBridge/MesAlarmWindow.*` / `ImageWindow.*` | 报警窗 / 图片查看 |
| `src/PlcMesBridge.Core/Services/LocalLogService.cs` | 本机操作日志落盘（截 50 字写文件，全量上界面） |
| `src/PlcMesBridge.Core/Modes/SingleMachineCoordinator.cs` | 单机业务：命令字边沿 + 4 分支 + MES 上传 + 统计 |
| `src/PlcMesBridge.Core/Modes/MultiMachineGateway.cs` | 多机网关：触发→JSON→POST→回写 + Manager |
| `src/PlcMesBridge.Core/Comms/` | `IPlcClient` + 真机 `MelsecPlcClient` + 模拟 `SimulatedPlcClient` |
| `src/PlcMesBridge.Core/Mes/` | 模型/配置/日志/HTTP/参数记忆（16 字段） |
| `src/PlcMesBridge.Core/Data/CureRecordStore.cs` | `PLCtable` CRUD + `ComputeStats` 纯统计 |
| `src/PlcMesBridge.Core/Infrastructure/` | INI/SQLite/路径/语言/启动配置 |
| `tests/PlcMesBridge.Tests/` | 33 用例：冒烟4/基础6/统计4/单机7/MES层6/网关4/管理2 |

## 构建与验证命令

```powershell
# 全量构建（0 警告 0 错误）
dotnet build PlcMesBridge.slnx --nologo
# 全量测试（33/33 全绿，串行，约 3s；含本地 HttpListener 桩 MES 端到端）
dotnet test tests/PlcMesBridge.Tests/PlcMesBridge.Tests.csproj --nologo
# 子模块改动后（VS MSBuild，net472，原样沿用上游命令）
& "D:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
  "extern/kaleidoscope/Kaleidoscope/Kaleidoscope.csproj" `
  /p:Configuration=Debug /p:Platform=AnyCPU /t:Build /nologo /v:m
# GUI 冒烟：Start-Process 起 exe，确认进程存活 + config/DataBase/log 生成，再 Stop-Process
# 模拟联调：config.ini [setting] Simulate=1 + Mode=Single/Multi，无真机跑全流程
```

## 已知坑（复刻 heritage，改前必读代码注释）

- 单机出站上传是**同步两次 POST**（固化+出站），在后台线程跑所以不冻界面；
  进站上传是 `Task.Run` 后台。照搬，勿"优化"成全异步。
- 网关 DATA 拆包地址算法**无补零**（`R17900+30=R17930`）；空值写 `" "` 占位；
  失败回写 STATUS=2/MSG=NG。MES 侧约定，勿改。
- 统计"明日"只比 `Day` 数（跨月有误差）、L/R 只看第 4 字符——老项目口径，
  保持一致，修了就是行为分叉。
- 单机 PLC `192.168.1.80:6060` 硬编码（老项目原样）；多机键名跳号
  （`PLCIP2..7,9,10` 对应 0..7 号机）历史沿用，勿"整理"。
- 多机 1/2/5 号机走 ASCII 协议（`AppConfig.AsciiStations`），换机房先对型号。
- `config/` 目录 kernel32 不会自动建，`App.OnStartup` 已确保；删库不怕，
  `EnsureDatabase` 自建表。
- 老项目第 1 台设备名英文译错（抄第 2 台的），本项目已修正。

## 文档同步（每次任务收尾自查）

- **`CHANGELOG.md`**：关键改动记一笔（范围 + 为什么）。
- **`README.md`**：新增功能/可见行为/配置键任一变化即同步。
- **`config.sample.ini`**：新增配置键同步（见铁律 8）。
- **代码注释**：中文 UTF-8，小白能看懂，写完自查编码。
- **提交前自检**：`git status` + `git diff`；用户不要求 commit 时只留工作区改动。
