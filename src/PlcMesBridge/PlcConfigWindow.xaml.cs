using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PlcMesBridge.Core.Comms;
using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Core.Mes;

namespace PlcMesBridge;

/// <summary>
/// PLC 配置窗（去现场零写代码的入口：IP/端口/命令地址全界面改）。
/// 三页：单机PLC / 多机PLC / 接口地址（6 接口触发 + 回写地址束 + URL）。
/// 现场流程：填表 → 点"测试连接"（拿表单值直测，不用先保存，绿了再存）→
/// "保存并重启"（先校验格式，错当场拦）→ 主窗自动重启生效。
/// 本窗只做"事件→控件"搬运：读写校验全走 Core（PlcConfigStore/Validator/
/// Tester，可测），落盘后调 AppConfig.Load 刷新内存再关窗重启。
/// </summary>
public partial class PlcConfigWindow : Window
{
    /// <summary>本次保存是否改了 PLC 配置（主窗据此决定是否自动重启）。</summary>
    public bool PlcChanged { get; private set; }

    private readonly Dictionary<string, TextBox> _singleEdits = new();
    private readonly CheckBox[] _enChk = new CheckBox[8];
    private readonly TextBox[] _ipBox = new TextBox[8];
    private readonly TextBox[] _portBox = new TextBox[8];
    private readonly ComboBox[] _protoBox = new ComboBox[8];
    private readonly Label[] _rowResult = new Label[8];
    private readonly Dictionary<string, TextBox> _apiEdits = new();
    private TextBox _singleResult = null!;
    private TextBox _dataLenBox = null!;
    private readonly bool _useSim;

    public PlcConfigWindow()
    {
        InitializeComponent();
        _useSim = IniFile.ReadStr(AppPaths.ConfigIni, "setting", "Simulate", "0") == "1";
        BuildSingle();
        BuildMulti();
        BuildApi();
        UpdateUI();
    }

    // ================= 单机页 =================

