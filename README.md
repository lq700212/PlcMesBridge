# PlcMesBridge（WPF 融合版）

从 VB 老项目（`PLC_ReadData` 单机固化收料 + `PLC_ReadDataA` 八机通用网关）
1:1 复刻的 .NET 8 WPF 程序，单/多机可配置切换。详见 `AGENTS.md`（开工必读）
与 `CHANGELOG.md`。

## 快速开始

```powershell
# 1. 拉代码（含子模块）
git clone https://github.com/lq700212/PLC_ReadData.git
cd PLC_ReadData
git submodule update --init

# 2. 编译子模块（net472，需 VS MSBuild）
& "D:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
  "extern/kaleidoscope/Kaleidoscope/Kaleidoscope.csproj" `
  /p:Configuration=Debug /p:Platform=AnyCPU /t:Build /nologo /v:m

# 3. 全量构建 + 测试
dotnet build PlcMesBridge.slnx --nologo
dotnet test tests/PlcMesBridge.Tests/PlcMesBridge.Tests.csproj --nologo

# 4. 模拟联调（无真机）：复制 config.sample.ini 为
#    src/PlcMesBridge/bin/Debug/net8.0-windows/config/config.ini，
#    设 Mode=Single/Multi + Simulate=1，启动 PlcMesBridge.exe
#    也可在界面点"运行模式"按钮（先点"登录"）切换单机/多机，保存后重启生效
```

## 架构（MVVM）

- View（`src/PlcMesBridge/*.xaml` 纯绑定）← ViewModel（`ViewModels/`：
  状态 + 命令，`ViewModelBase` 管通知与跨线程封送）→ Model/Core
  （业务，可测）+ `IDialogService`（VM 开窗/提示唯一出口）。
- xaml.cs 只做三件事：设 DataContext、PasswordBox 密码转交（不可绑定）、
  转发关窗；VM 禁止碰 Window/MessageBox/Dispatcher；后台节拍经
  `UiInvoke` 回 UI 线程。

## 界面设置

- 菜单栏"登录"按钮 → 登录窗（两档账号：admin 普通管理，
  dev 最高权限；"记住密码"按账号各记各的 DPAPI 密文，用户名框改谁就跟出谁的）。
  已登录再点可退出或切换账号；运行模式/设置子项未登录会先提示登录。
  账号存数据库 users 表（PBKDF2 哈希，无明文），上线前必须改掉初始密码。
- 菜单栏"运行模式"按钮（登录后可点） → 模式切换窗单机/多机二选一 → 保存：
  模式变了自动重启生效，没变只提示已保存。
- 菜单栏"设置"下拉：admin 见"修改密码" + "创建桌面快捷方式"，
  dev 另见"PLC设置"。"修改密码"自己改自己（dev 可下拉代改他人），
  新密码≥6 位。
  "PLC设置" → PLC 配置窗（三页：单机PLC / 多机PLC /
  接口地址）：IP/端口/命令地址全界面改，"测试连接"拿表单值直测
  （不用先保存），"保存并重启"先校验格式、错当场拦，通过后自动重启生效。
  去现场只点鼠标，不改代码不改 ini。
- "创建桌面快捷方式"：一键在桌面建 `.lnk`（图标取 exe 内嵌图标，
  已存在不覆盖只提示路径）。exe/任务栏/全部 8 个窗体左上角图标统一为
  `src/PlcMesBridge/Assets/app.ico`，换图标只换该文件重新编译。
- 风格：天蓝色小清新全局样式（`src/PlcMesBridge/Styles/FreshBlue.xaml`）。

## 目录

- `src/PlcMesBridge.Core/` — 业务与基础（INI/SQLite/MES/通讯/单机/多机逻辑）
- `src/PlcMesBridge/` — WPF 界面（主窗/调试/报警/日志/图片窗），
  图标在 `Assets/`（`app.ico` 程序图标 / `app.png` 高清源备用）
- `tests/PlcMesBridge.Tests/` — 133 例 xUnit（`Mocks/`：本地桩 MES /
  标准报文与单机预置 / 静态全局快照恢复 / Fake 对话框，下次加用例直接复用）
- `extern/kaleidoscope/` — 通讯子模块（独立仓库，勿直接改，见 AGENTS.md）
- `config.sample.ini` — 现场配置样例
