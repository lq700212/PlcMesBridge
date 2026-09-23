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
#    也可在界面点"设置"按钮（登录 admin/123456）切换单机/多机，重启生效
```

## 界面设置

- 主窗（单机/多机视图）均有"设置"按钮 → 登录窗（admin/123456，
  可勾"记住密码"下次预填） → 设置窗单机/多机二选一 → 保存：
  模式变了自动重启生效，没变只提示已保存。
- 设置窗 → "PLC配置..." → PLC 配置窗（三页：单机PLC / 多机PLC /
  接口地址）：IP/端口/命令地址全界面改，"测试连接"拿表单值直测
  （不用先保存），"保存并重启"先校验格式、错当场拦，通过后自动重启生效。
  去现场只点鼠标，不改代码不改 ini。
- 设置窗 → "创建桌面快捷方式"：一键在桌面建 `.lnk`（图标取 exe 内嵌图标，
  已存在不覆盖只提示路径）。exe/任务栏/全部 8 个窗体左上角图标统一为
  `src/PlcMesBridge/Assets/app.ico`，换图标只换该文件重新编译。
- 风格：天蓝色小清新全局样式（`src/PlcMesBridge/Styles/FreshBlue.xaml`）。

## 目录

- `src/PlcMesBridge.Core/` — 业务与基础（INI/SQLite/MES/通讯/单机/多机逻辑）
- `src/PlcMesBridge/` — WPF 界面（主窗/调试/报警/日志/图片窗），
  图标在 `Assets/`（`app.ico` 程序图标 / `app.png` 高清源备用）
- `tests/PlcMesBridge.Tests/` — 111 例 xUnit（含 `Mocks/` 复用基建：本地桩 MES /
  标准报文与单机预置 / 静态全局快照恢复，下次加用例直接复用）
- `extern/kaleidoscope/` — 通讯子模块（独立仓库，勿直接改，见 AGENTS.md）
- `config.sample.ini` — 现场配置样例
