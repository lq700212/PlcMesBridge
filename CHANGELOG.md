# CHANGELOG — PlcMesBridge

> VB 老项目 1:1 复刻的 WPF 融合版。版本规则：关键行为变化即记一笔。

## V0.0.5（2026-09-23，主窗菜单栏布局）

- 主窗操作按钮（连接/日志/调试/语言/设置）从左侧按钮堆搬到顶部共享
  菜单栏（单机/多机共用，连接按模式分发；时钟并入菜单栏右侧）。
- 单机左列改为纵向状态带 → 统计带 → 日志（原来按钮区位置），右产品表
  高度拉满（行高 30）；多机顶栏只留视图筛选 + 扫描周期。
- 顶菜单项换肤 `FreshMenuButton`（白底小药丸 + 浅蓝边 + 深蓝字，
  悬停变蓝底白字），离屏渲染目检通过。
- 状态带 / 统计带分区卡片化：浅蓝 `FreshHintBorder` 运行状态卡 +
  白底 `FreshCardBorder` 统计卡（新增复用样式键）+ 深蓝小标题，
  真实主窗离屏渲染目检通过。
- 验证：构建 0 警告 0 错误，测试 111/111，GUI 冒烟通过。

## V0.0.4（2026-09-23，PLC 可配置：现场零写代码）

- 新增 PLC 配置窗（三页：单机PLC / 多机PLC / 接口地址）：
  设置窗 → "PLC配置..." → 填表 → "测试连接"（拿表单值直测，不用先保存）
  → "保存并重启"（先校验，错当场拦）→ 主窗自动重启生效。
- 单机 11 处硬编码地址收敛进 `SinglePlcConfig`（IP/端口/命令/完成/心跳/
  固化/进入/PC时间/库位/产品区算法参数/报警位），缺省=老项目硬编码，
  老 ini 无新键行为零变化；报警位读链：新键 → 老 `MESPLCALarm` → D30013。
- 多机端口（老项目写死 6060）与协议（老项目写死 1/2/5 号 ASCII）进 ini
  （`PLCPorts`/`PLCAscii`），改后点"连接设备"重连生效；触发/地址束改后
  下次扫描（200ms）即生效。
- 新增 `PlcConnectionTester`（连通 + 读回验证，超时 3s，后台跑不冻界面）。
- 测试 95→111：`PlcConfigTests` 10（缺省/自愈/往返/校验/读链）/
  `CoordinatorCustomAddrTests` 3（换 D500xx 全链路，防写回硬编码）/
  `ConnectionTesterTests` 3（成功/失败链）；中英覆盖扩展 PLC 新键。
- 验证：构建 0 警告 0 错误，测试 111/111，GUI 冒烟通过。

## V0.0.3（2026-09-22，全量走查 + 测试加固 42→95）

- 走查修 4 个 bug：
  （1）`AppConfig.Load` 的 `DataLength` 用 `int.Parse`，ini 手改错一个字符
  启动即崩——改为容错解析，非法/非正数回填 799；
  （2）`IniFile.ReadInt/ReadByte` 自愈按 `"#0.000"` 写（如 "799.000"），
  下次自己解析不了——改为纯整数写，自愈一次就好（`ReadDouble` 保持三位小数）；
  （3）`StationGateway.ShiftAddr` 大小写敏感 + `int.Parse` 无保护——改为
  大小写不敏感、非法地址返回原值不抛（合法输入与老项目逐字一致）；
  （4）`MultiMachineManager.ConnectAll` 失败网关也进 `Stations`——改为当场
  释放、不进列表（`Stations` 只收连上且线程已起的）。
- `LocalLogService` 从 WPF 层搬进 `Core/Services`（纯逻辑无界面依赖，
  按"业务进 Core"约定；`MainWindow` 只改 using，行为不变）。
- `MesLogger` 新增 `ClearForTests()`（仅清内存缓存，供测试隔离，生产不调）。
- 测试 42→95（新增 53 例，全部串行约 7s）：沉淀 `tests/.../Mocks/`
  （`MesStubServer` 本地桩 MES + `PlcScenarios` 标准报文/单机预置 +
  `TestScope` 静态全局快照恢复 + 落盘重定向临时目录，下次直接复用）；
  新增 `AppConfigTests` 7 / `IniNumberFormatTests` 4 / `ShiftAddrRobustTests` 4 /
  `MesLoggerTests` 5 / `LocalLogServiceTests` 4 / `SingleMachineEdgeTests` 9
  （桩 MES 精确断言进站 ACTION=I、出站两次 POST）/ `GatewayExtendedTests` 10
  （0031 拆包/HTTP500/空 URL/空格占位/双触发单次处理）/ `SimAndHttpTests` 8 /
  `LanguageCoverageTests` 2（UI 常用键英文全覆盖）。
- 验证：`dotnet build` 0 警告 0 错误，`dotnet test` 95/95（连跑两轮稳定），
  GUI 冒烟（exe 存活 + config 自愈 + 建库）通过。

## V0.0.2（2026-09-22，界面设置切换模式）

- 主窗新增"设置"按钮（单机/多机视图各一）：点击先弹登录窗
  （账号 admin / 密码 123456，本地防误触），通过后弹设置窗。
- 设置窗单机/多机二选一，保存写入 `config.ini [setting] Mode`，
  模式变了主窗自动重启（先拉新进程再退当前进程，并跳过"是否退出"确认，
  否则确认框卡住旧进程会双开；没变只提示已保存）。
- 登录窗密码下方加"记住密码"：勾选存账号到 ini 下次预填，取消即清。
- 逻辑进 Core（`RunModeSettings` 纯函数：登录校验/解析/落盘/重启判断/登录记忆），
  新增 `RunModeSettingsTests` 9 例，测试 42/42。
- 换肤：天蓝色小清新全局样式（`Styles/FreshBlue.xaml`，对标 AgingTestSystem：
  白底+微软雅黑+DodgerBlue 主按钮+浅蓝提示条），同行控件统一上下居中。

## V0.0.1（2026-09-21，复刻基线）

- 单机/多机融合成一个程序，`config.ini [setting] Mode=Single/Multi` 切换；
  新增 `[setting] Simulate=1` 内存模拟联调（无真机跑全流程）。
- 复刻范围：单机版 Form1（含 4 分支命令字、160 行产品表、统计、进出站 MES）、
  多机版通用网关（6 触发→JSON→POST→回写，含 0030/0031 拆包）、调试窗
  （追加 API0033）、报警窗、日志窗（单机全量/分机台）、图片查看。
- 补齐（相对老项目残缺）：Hsl 引用转正（走子模块 `libs`，不再靠 bin 残留）；
  Form2/Form6 死窗体改为视图筛选下拉；D22xx 注释代码不复刻。
- 修正：（1）第 1 台设备名英文误译（抄第 2 台）；（2）节拍移后台线程
  （老项目 UI 线程跑 160 次 PLC 读会冻界面）；（3）连接按钮后台化；
  （4）`SQLiteHalperObj` 拼写修正为 `SqliteHelper`；（5）统计短 ID 归 R
  （老项目会抛异常）。
- 通讯：`extern/kaleidoscope` 子模块零改动直引（net472→net8 已验证），
  `push.recurseSubmodules=on-demand` 一次 push 双更新；HSL 授权复用子模块
  `hsl.dat` 机制；SQLite 用官方 `System.Data.SQLite.Core` 1.0.119。
- 测试 33 例全绿：冒烟4/基础6/统计4/单机7/MES层6/网关4（本地桩 MES 端到端）/管理2。
