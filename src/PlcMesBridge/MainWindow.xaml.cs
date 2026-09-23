using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PlcMesBridge.Controls;
using PlcMesBridge.Core.Comms;
using PlcMesBridge.Core.Data;
using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Core.Mes;
using PlcMesBridge.Core.Modes;
using PlcMesBridge.Core.Services;

namespace PlcMesBridge;

/// <summary>产品行（DataGrid 绑定，No=产品号 1..160，ProductId=产品 ID）。</summary>
public class ProductRow
{
    public int No { get; set; }
    public string ProductId { get; set; } = "";
}

/// <summary>
/// 主窗（单机视图 = 单机版 Form1；多机视图 = 多机版 Form1，1:1）。
/// 节拍说明（相对老项目的唯一现代化改动，kaleidoscope 铁律：UI 线程禁网络 IO）：
/// 老项目 Timer 跑在 UI 线程（160 次 PLC 读 + 同步 POST 会冻界面）；
/// 新项目节拍跑后台 Task，PLC/DB/HTTP 全在后台，UI 只收事件经 Dispatcher 刷新。
/// </summary>
public partial class MainWindow : Window
{
    private SingleMachineCoordinator? _single;
    private MultiMachineManager? _multi;
    private IPlcClient? _singlePlc;
    private CancellationTokenSource? _cts;
    private readonly ObservableCollection<ProductRow> _rows = new();
    private readonly StationPanel[] _panels = new StationPanel[8];
    private readonly DispatcherTimer _clock = new();
    private bool _useSim;
    /// <summary>程序化重启中（设置切模式后自动重启）：关窗跳过"是否退出"确认，
    /// 否则确认框卡住旧进程不退，新进程已起，造成双开。</summary>
    private bool _isRestarting;

    public MainWindow()
    {
        InitializeComponent();
        GridProducts.ItemsSource = _rows;
        for (int i = 1; i <= 160; i++)
            _rows.Add(new ProductRow { No = i });
        _panels = new[] { St0, St1, St2, St3, St4, St5, St6, St7 };
        for (int i = 0; i < 8; i++)
        {
            int id = i;
            _panels[i].StationId = id;
            _panels[i].SetName(MesLogger.StationNames[id]);
            _panels[i].OpenLog += OpenStationLog;
            _panels[i].ShowDetail += m => Dispatcher.Invoke(() => ShowStationDetail(m));
        }

        // 模拟开关：config.ini [setting] Simulate=1 则全用内存模拟（先做模拟联调）。
        _useSim = IniFile.ReadStr(AppPaths.ConfigIni, "setting", "Simulate", "0") == "1";

        if (AppConfig.Mode == RunMode.Single)
            InitSingle();
        else
            InitMulti();

        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => LbClock.Content = DateTime.Now.ToString("G");
        _clock.Start();

        UpdateUI();
        LocalLog("软件启动", -1);
    }

    // ================= 单机 =================

    private void InitSingle()
    {
        SingleView.Visibility = Visibility.Visible;
        MultiView.Visibility = Visibility.Collapsed;
        _singlePlc = _useSim ? new SimulatedPlcClient() : new MelsecPlcClient(false);
        _single = new SingleMachineCoordinator(
            _singlePlc, new CureRecordStore(new SqliteHelper(AppPaths.DatabaseFile)));
        _single.LogMessage += m => LocalLog(m, -1);
        _single.UiChanged += RefreshSingle;
        _single.ProductTable += FillTable;
        _single.AlarmRaised += (m, n) => Dispatcher.Invoke(() =>
            new MesAlarmWindow(m, n).Show());
        if (_useSim)
            SeedSimulation();
    }

    /// <summary>模拟联调预置：库位 + 两个产品 + 固化 + 进入时间（地址跟当前单机配置走）。</summary>
    private void SeedSimulation()
    {
        if (_singlePlc is not SimulatedPlcClient sim || _single == null)
            return;
        var cfg = _single.Cfg;
        sim.PresetString(cfg.BinId, "SIM-KW01");
        sim.PresetString(cfg.ProductAddr(1), "SIML0001");
        sim.PresetString(cfg.ProductAddr(2), "SIMR0002");
        sim.PresetInt32(cfg.Curing, new[] { 300 });
        var t = DateTime.Now;
        sim.PresetWords(cfg.EntryTime, new short[]
            { (short)t.Year, (short)t.Month, (short)t.Day,
              (short)t.Hour, (short)t.Minute, (short)t.Second });
    }

