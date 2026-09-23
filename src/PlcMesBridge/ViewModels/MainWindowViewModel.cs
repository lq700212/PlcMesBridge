// =========================================================================
// MainWindowViewModel：主窗全部状态 + 命令（MVVM，单机/多机双视图）。
//
// 绑什么：菜单文本+门禁（角色 gating）/ 时钟 / 单机状态卡+统计卡+日志+
//   产品表 / 多机视图筛选+扫描周期+8 面板 / 连接按钮两处。
// Model 层不动：SingleMachineCoordinator / MultiMachineManager /
//   LocalLogService 照用（事件在后台线程触发，VM 经 UiInvoke 更新）。
// 节拍说明（相对老项目的唯一现代化改动，铁律：UI 线程禁网络 IO）：
//   老项目 Timer 跑 UI 线程（160 次 PLC 读 + 同步 POST 冻界面）；
//   新项目节拍跑后台 Task，PLC/DB/HTTP 全在后台，VM 只收事件更新属性。
// 关窗：View.OnClosing 调 OnClosing（确认经服务；重启中跳过确认，
//   否则确认框卡住旧进程造成双开）；Dispose 停节拍放资源。
// =========================================================================

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Timers;
using PlcMesBridge.Core.Comms;
using PlcMesBridge.Core.Data;
using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Core.Mes;
using PlcMesBridge.Core.Modes;
using PlcMesBridge.Core.Services;

