using System.IO;
using System.Windows;
using PlcMesBridge.Core.Infrastructure;

namespace PlcMesBridge;

/// <summary>
/// 设置窗（登录通过后弹出：单机/多机二选一）。
/// 打开时按 AppConfig.Mode 预选中；保存走 Core.RunModeSettings.SaveMode 写 ini。
/// 重启由主窗执行（本窗只置 ModeChanged=true 并关闭）：
/// 主窗关窗有"是否退出"确认，直接在本窗调 Shutdown 会被确认框卡住，
/// 造成"旧进程未退 + 新进程已起"的双开 bug，故重启必须回到主窗上下文做。
/// 本窗只做"事件→控件"搬运，读写逻辑在 Core（可测）。
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>本次保存是否切换了模式（主窗据此决定是否自动重启）。</summary>
    public bool ModeChanged { get; private set; }

    /// <summary>PLC 配置是否被改过（主窗据此决定是否自动重启）。</summary>
    public bool PlcChanged { get; private set; }

    /// <summary>任一配置变化都需重启生效（模式切换 / PLC 配置保存）。</summary>
    public bool NeedsRestart() => ModeChanged || PlcChanged;
    public SettingsWindow()
    {
        InitializeComponent();
        Title = LanguageService.Tr("设置");
        LbMode.Content = LanguageService.Tr("运行模式");
        RbSingle.Content = LanguageService.Tr("单机固化收料");
        RbMulti.Content = LanguageService.Tr("多机通用网关");
        LbHint.Content = LanguageService.Tr("切换模式保存后自动重启软件");
        BtnSave.Content = LanguageService.Tr("保存");
        BtnPlc.Content = LanguageService.Tr("PLC配置") + "...";
        BtnCancel.Content = LanguageService.Tr("取消");
        BtnShortcut.Content = LanguageService.Tr("创建桌面快捷方式");
        if (AppConfig.Mode == RunMode.Multi)
            RbMulti.IsChecked = true;
        else
            RbSingle.IsChecked = true;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        RunMode selected = RbMulti.IsChecked == true ? RunMode.Multi : RunMode.Single;
        ModeChanged = RunModeSettings.NeedsRestart(AppConfig.Mode, selected);
        RunModeSettings.SaveMode(AppPaths.ConfigIni, selected);
        if (!ModeChanged)
        {
            MessageBox.Show(LanguageService.Tr("设置已保存"),
                LanguageService.Tr("设置"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// PLC 配置入口（已登录，无需二次验证）：打开 PLC 配置窗；
    /// 若保存了配置，本窗也视为"需重启"，关闭后由主窗统一重启。
    /// </summary>
    private void BtnPlc_Click(object sender, RoutedEventArgs e)
    {
        var w = new PlcConfigWindow { Owner = this };
        if (w.ShowDialog() == true && w.PlcChanged)
            PlcChanged = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 创建桌面快捷方式（本地小文件写，毫秒级，UI 线程直接做，不违反"UI 禁网络 IO"铁律）。
    /// 路径逻辑在 Core.DesktopShortcut（可测），本窗只做"事件→控件"搬运：
    /// 取当前 exe 全路径 + 桌面目录 → 调 EnsureOnDesktop → 按结果弹提示。
    /// </summary>
    private void BtnShortcut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // net8 取当前 exe 全路径（单文件/普通发布都准；极端拿不到才回退基址拼进程名）。
            string exePath = Environment.ProcessPath
                ?? Path.Combine(AppPaths.BaseDir,
                    System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe");
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var (result, path) = DesktopShortcut.EnsureOnDesktop(exePath, desktop);
            string msg = result == ShortcutResult.Created
                ? LanguageService.Tr("桌面快捷方式已创建") + "\n" + path
                : LanguageService.Tr("桌面快捷方式已存在") + "\n" + path;
            MessageBox.Show(msg, LanguageService.Tr("设置"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            // COM 被禁/桌面不可写等：原样报原因，不吞异常（方便现场排查）。
            MessageBox.Show(LanguageService.Tr("发生错误:") + ex.Message,
                LanguageService.Tr("设置"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
