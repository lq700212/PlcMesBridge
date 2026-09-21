using System.Windows;
using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Core.Mes;

namespace PlcMesBridge;

/// <summary>日志窗（复刻 FormMESLogView：开窗加载历史 + 订阅 OnLogAdded，关窗退订）。</summary>
public partial class MesLogWindow : Window
{
    private readonly int _stationId;

    public MesLogWindow(int stationId = -1)
    {
        InitializeComponent();
        _stationId = stationId;
        UpdateUI();

        var history = stationId < 0
            ? MesLogger.GetCachedLogs()
            : MesLogger.GetCachedLogs(stationId);
        foreach (var line in history)
            BoxLogs.Items.Add(line);
        ScrollToBottom();

        MesLogger.LogAdded += OnLogAdded;
    }

    private void UpdateUI()
    {
        Title = LanguageService.Tr("MES 交互实时日志");
        BtnClear.Content = LanguageService.Tr("清空当前显示");
        BtnLang.Content = LanguageService.CurrentLanguage == "CH" ? "English" : "中文";
    }

    private void BtnLang_Click(object sender, RoutedEventArgs e)
    {
        LanguageService.CurrentLanguage =
            LanguageService.CurrentLanguage == "CH" ? "EN" : "CH";
        IniFile.Write(AppPaths.ConfigIni, "setting", "Language",
            LanguageService.CurrentLanguage);
        UpdateUI();
    }

    private void OnLogAdded(string message, int stationId)
    {
        // 分流：单机窗只收 -1，多机窗只收本机台（老项目 index 过滤语义）。
        if (_stationId < 0 ? stationId >= 0 : stationId != _stationId)
            return;
        Dispatcher.Invoke(() =>
        {
            BoxLogs.Items.Add(message);
            if (BoxLogs.Items.Count > 300)
                BoxLogs.Items.RemoveAt(0);
            ScrollToBottom();
        });
    }

    private void ScrollToBottom()
    {
        if (BoxLogs.Items.Count > 0)
            BoxLogs.ScrollIntoView(BoxLogs.Items[^1]);
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e) =>
        BoxLogs.Items.Clear(); // 只清显示，不动后台缓存（老项目原注）。

    protected override void OnClosed(EventArgs e)
    {
        MesLogger.LogAdded -= OnLogAdded;
        base.OnClosed(e);
    }
}