    /// <summary>
    /// 连接控件状态（单机走状态卡内按钮，多机走第二行按钮，两处不重复）。
    /// </summary>
    private void SetConnectUi(bool enabled, string text)
    {
        if (_single != null)
        {
            BtnConnectInline.IsEnabled = enabled;
            BtnConnectInline.Content = text;
        }
        else
        {
            BtnConnectM.IsEnabled = enabled;
            BtnConnectM.Content = text;
        }
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_single == null)
            return;
        SetConnectUi(false, "连接中...");
        Task.Run(() =>
        {
            bool ok = _single.Connect(out _);
            Dispatcher.Invoke(() =>
            {
                if (ok)
                {
                    // 复刻：连上后产品表变灰（老项目 DataGridView1.BackgroundColor=DarkGray）。
                    GridProducts.Background = Brushes.DarkGray;
                    StartSingleLoop();
                    SetConnectUi(false, LanguageService.Tr("连接PLC"));
                }
                else
                {
                    SetConnectUi(true, LanguageService.Tr("连接PLC"));
                    LbBeatLamp.Foreground = Brushes.Red;
                    MessageBox.Show(LanguageService.Tr("连接PLC失败"), "PLC",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        });
    }

    /// <summary>后台节拍：200ms 业务 + 1s 心跳（老项目 TimerScan/TimerLive 周期原样）。</summary>
    private void StartSingleLoop()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(async () =>
        {
            int n = 0;
            while (!token.IsCancellationRequested)
            {
                try { _single?.TickScan(); } catch { }
                if (++n % 5 == 0)
                {
                    try
                    {
                        bool lamp = _single?.TickLive() ?? false;
                        Dispatcher.Invoke(() => LbBeatLamp.Foreground =
                            lamp ? Brushes.Lime : Brushes.Green);
                    }
                    catch { }
                }
                await Task.Delay(200, token).ConfigureAwait(false);
            }
        }, token);
    }

    private void RefreshSingle()
    {
        if (_single == null)
            return;
        Dispatcher.Invoke(() =>
        {
            LbCuring.Content = $"{LanguageService.Tr("固化时间: ")}{_single.CuringText}";
            LbInTime.Content = $"{LanguageService.Tr("进入时间: ")}{_single.InTimeText}";
            LbBin.Content = $"{LanguageService.Tr("库位ID: ")}{_single.BinIdText}";
            LbStatus.Content = LanguageService.Tr(_single.StatusKey);
            var s = _single.Stats;
            LbTotal.Content = $"{LanguageService.Tr("库内总物料:")}  {s.Total}";
            LbDone.Content = $"{LanguageService.Tr("静置已完成:")} L:{s.DoneL} R:{s.DoneR}";
            LbResting.Content = $"{LanguageService.Tr("正在静置:")} L:{s.RestingL} R:{s.RestingR}";
            LbTomorrow.Content = $"{LanguageService.Tr("明日可完成:")} L:{s.TomorrowL} R:{s.TomorrowR}";
        });
    }

    private void FillTable(IReadOnlyList<string> ps)
    {
        Dispatcher.Invoke(() =>
        {
            for (int i = 0; i < _rows.Count && i < ps.Count; i++)
                _rows[i].ProductId = ps[i];
        });
    }

    private void LocalLog(string message, int station)
    {
        string line = LocalLogService.Write(message, station);
        if (station < 0)
        {
            Dispatcher.Invoke(() =>
            {
                if (BoxLog.Items.Count > 100)
                    BoxLog.Items.Clear();
                BoxLog.Items.Insert(0, line);
            });
        }
        else if (station < 8)
        {
            _panels[station].AddLog(line);
        }
    }

    // ================= 多机 =================

    private void InitMulti()
    {
        SingleView.Visibility = Visibility.Collapsed;
        MultiView.Visibility = Visibility.Visible;
        CmbView.Items.Add("全部机台");
        for (int i = 0; i < 8; i++)
            CmbView.Items.Add($"{i + 1}号-{MesLogger.StationNames[i]}");
        CmbView.SelectedIndex = 0;

        _multi = new MultiMachineManager(id =>
            _useSim ? new SimulatedPlcClient()
                    : new MelsecPlcClient(AppConfig.StationAscii[id]));
        _multi.StationLog += (id, m) => LocalLog(m, id);
        _multi.Heartbeat += (id, on) => Dispatcher.Invoke(() =>
            _panels[id].SetLive(on, false));
        _multi.StationBroken += id => Dispatcher.Invoke(() =>
            _panels[id].SetLive(false, true));
        _multi.ScanElapsed += (id, ms) => Dispatcher.Invoke(() =>
            LbCycle.Content = $"{LanguageService.Tr("扫描周期: ")}{ms} ms");
        _multi.AlarmRaised += (m, n) => Dispatcher.Invoke(() =>
            new MesAlarmWindow(m, n).Show());
    }

    /// <summary>
    /// 连接全部机台（老项目 Button1_Click 循环语义；逐台 ConnectServer 是阻塞
    /// 网络 IO，必须后台跑，老项目 ProgressBar 进度此处用按钮文案"连接中..."代替）。
    /// </summary>
    private void BtnConnectM_Click(object sender, RoutedEventArgs e)
    {
        if (_multi == null)
            return;
        SetConnectUi(false, "连接中...");
        Task.Run(() =>
        {
            var (ok, skipped) = _multi.ConnectAll();
            Dispatcher.Invoke(() =>
            {
                LocalLog($"连接完成：成功 {ok} 台，跳过 {skipped} 台", -1);
                SetConnectUi(false, LanguageService.Tr("连接设备"));
            });
        });
    }

    /// <summary>视图筛选（补齐 Form2/Form6"只看某几台"：下拉切单台/全部，面板复用不复制窗体）。</summary>
    private void CmbView_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_panels[0] == null)
            return;
        int sel = CmbView.SelectedIndex;
        for (int i = 0; i < 8; i++)
            _panels[i].Visibility = (sel == 0 || sel - 1 == i)
                ? Visibility.Visible : Visibility.Collapsed;
        StationGrid.Columns = sel == 0 ? 4 : 1;
    }

