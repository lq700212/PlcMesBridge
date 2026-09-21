# CHANGELOG — PlcMesBridge

> VB 老项目 1:1 复刻的 WPF 融合版。版本规则：关键行为变化即记一笔。

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
