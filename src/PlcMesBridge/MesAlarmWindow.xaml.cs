using System.Windows;
using PlcMesBridge.Core.Infrastructure;

namespace PlcMesBridge;

/// <summary>报警窗（复刻 FormMESAlarm.ShowAlarm：设文本+名称，非模态置顶）。</summary>
public partial class MesAlarmWindow : Window
{
    public MesAlarmWindow(string message, string name)
    {
        InitializeComponent();
        TxtMsg.Text = message;
        LbName.Content = name;
        Title = LanguageService.Tr("MES 报警信息");
        BtnClose.Content = LanguageService.Tr("关闭");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
