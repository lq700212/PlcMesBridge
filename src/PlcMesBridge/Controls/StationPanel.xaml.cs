using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PlcMesBridge.Core.Infrastructure;

namespace PlcMesBridge.Controls;

/// <summary>单机台面板（机台名 + 心跳灯 + 日志）。日志上限 100 条头插（老项目语义）。</summary>
public partial class StationPanel : UserControl
{
    public int StationId { get; set; }

    /// <summary>双击日志行：打开该机台 MES 日志窗（老项目 ListBox_DoubleClick 按 Tag 开窗）。</summary>
    public event Action<int>? OpenLog;

    /// <summary>单击日志行：老项目 SelectedIndexChanged 是 MsgBox 全文（存图链路已移入 MES 日志窗，此处保留 MsgBox 全文）。</summary>
    public event Action<string>? ShowDetail;

    public StationPanel()
    {
        InitializeComponent();
    }

    public void SetName(string name) => LblName.Content = name;

    public void SetLive(bool on, bool broken)
    {
        // 复刻：正常 Green/Lime 闪，断连红（老项目 LabelPLClive.ForeColor=Red）。
        LblLive.Foreground = broken ? Brushes.Red : (on ? Brushes.Lime : Brushes.Green);
    }

    public void AddLog(string line)
    {
        Dispatcher.Invoke(() =>
        {
            if (BoxLog.Items.Count > 100)
                BoxLog.Items.Clear();
            BoxLog.Items.Insert(0, line);
        });
    }

    public void RefreshLanguage()
    {
        LblName.Content = LanguageService.Tr(LblName.Content?.ToString() ?? string.Empty);
    }

    private void BoxLog_Selected(object sender, SelectionChangedEventArgs e)
    {
        // 复刻老项目 ListBox_SelectedIndexChanged：单击弹全文。
        if (BoxLog.SelectedItem != null)
            ShowDetail?.Invoke(BoxLog.SelectedItem.ToString() ?? string.Empty);
    }

    private void BoxLog_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (BoxLog.SelectedIndex >= 0)
            OpenLog?.Invoke(StationId);
    }
}
