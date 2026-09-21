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
    public SettingsWindow()
    {
        InitializeComponent();
        Title = LanguageService.Tr("设置");
        LbMode.Content = LanguageService.Tr("运行模式");
        RbSingle.Content = LanguageService.Tr("单机固化收料");
        RbMulti.Content = LanguageService.Tr("多机通用网关");
        LbHint.Content = LanguageService.Tr("切换模式保存后自动重启软件");
        BtnSave.Content = LanguageService.Tr("保存");
        BtnCancel.Content = LanguageService.Tr("取消");
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

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
