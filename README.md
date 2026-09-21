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
```

## 目录

- `src/PlcMesBridge.Core/` — 业务与基础（INI/SQLite/MES/通讯/单机/多机逻辑）
- `src/PlcMesBridge/` — WPF 界面（主窗/调试/报警/日志/图片窗）
- `tests/PlcMesBridge.Tests/` — 33 例 xUnit
- `extern/kaleidoscope/` — 通讯子模块（独立仓库，勿直接改，见 AGENTS.md）
- `config.sample.ini` — 现场配置样例