    private void OpenStationLog(int station)
    {
        var w = new MesLogWindow(station);
        w.Show();
    }

    /// <summary>
    /// 机台日志单击（复刻老项目 ListBox1_SelectedIndexChanged 全逻辑）：
    /// 先 MsgBox 全文；若含"存图上报"+"图片路径"，按⚫取第二段，文件存在开图，
    /// 不存在/格式不对给对应提示（老项目 MsgBox 原样）。
    /// </summary>
    private void ShowStationDetail(string line)
    {
        MessageBox.Show(line);
        string imgTag = LanguageService.Tr("存图上报");
        string pathTag = LanguageService.Tr("图片路径");
        if (line.Contains(imgTag) && line.Contains(pathTag))
        {
            string[] parts = line.Split('⚫');
            if (parts.Length >= 3)
            {
                string path = parts[1];
                if (File.Exists(path))
                    new ImageWindow(path).Show();
                else
                    MessageBox.Show(
                        LanguageService.Tr("图片文件不存在") + "\r\n" + path);
            }
            else
            {
                MessageBox.Show(LanguageService.Tr("图片路径不存在,请查看详细日志"));
            }
        }
    }

    // ================= 通用 =================

    private void BtnLang_Click(object sender, RoutedEventArgs e)
    {
        LanguageService.CurrentLanguage =
            LanguageService.CurrentLanguage == "CH" ? "EN" : "CH";
        IniFile.Write(AppPaths.ConfigIni, "setting", "Language",
            LanguageService.CurrentLanguage);
        UpdateUI();
        RefreshSingle();
    }

    private void UpdateUI()
    {
        Title = LanguageService.Tr("PLC通信");
        LbStatusTitle.Content = LanguageService.Tr("运行状态");
        LbStatsTitle.Content = LanguageService.Tr("统计信息");
        // 操作入口：单机顶菜单（连接放状态卡内）；
        // 多机顶菜单 + 第二行只放连接设备按钮。
        bool single = AppConfig.Mode == RunMode.Single;
        if (single)
            BtnConnectInline.Content = LanguageService.Tr("连接PLC");
        else
            BtnConnectM.Content = LanguageService.Tr("连接设备");
        LbBeat.Content = LanguageService.Tr("PLC心跳");
        MiLog.Header = LanguageService.Tr("MES 交互实时日志");
        MiDebug.Header = LanguageService.Tr("MES 接口调试界面");
        MiSettings.Header = LanguageService.Tr("设置");
        MiLang.Header =
            LanguageService.CurrentLanguage == "CH" ? "English" : "中文";
        ColNo.Header = LanguageService.Tr("产品号");
        ColId.Header = LanguageService.Tr("产品ID");
        LbStatus.Content = LanguageService.Tr(_single?.StatusKey ?? "物料状态");
    }

    private void BtnLog_Click(object sender, RoutedEventArgs e) =>
        new MesLogWindow(-1).Show();

    private void BtnDebug_Click(object sender, RoutedEventArgs e) =>
        new MesDebugWindow().Show();

    /// <summary>
    /// 设置按钮（单机/多机视图共用）：先弹登录窗验 admin/123456，
    /// 通过再弹设置窗切运行模式；模式变了自动重启
    /// （先拉新进程再退当前进程，且跳过退出确认，保证旧进程一定退出）。
    /// </summary>
    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() != true)
            return;
        var settings = new SettingsWindow { Owner = this };
        if (settings.ShowDialog() != true || !settings.NeedsRestart())
            return;
        string? exe = Environment.ProcessPath;
        if (exe == null)
        {
            MessageBox.Show(LanguageService.Tr("设置已保存，重启软件后生效"),
                LanguageService.Tr("设置"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _isRestarting = true;
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    private void BoxLog_DoubleClick(object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (BoxLog.SelectedItem != null)
            MessageBox.Show(BoxLog.SelectedItem.ToString());
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 程序化重启不弹确认（确认框会卡住旧进程导致双开），用户手动关窗才确认。
        if (!_isRestarting && MessageBox.Show(LanguageService.Tr("是否退出?"), Title,
                MessageBoxButton.OKCancel) == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }
        _cts?.Cancel();
        _multi?.Dispose();
        _singlePlc?.Dispose();
        base.OnClosing(e);
    }
}