    private void BuildSingle()
    {
        var cfg = AppConfig.Single;
        AddSingleRow("IP地址", cfg.Ip);
        AddSingleRow("端口", cfg.Port.ToString(), 80);

        var testRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var btnTest = new Button { Content = LanguageService.Tr("测试连接"), Width = 100, Height = 30, Margin = new Thickness(114, 0, 8, 0) };
        _singleResult = new TextBox
        {
            Width = 330, Height = 30, IsReadOnly = true,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        btnTest.Click += async (_, _) => await TestSingleAsync(btnTest);
        testRow.Children.Add(btnTest);
        testRow.Children.Add(_singleResult);
        PanelSingle.Children.Add(testRow);

        AddSingleRow("命令字", cfg.Cmd);
        AddSingleRow("完成位", cfg.Done);
        AddSingleRow("心跳地址", cfg.Live);
        AddSingleRow("固化时间地址", cfg.Curing);
        AddSingleRow("进入时间地址", cfg.EntryTime);
        AddSingleRow("PC时间地址", cfg.PcTime);
        AddSingleRow("库位地址", cfg.BinId);
        AddSingleRow("库位长度", cfg.BinLen.ToString(), 80);
        AddSingleRow("报警地址", cfg.Alarm);
        AddSingleRow("产品前缀", cfg.ProdPrefix, 80);
        AddSingleRow("产品起始", cfg.ProdStart.ToString(), 80);
        AddSingleRow("产品步长", cfg.ProdStep.ToString(), 80);
        AddSingleRow("产品个数", cfg.ProdCount.ToString(), 80);
        AddSingleRow("产品读长", cfg.ProdReadLen.ToString(), 80);
        AddSingleRow("产品写长", cfg.ProdWriteLen.ToString(), 80);

        var btnDef = new Button
        {
            Content = LanguageService.Tr("恢复缺省"), Width = 100, Height = 30,
            Margin = new Thickness(114, 8, 0, 0),
            Style = (Style)FindResource("FreshCancelButton"),
        };
        btnDef.Click += (_, _) => FillSingle(SinglePlcConfig.Default);
        PanelSingle.Children.Add(btnDef);
    }

    private void AddSingleRow(string labelKey, string initial, int boxWidth = 170)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        row.Children.Add(new Label
        {
            Content = LanguageService.Tr(labelKey), Width = 110,
            VerticalContentAlignment = VerticalAlignment.Center,
        });
        var txt = new TextBox
        {
            Text = initial, Width = boxWidth, Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(txt);
        PanelSingle.Children.Add(row);
        _singleEdits[labelKey] = txt;
    }

    private void FillSingle(SinglePlcConfig c)
    {
        _singleEdits["IP地址"].Text = c.Ip;
        _singleEdits["端口"].Text = c.Port.ToString();
        _singleEdits["命令字"].Text = c.Cmd;
        _singleEdits["完成位"].Text = c.Done;
        _singleEdits["心跳地址"].Text = c.Live;
        _singleEdits["固化时间地址"].Text = c.Curing;
        _singleEdits["进入时间地址"].Text = c.EntryTime;
        _singleEdits["PC时间地址"].Text = c.PcTime;
        _singleEdits["库位地址"].Text = c.BinId;
        _singleEdits["库位长度"].Text = c.BinLen.ToString();
        _singleEdits["报警地址"].Text = c.Alarm;
        _singleEdits["产品前缀"].Text = c.ProdPrefix;
        _singleEdits["产品起始"].Text = c.ProdStart.ToString();
        _singleEdits["产品步长"].Text = c.ProdStep.ToString();
        _singleEdits["产品个数"].Text = c.ProdCount.ToString();
        _singleEdits["产品读长"].Text = c.ProdReadLen.ToString();
        _singleEdits["产品写长"].Text = c.ProdWriteLen.ToString();
    }

    private SinglePlcConfig GatherSingle() => new()
    {
        Ip = _singleEdits["IP地址"].Text,
        Port = ParseInt(_singleEdits["端口"].Text),
        Cmd = _singleEdits["命令字"].Text,
        Done = _singleEdits["完成位"].Text,
        Live = _singleEdits["心跳地址"].Text,
        Curing = _singleEdits["固化时间地址"].Text,
        EntryTime = _singleEdits["进入时间地址"].Text,
        PcTime = _singleEdits["PC时间地址"].Text,
        BinId = _singleEdits["库位地址"].Text,
        BinLen = ParseInt(_singleEdits["库位长度"].Text),
        Alarm = _singleEdits["报警地址"].Text,
        ProdPrefix = _singleEdits["产品前缀"].Text,
        ProdStart = ParseInt(_singleEdits["产品起始"].Text),
        ProdStep = ParseInt(_singleEdits["产品步长"].Text),
        ProdCount = ParseInt(_singleEdits["产品个数"].Text),
        ProdReadLen = ParseInt(_singleEdits["产品读长"].Text),
        ProdWriteLen = ParseInt(_singleEdits["产品写长"].Text),
    };

    private async Task TestSingleAsync(Button btn)
    {
        btn.IsEnabled = false;
        _singleResult.Text = LanguageService.Tr("测试中...");
        try
        {
            var c = GatherSingle();
            var (ok, msg, _) = await PlcConnectionTester.TestAsync(
                () => new MelsecPlcClient(false), c.Ip.Trim(), c.Port,
                PlcConfigValidator.NormAddr(c.Cmd)).ConfigureAwait(true);
            _singleResult.Text = (ok ? LanguageService.Tr("连接成功") + " " : LanguageService.Tr("连接失败") + " ") + msg;
            _singleResult.Foreground = ok ? Brushes.DarkGreen : Brushes.Red;
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    // ================= 多机页 =================

    private void BuildMulti()
    {
        var stations = PlcConfigStore.LoadStations(AppPaths.ConfigIni);
        for (int i = 0; i < 8; i++)
        {
            int id = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            var chk = new CheckBox
            {
                Content = $"{id + 1}号{LanguageService.Tr("启用")}",
                IsChecked = stations[id].Enabled, Width = 90,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            var ip = new TextBox
            {
                Text = stations[id].Ip, Width = 130, Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0),
            };
            var port = new TextBox
            {
                Text = stations[id].Port.ToString(), Width = 60, Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0),
            };
            var proto = new ComboBox { Width = 80, Height = 28, Margin = new Thickness(4, 0, 0, 0) };
            proto.Items.Add(LanguageService.Tr("二进制"));
            proto.Items.Add("ASCII");
            proto.SelectedIndex = stations[id].UseAscii ? 1 : 0;
            var btn = new Button
            {
                Content = LanguageService.Tr("测试连接"), Width = 70, Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
            };
            var result = new Label { Width = 120, VerticalContentAlignment = VerticalAlignment.Center };
            btn.Click += async (_, _) => await TestStationAsync(id, btn);
            row.Children.Add(chk);
            row.Children.Add(ip);
            row.Children.Add(port);
            row.Children.Add(proto);
            row.Children.Add(btn);
            row.Children.Add(result);
            PanelMulti.Children.Add(row);
            _enChk[id] = chk;
            _ipBox[id] = ip;
            _portBox[id] = port;
            _protoBox[id] = proto;
            _rowResult[id] = result;
        }

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var btnAll = new Button { Content = LanguageService.Tr("全部测试"), Width = 100, Height = 30 };
        btnAll.Click += async (_, _) => await TestAllStationsAsync(btnAll);
        var btnDef = new Button
        {
            Content = LanguageService.Tr("恢复缺省"), Width = 100, Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource("FreshCancelButton"),
        };
        btnDef.Click += (_, _) => FillStations(PlcConfigStore.LoadStations(AppPaths.ConfigIni));
        bar.Children.Add(btnAll);
        bar.Children.Add(btnDef);
        PanelMulti.Children.Add(bar);
    }

    private void FillStations(StationPlcConfig[] cs)
    {
        for (int i = 0; i < 8; i++)
        {
            _enChk[i].IsChecked = cs[i].Enabled;
            _ipBox[i].Text = cs[i].Ip;
            _portBox[i].Text = cs[i].Port.ToString();
            _protoBox[i].SelectedIndex = cs[i].UseAscii ? 1 : 0;
        }
    }

    private StationPlcConfig[] GatherStations()
    {
        var cs = new StationPlcConfig[8];
        for (int i = 0; i < 8; i++)
        {
            cs[i] = new StationPlcConfig
            {
                Enabled = _enChk[i].IsChecked == true,
                Ip = _ipBox[i].Text,
                Port = ParseInt(_portBox[i].Text),
                UseAscii = _protoBox[i].SelectedIndex == 1,
            };
        }
        return cs;
    }

    private async Task TestStationAsync(int id, Button btn)
    {
        btn.IsEnabled = false;
        _rowResult[id].Content = LanguageService.Tr("测试中...");
        try
        {
            var cs = GatherStations();
            // 读 0027 触发字验证可读（触发地址是全局配置，读当前 API 页表单值）。
            string trg = _apiEdits.TryGetValue("trg:API0027", out var t)
                ? t.Text : MesConfig.TriggerApi0027;
            bool ascii = cs[id].UseAscii;
            var (ok, msg, _) = await PlcConnectionTester.TestAsync(
                () => new MelsecPlcClient(ascii), cs[id].Ip.Trim(), cs[id].Port,
                PlcConfigValidator.NormAddr(trg)).ConfigureAwait(true);
            _rowResult[id].Content = ok ? LanguageService.Tr("连接成功") : LanguageService.Tr("连接失败");
            _rowResult[id].Foreground = ok ? Brushes.DarkGreen : Brushes.Red;
            _rowResult[id].ToolTip = msg;
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    private async Task TestAllStationsAsync(Button btn)
    {
        btn.IsEnabled = false;
        try
        {
            var cs = GatherStations();
            string trg = _apiEdits.TryGetValue("trg:API0027", out var t)
                ? t.Text : MesConfig.TriggerApi0027;
            for (int i = 0; i < 8; i++)
            {
                if (!cs[i].Enabled)
                {
                    _rowResult[i].Content = "—";
                    continue;
                }
                _rowResult[i].Content = LanguageService.Tr("测试中...");
                bool ascii = cs[i].UseAscii;
                var (ok, msg, _) = await PlcConnectionTester.TestAsync(
                    () => new MelsecPlcClient(ascii), cs[i].Ip.Trim(), cs[i].Port,
                    PlcConfigValidator.NormAddr(trg)).ConfigureAwait(true);
                _rowResult[i].Content = ok ? LanguageService.Tr("连接成功") : LanguageService.Tr("连接失败");
                _rowResult[i].Foreground = ok ? Brushes.DarkGreen : Brushes.Red;
                _rowResult[i].ToolTip = msg;
            }
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    // ================= 接口地址页 =================

    private void BuildApi()
    {
        _dataLenBox = AddApiRow("数据长度", MesConfig.DataLength.ToString(), 80);
        foreach (string api in MesConfig.ApiCodes)
        {
            PanelApi.Children.Add(new Label
            {
                Content = api, FontWeight = FontWeights.Bold,
                Foreground = (Brush)new BrushConverter().ConvertFrom("#1565C0")!,
                Margin = new Thickness(0, 8, 0, 0),
            });
            AddApiRow("触发地址", MesConfig.GetTrigger(api), key: $"trg:{api}");
            int idx = Array.IndexOf(MesConfig.ApiCodes, api);
            var set = MesConfig.ApiAddresses[idx];
            AddApiRow("URL", MesConfig.GetUrl(api), 340, $"url:{api}");
            AddApiRow("数据地址", set.PlcData, key: $"dat:{api}");
            AddApiRow("MSG地址", set.MesMsg, key: $"msg:{api}");
            AddApiRow("DATA地址", set.MesData, key: $"data:{api}");
            AddApiRow("状态地址", set.MesStatus, key: $"st:{api}");
            AddApiRow("完成地址", set.MesComplete, key: $"done:{api}");
        }
    }

    private TextBox AddApiRow(string labelKey, string initial, int boxWidth = 170, string? key = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        row.Children.Add(new Label
        {
            Content = LanguageService.Tr(labelKey), Width = 110,
            VerticalContentAlignment = VerticalAlignment.Center,
        });
        var txt = new TextBox
        {
            Text = initial, Width = boxWidth, Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(txt);
        PanelApi.Children.Add(row);
        _apiEdits[key ?? labelKey] = txt;
        return txt;
    }

    // ================= 保存 =================

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var single = GatherSingle();
        var stations = GatherStations();
        var errs = PlcConfigValidator.ValidateSingle(single);
        errs.AddRange(PlcConfigValidator.ValidateStations(stations));
        errs.AddRange(ValidateApi());

        if (errs.Count > 0)
        {
            string lines = string.Join("\r\n",
                errs.Select(x => $"{LanguageService.Tr(x.Field)}：{LanguageService.Tr(x.Reason)}"));
            MessageBox.Show($"{LanguageService.Tr("以下配置有误")}：\r\n{lines}",
                LanguageService.Tr("PLC配置"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string ini = AppPaths.ConfigIni;
        PlcConfigStore.SaveSingle(ini, single);
        PlcConfigStore.SaveStations(ini, stations);
        SaveApiSection(ini);
        AppConfig.Load(); // 刷新内存（重启后会再读一次，此处保证本进程内一致）
        PlcChanged = true;
        DialogResult = true;
        Close();
    }

    private List<(string Field, string Reason)> ValidateApi()
    {
        var errs = new List<(string, string)>();
        if (!int.TryParse(_dataLenBox.Text.Trim(), out int dl) || dl is < 1 or > 10000)
            errs.Add(("数据长度", "范围1-10000"));
        foreach (string api in MesConfig.ApiCodes)
        {
            foreach (string key in new[] { $"trg:{api}", $"dat:{api}", $"msg:{api}", $"data:{api}", $"st:{api}", $"done:{api}" })
            {
                if (!PlcConfigValidator.IsPlcAddress(_apiEdits[key].Text))
                    errs.Add(($"{api} {key.Split(':')[0]}", "PLC地址格式须为D/R+数字"));
            }
            string url = _apiEdits[$"url:{api}"].Text.Trim();
            if (!string.IsNullOrEmpty(url) &&
                (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
                 (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)))
                errs.Add(($"{api} URL", "IP地址无效"));
        }
        return errs;
    }

    private void SaveApiSection(string ini)
    {
        string[] names = { "API0027", "API0028", "API0030", "API0031", "API0032", "API0033" };
        string[] urlKeys = { "Url_API0027", "Url_API0028", "Url_API0030", "Url_API0031", "Url_API0032", "Url_API0033" };
        string[] trgKeys = { "TriggerAddrAPI0027", "TriggerAddrAPI0028", "TriggerAddrAPI0030", "TriggerAddrAPI0031", "TriggerAddrAPI0032", "TriggerAddrAPI0033" };
        for (int i = 0; i < 6; i++)
        {
            IniFile.Write(ini, "MES", urlKeys[i], _apiEdits[$"url:{names[i]}"].Text.Trim());
            IniFile.Write(ini, "MES", trgKeys[i],
                PlcConfigValidator.NormAddr(_apiEdits[$"trg:{names[i]}"].Text));
            IniFile.Write(ini, "MES", $"{names[i]}PLCDataAddr",
                PlcConfigValidator.NormAddr(_apiEdits[$"dat:{names[i]}"].Text));
            IniFile.Write(ini, "MES", $"{names[i]}MESMSGAddr",
                PlcConfigValidator.NormAddr(_apiEdits[$"msg:{names[i]}"].Text));
            IniFile.Write(ini, "MES", $"{names[i]}MESDataAddr",
                PlcConfigValidator.NormAddr(_apiEdits[$"data:{names[i]}"].Text));
            IniFile.Write(ini, "MES", $"{names[i]}MESSTATUSAddr",
                PlcConfigValidator.NormAddr(_apiEdits[$"st:{names[i]}"].Text));
            IniFile.Write(ini, "MES", $"{names[i]}MESCompleteAddr",
                PlcConfigValidator.NormAddr(_apiEdits[$"done:{names[i]}"].Text));
        }
        IniFile.Write(ini, "MES", "DataLength", _dataLenBox.Text.Trim());
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static int ParseInt(string s) =>
        int.TryParse((s ?? "").Trim(), out int v) ? v : 0;

    private void UpdateUI()
    {
        Title = LanguageService.Tr("PLC配置");
        TabSingle.Header = LanguageService.Tr("单机PLC");
        TabMulti.Header = LanguageService.Tr("多机PLC");
        TabApi.Header = LanguageService.Tr("接口地址");
        BtnSave.Content = LanguageService.Tr("保存并重启");
        BtnCancel.Content = LanguageService.Tr("取消");
        string hint = LanguageService.Tr("测试用表单值直测保存后重启生效");
        if (_useSim)
            hint += "（" + LanguageService.Tr("模拟模式下配置仅对真机有效") + "）";
        LbHint.Content = hint;
    }
}
