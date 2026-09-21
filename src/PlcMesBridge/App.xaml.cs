using System.IO;
using System.Windows;
using PlcMesBridge.Core.Data;
using PlcMesBridge.Core.Infrastructure;

namespace PlcMesBridge;

/// <summary>
/// 程序入口装配（对应老项目 Form1_Load 的前半：读配置→建库→起主窗）。
/// 顺序：定基址 → 读 config.ini（语言/Mode/PLC/MES）→ 建 SQLite 库表 → 起主窗。
/// config 缺键自动回填缺省（IniFile 自愈），现场删配置不怕。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        // kernel32 写 INI 不会自动建目录（老项目靠现场预置 config/），此处确保。
        Directory.CreateDirectory(AppPaths.ConfigDir);
        AppConfig.Load();
        MesParamStoreLoad();
        new SqliteHelper(AppPaths.DatabaseFile).EnsureDatabase();
    }

    private static void MesParamStoreLoad()
    {
        // AppConfig.Load 内已调 MesParamStore.LoadParams，此处占位说明装配链完整。
    }
}