namespace PlcMesBridge.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IDialogService _dialogs;
    private SingleMachineCoordinator? _single;
    private MultiMachineManager? _multi;
    private IPlcClient? _singlePlc;
    private CancellationTokenSource? _cts;
    private readonly System.Timers.Timer _clock = new(1000);
    private readonly bool _useSim;
    private bool _disposed;

    // ---- 登录态 ----
    private LoginRole _role = LoginRole.None;
    private string _loginUser = "";

    // ---- 单机状态 ----
    private string _beatLampColor = "Gray";
    private string _curingText = "";
    private string _inTimeText = "";
    private string _binIdText = "";
    private string _statusKey = "物料状态";
    private string _connectText = "";
    private bool _connectEnabled = true;
    private bool _gridGray;
    private string? _selectedLogLine;

    // ---- 多机状态 ----
    private int _selectedViewIndex;
    private int _gridColumns = 4;
    private string _scanCycleText = "";
    private string _clockText = "";

    public MainWindowViewModel(IDialogService? dialogs = null)
    {
        _dialogs = dialogs ?? DialogService.Instance;
        IsSingleMode = AppConfig.Mode == RunMode.Single;
        // 模拟开关：config.ini [setting] Simulate=1 则全用内存模拟。
        _useSim = IniFile.ReadStr(AppPaths.ConfigIni, "setting", "Simulate", "0") == "1";

        for (int i = 1; i <= 160; i++)
            Products.Add(new ProductRowViewModel(i));
        for (int i = 0; i < 8; i++)
        {
            int id = i;
            Stations.Add(new StationPanelViewModel(id,
                st => _dialogs.ShowMesLog(st),
                line => ShowStationDetail(line)));
        }
        ViewOptions.Add(Tr("全部机台"));
        for (int i = 0; i < 8; i++)
            ViewOptions.Add($"{i + 1}号-{MesLogger.StationNames[i]}");

        if (IsSingleMode)
            InitSingle();
        else
            InitMulti();

        _clock.Elapsed += (_, _) => UiInvoke(() =>
            ClockText = DateTime.Now.ToString("G"));
        _clock.Start();
        ClockText = DateTime.Now.ToString("G");

        ConnectSingleCommand = new RelayCommand(ConnectSingle, () => ConnectEnabled);
        ConnectMultiCommand = new RelayCommand(ConnectMulti, () => ConnectEnabled);
        ShowLogCommand = new RelayCommand(() => _dialogs.ShowMesLog(-1));
        ShowDebugCommand = new RelayCommand(() => _dialogs.ShowMesDebug());
        ShowModeCommand = new RelayCommand(ShowMode);
        ShowPlcCommand = new RelayCommand(ShowPlc);
        ShowChangePwdCommand = new RelayCommand(ShowChangePwd);
        ShowShortcutCommand = new RelayCommand(ShowShortcut);
        SwitchLanguageCommand = new RelayCommand(SwitchLanguage);
        LoginCommand = new RelayCommand(LoginLogout);
        ShowLogDetailCommand = new RelayCommand(ShowSelectedLogDetail);

        RefreshTexts();
        LocalLog(Tr("软件启动"), -1);
    }

    public bool IsSingleMode { get; }
    public bool IsMultiMode => !IsSingleMode;

    /// <summary>程序化重启中（关窗跳过"是否退出"确认，防双开）。</summary>
    public bool IsRestarting { get; private set; }

    // ================= 登录态 + 菜单 =================

    public string LoginHeader => _role == LoginRole.None
        ? Tr("登录") : $"{_loginUser} ({Tr("切换/退出")})";

    /// <summary>PLC设置项显隐（仅 dev 可见，XAML 经转换器绑 Visibility）。</summary>
    public bool PlcVisible => _role == LoginRole.Dev;

    public bool PlcEnabled => _role == LoginRole.Dev;
    public bool SettingsItemEnabled => RunModeSettings.IsLoggedIn(_role);

    public string TitleText => Tr("PLC通信");
    public string StatusTitleText => Tr("运行状态");
    public string StatsTitleText => Tr("统计信息");
    public string BeatLabel => Tr("PLC心跳");
    public string MiLogText => Tr("MES 交互实时日志");
    public string MiDebugText => Tr("MES 接口调试界面");
    public string MiModeText => Tr("运行模式");
    public string MiSettingsText => Tr("设置");
    public string MiPlcText => Tr("PLC设置");
    public string MiChangePwdText => Tr("修改密码");
    public string MiShortcutText => Tr("创建桌面快捷方式");
    public string MiLangText =>
        LanguageService.CurrentLanguage == "CH" ? "English" : "中文";
    public string ColNoText => Tr("产品号");
    public string ColIdText => Tr("产品ID");

    public RelayCommand ShowLogCommand { get; }
    public RelayCommand ShowDebugCommand { get; }
    public RelayCommand ShowModeCommand { get; }
    public RelayCommand ShowPlcCommand { get; }
    public RelayCommand ShowChangePwdCommand { get; }
    public RelayCommand ShowShortcutCommand { get; }
    public RelayCommand SwitchLanguageCommand { get; }
    public RelayCommand LoginCommand { get; }
    public RelayCommand ShowLogDetailCommand { get; }

    public void RefreshTexts()
    {
        ConnectText = IsSingleMode ? Tr("连接PLC") : Tr("连接设备");
        RefreshAll();
        RefreshSingle();
    }

    private static string Tr(string zh) => LanguageService.Tr(zh);

    private void RefreshGating()
    {
        Raise(nameof(LoginHeader));
        Raise(nameof(PlcVisible));
        Raise(nameof(PlcEnabled));
        Raise(nameof(SettingsItemEnabled));
    }

    /// <summary>
    /// 登录按钮：未登录弹登录窗，成功记角色并刷新门禁；
    /// 已登录再点确认是否退出（是→清登录态；否→弹窗切换账号）。
    /// </summary>
    private void LoginLogout()
    {
        if (_role != LoginRole.None)
        {
            bool? ask = _dialogs.ConfirmOrCancel(
                $"{Tr("当前已登录")}: {_loginUser}\n" +
                Tr("是否退出登录？（点否则切换账号）"), Tr("登录"));
            if (ask == null)
                return;
            if (ask == true)
            {
                _role = LoginRole.None;
                _loginUser = "";
                RefreshGating();
                return;
            }
        }
        var (ok, role, user) = _dialogs.ShowLogin();
        if (!ok)
            return;
        _role = role;
        _loginUser = user;
        RefreshGating();
    }

    /// <summary>未登录拦一刀（运行模式/设置子项共用）。</summary>
    private bool RequireLogin()
    {
        if (RunModeSettings.IsLoggedIn(_role))
            return true;
        _dialogs.ShowMessage(Tr("请先登录"), Tr("登录"));
        return false;
    }

    private void ShowMode()
    {
        if (!RequireLogin())
            return;
        if (_dialogs.ShowModeSwitch())
            RestartApp();
    }

    private void ShowPlc()
    {
        if (!RequireLogin())
            return;
        if (!RunModeSettings.IsDev(_role))
        {
            _dialogs.ShowMessage(Tr("需要 dev 权限"), Tr("设置"),
                DialogImage.Warning);
            return;
        }
        if (_dialogs.ShowPlcConfig())
            RestartApp();
    }

    private void ShowChangePwd()
    {
        if (!RequireLogin())
            return;
        _dialogs.ShowChangePassword(_loginUser, _role);
    }

    private void ShowShortcut()
    {
        if (!RequireLogin())
            return;
        _dialogs.CreateDesktopShortcut();
    }

    private void SwitchLanguage()
    {
        LanguageService.CurrentLanguage =
            LanguageService.CurrentLanguage == "CH" ? "EN" : "CH";
        IniFile.Write(AppPaths.ConfigIni, "setting", "Language",
            LanguageService.CurrentLanguage);
        RefreshTexts();
    }

    private void RestartApp()
    {
        IsRestarting = true;
        _dialogs.RestartApp();
    }

    // ================= 通用：时钟 + 日志 =================

    public string ClockText
    {
        get => _clockText;
        private set => Set(ref _clockText, value);
    }

    /// <summary>单机全量日志（头插，上限 100；分机台走各面板）。</summary>
    public ObservableCollection<string> LogLines { get; } = new();

    public string? SelectedLogLine
    {
        get => _selectedLogLine;
        set => Set(ref _selectedLogLine, value);
    }

    private void ShowSelectedLogDetail()
    {
        if (!string.IsNullOrEmpty(SelectedLogLine))
            _dialogs.ShowMessage(SelectedLogLine, TitleText);
    }

    private void LocalLog(string message, int station)
    {
        string line = LocalLogService.Write(message, station);
        if (station < 0)
        {
            UiInvoke(() =>
            {
                if (LogLines.Count > 100)
                    LogLines.Clear();
                LogLines.Insert(0, line);
            });
        }
        else if (station < 8)
        {
            Stations[station].AddLog(line);
        }
    }

    // ================= 单机 =================

    /// <summary>产品表（160 行，No 固定，ProductId 后台回填）。</summary>
    public ObservableCollection<ProductRowViewModel> Products { get; } = new();

    public string BeatLampColor
    {
        get => _beatLampColor;
        private set => Set(ref _beatLampColor, value);
    }

    public string CuringLine => $"{Tr("固化时间: ")}{_curingText}";
    public string InTimeLine => $"{Tr("进入时间: ")}{_inTimeText}";
    public string BinLine => $"{Tr("库位ID: ")}{_binIdText}";
    public string StatusLine => Tr(_statusKey);
    public string TotalLine { get; private set; } = "";
    public string DoneLine { get; private set; } = "";
    public string RestingLine { get; private set; } = "";
    public string TomorrowLine { get; private set; } = "";

    public string ConnectText
    {
        get => _connectText;
        private set => Set(ref _connectText, value);
    }

    public bool ConnectEnabled
    {
        get => _connectEnabled;
        private set
        {
            if (Set(ref _connectEnabled, value))
            {
                ConnectSingleCommand.RaiseCanExecuteChanged();
                ConnectMultiCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>连上后产品表变灰（老项目 DarkGray 語义，XAML 绑 Background）。</summary>
    public string GridBackground => _gridGray ? "DarkGray" : "White";

    public RelayCommand ConnectSingleCommand { get; }
    public RelayCommand ConnectMultiCommand { get; }

    private void InitSingle()
    {
        ConnectText = Tr("连接PLC");
        _singlePlc = _useSim ? new SimulatedPlcClient() : new MelsecPlcClient(false);
        _single = new SingleMachineCoordinator(
            _singlePlc, new CureRecordStore(new SqliteHelper(AppPaths.DatabaseFile)));
        _single.LogMessage += m => LocalLog(m, -1);
        _single.UiChanged += RefreshSingle;
        _single.ProductTable += FillTable;
        _single.AlarmRaised += (m, n) => UiInvoke(() => _dialogs.ShowAlarm(m, n));
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

    private void SetConnectUi(bool enabled, string text) =>
        UiInvoke(() =>
        {
            ConnectEnabled = enabled;
            ConnectText = text;
        });

    private void ConnectSingle()
    {
        if (_single == null)
            return;
        SetConnectUi(false, "连接中...");
        Task.Run(() =>
        {
            bool ok = _single.Connect(out _);
            UiInvoke(() =>
            {
                if (ok)
                {
                    // 复刻：连上后产品表变灰。
                    _gridGray = true;
                    Raise(nameof(GridBackground));
                    StartSingleLoop();
                    SetConnectUi(false, Tr("连接PLC"));
                }
                else
                {
                    SetConnectUi(true, Tr("连接PLC"));
                    BeatLampColor = "Red";
                    _dialogs.ShowMessage(Tr("连接PLC失败"), "PLC", DialogImage.Error);
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
                        UiInvoke(() => BeatLampColor = lamp ? "Lime" : "Green");
                    }
                    catch { }
                }
                try
                {
                    await Task.Delay(200, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private void RefreshSingle()
    {
        if (_single == null)
            return;
        UiInvoke(() =>
        {
            _curingText = _single.CuringText;
            _inTimeText = _single.InTimeText;
            _binIdText = _single.BinIdText;
            _statusKey = _single.StatusKey;
            var s = _single.Stats;
            TotalLine = $"{Tr("库内总物料:")}  {s.Total}";
            DoneLine = $"{Tr("静置已完成:")} L:{s.DoneL} R:{s.DoneR}";
            RestingLine = $"{Tr("正在静置:")} L:{s.RestingL} R:{s.RestingR}";
            TomorrowLine = $"{Tr("明日可完成:")} L:{s.TomorrowL} R:{s.TomorrowR}";
            Raise(nameof(CuringLine));
            Raise(nameof(InTimeLine));
            Raise(nameof(BinLine));
            Raise(nameof(StatusLine));
            Raise(nameof(TotalLine));
            Raise(nameof(DoneLine));
            Raise(nameof(RestingLine));
            Raise(nameof(TomorrowLine));
        });
    }

    private void FillTable(IReadOnlyList<string> ps) => UiInvoke(() =>
    {
        for (int i = 0; i < Products.Count && i < ps.Count; i++)
            Products[i].ProductId = ps[i];
    });

    // ================= 多机 =================

    public ObservableCollection<StationPanelViewModel> Stations { get; } = new();
    public ObservableCollection<string> ViewOptions { get; } = new();

    public int SelectedViewIndex
    {
        get => _selectedViewIndex;
        set
        {
            if (!Set(ref _selectedViewIndex, value))
                return;
            ApplyViewFilter();
        }
    }

    public int GridColumns
    {
        get => _gridColumns;
        private set => Set(ref _gridColumns, value);
    }

    public string ScanCycleText
    {
        get => _scanCycleText;
        private set => Set(ref _scanCycleText, value);
    }

    private void InitMulti()
    {
        ConnectText = Tr("连接设备");
        _multi = new MultiMachineManager(id =>
            _useSim ? new SimulatedPlcClient()
                    : new MelsecPlcClient(AppConfig.StationAscii[id]));
        _multi.StationLog += (id, m) => LocalLog(m, id);
        _multi.Heartbeat += (id, on) => UiInvoke(() =>
            Stations[id].SetLive(on, false));
        _multi.StationBroken += id => UiInvoke(() =>
            Stations[id].SetLive(false, true));
        _multi.ScanElapsed += (id, ms) => UiInvoke(() =>
            ScanCycleText = $"{Tr("扫描周期: ")}{ms} ms");
        _multi.AlarmRaised += (m, n) => UiInvoke(() => _dialogs.ShowAlarm(m, n));
    }

    /// <summary>
    /// 连接全部机台（老项目 Button1_Click 循环语义；逐台 ConnectServer 是阻塞
    /// 网络 IO，必须后台跑；按钮文案"连接中..."代替老项目 ProgressBar）。
    /// </summary>
    private void ConnectMulti()
    {
        if (_multi == null)
            return;
        SetConnectUi(false, "连接中...");
        Task.Run(() =>
        {
            var (ok, skipped) = _multi.ConnectAll();
            UiInvoke(() =>
            {
                LocalLog($"连接完成：成功 {ok} 台，跳过 {skipped} 台", -1);
                SetConnectUi(false, Tr("连接设备"));
            });
        });
    }

    private void ApplyViewFilter()
    {
        for (int i = 0; i < 8; i++)
            Stations[i].Visible = _selectedViewIndex == 0 || _selectedViewIndex - 1 == i;
        GridColumns = _selectedViewIndex == 0 ? 4 : 1;
    }

    /// <summary>
    /// 机台日志单击（复刻老项目 ListBox1_SelectedIndexChanged 全逻辑）：
    /// 先 MsgBox 全文；若含"存图上报"+"图片路径"，按⚫取第二段，文件存在开图，
    /// 不存在/格式不对给对应提示（老项目 MsgBox 原样）。
    /// </summary>
    private void ShowStationDetail(string line)
    {
        UiInvoke(() =>
        {
            _dialogs.ShowMessage(line, TitleText);
            string imgTag = Tr("存图上报");
            string pathTag = Tr("图片路径");
            if (line.Contains(imgTag) && line.Contains(pathTag))
            {
                string[] parts = line.Split('⚫');
                if (parts.Length >= 3)
                {
                    string path = parts[1];
                    if (File.Exists(path))
                        _dialogs.ShowImage(path);
                    else
                        _dialogs.ShowMessage(Tr("图片文件不存在") + "\r\n" + path,
                            TitleText);
                }
                else
                {
                    _dialogs.ShowMessage(Tr("图片路径不存在,请查看详细日志"), TitleText);
                }
            }
        });
    }

    // ================= 关窗 =================

    /// <summary>
    /// 关窗确认（View.OnClosing 调）：重启中跳过确认；用户手动关才确认。
    /// 返回 true 表示放行关闭。
    /// </summary>
    public bool OnClosing()
    {
        if (IsRestarting)
            return true;
        return _dialogs.Confirm(Tr("是否退出?"), TitleText);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _clock.Stop();
        _clock.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _multi?.Dispose();
        _singlePlc?.Dispose();
    }
}
