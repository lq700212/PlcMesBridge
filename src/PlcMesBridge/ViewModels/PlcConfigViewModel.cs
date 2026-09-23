// =========================================================================
// PlcConfigViewModel：PLC 配置窗三页的状态 + 命令（MVVM）。
//
// 绑什么：单机 18 行 / 多机 8 行 / 接口 6 组×7 行 + 测试结果 +
//   静态文本 + 测试/恢复/保存取消命令。
// 语义与原来逐字一致：测试拿表单值直测（不用先保存）；保存先校验，
//   错当场拦（经 IDialogService 弹）；落盘后 AppConfig.Load 刷新内存，
//   置 PlcChanged 由主窗重启。
// 两个"恢复缺省"语义不同，原样保留：单机=出厂缺省（Default），
//   多机=ini 存盘值（撤销本次未保存修改）。
// 异步测试跑后台（铁律），回来经 UiInvoke 更新；按钮灰态经 Testing 属性。
// =========================================================================

using System.Collections.ObjectModel;
using PlcMesBridge.Core.Comms;
using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Core.Mes;

namespace PlcMesBridge.ViewModels;

/// <summary>单机/接口通用文本行（取值键 + 显示标签 + 值 + 框宽）。</summary>
public class ConfigTextRow : ViewModelBase
{
    private string _value;

    /// <param name="key">取值键（单机页=中文标签，接口页=trg:API0027 类复合键）。</param>
    /// <param name="displayLabelKey">显示标签键（走翻译，如"触发地址"）。</param>
    public ConfigTextRow(string key, string displayLabelKey, string value, int boxWidth = 170)
    {
        LabelKey = key;
        DisplayLabelKey = displayLabelKey;
        _value = value;
        BoxWidth = boxWidth;
    }

    public string LabelKey { get; }
    public string DisplayLabelKey { get; }
    public int BoxWidth { get; }

    public string Caption => LanguageService.Tr(DisplayLabelKey);

    public string Value
    {
        get => _value;
        set => Set(ref _value, value ?? "");
    }

    public void RefreshTexts() => RefreshAll();
}

/// <summary>多机单行（启用/IP/端口/协议/测试按钮/结果）。</summary>
public class StationRowViewModel : ViewModelBase
{
    private bool _enabled;
    private string _ip = "";
    private string _port = "";
    private int _protoIndex;
    private string _resultText = "";
    private string _resultForeground = "Black";
    private string? _resultTip;
    private bool _testing;

    public StationRowViewModel(int id)
    {
        Id = id;
    }

    public int Id { get; }

    public string TitleText => $"{Id + 1}号{LanguageService.Tr("启用")}";

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public string Ip
    {
        get => _ip;
        set => Set(ref _ip, value ?? "");
    }

    public string PortText
    {
        get => _port;
        set => Set(ref _port, value ?? "");
    }

    public int ProtoIndex
    {
        get => _protoIndex;
        set => Set(ref _protoIndex, value);
    }

    public string ResultText
    {
        get => _resultText;
        set => Set(ref _resultText, value);
    }

    public string ResultForeground
    {
        get => _resultForeground;
        set => Set(ref _resultForeground, value);
    }

    public string? ResultTip
    {
        get => _resultTip;
        set => Set(ref _resultTip, value);
    }

    public bool Testing
    {
        get => _testing;
        set => Set(ref _testing, value);
    }

    public void RefreshTexts() => RefreshAll();
}

/// <summary>接口一组（API0027 等 + 7 行：触发/URL/数据/MSG/DATA/状态/完成）。</summary>
public class ApiSectionViewModel : ViewModelBase
{
    public ApiSectionViewModel(string apiCode)
    {
        ApiCode = apiCode;
    }

    public string ApiCode { get; }

    public ObservableCollection<ConfigTextRow> Rows { get; } = new();

    public void RefreshTexts()
    {
        RefreshAll();
        foreach (ConfigTextRow r in Rows)
            r.RefreshTexts();
    }
}

public class PlcConfigViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly bool _useSim;
    private string _singleResultText = "";
    private string _singleResultForeground = "Black";
    private bool _singleTesting;

    public PlcConfigViewModel(IDialogService? dialogs = null)
    {
        _dialogs = dialogs ?? DialogService.Instance;
        _useSim = IniFile.ReadStr(AppPaths.ConfigIni, "setting", "Simulate", "0") == "1";
        // ---- 单机页（键=中文标签，与校验/取值一一对应）----
        var cfg = AppConfig.Single;
        AddSingle("IP地址", cfg.Ip);
        AddSingle("端口", cfg.Port.ToString(), 80);
        AddSingle("命令字", cfg.Cmd);
        AddSingle("完成位", cfg.Done);
        AddSingle("心跳地址", cfg.Live);
        AddSingle("固化时间地址", cfg.Curing);
        AddSingle("进入时间地址", cfg.EntryTime);
        AddSingle("PC时间地址", cfg.PcTime);
        AddSingle("库位地址", cfg.BinId);
        AddSingle("库位长度", cfg.BinLen.ToString(), 80);
        AddSingle("报警地址", cfg.Alarm);
        AddSingle("产品前缀", cfg.ProdPrefix, 80);
        AddSingle("产品起始", cfg.ProdStart.ToString(), 80);
        AddSingle("产品步长", cfg.ProdStep.ToString(), 80);
        AddSingle("产品个数", cfg.ProdCount.ToString(), 80);
        AddSingle("产品读长", cfg.ProdReadLen.ToString(), 80);
        AddSingle("产品写长", cfg.ProdWriteLen.ToString(), 80);
        // ---- 多机页 ----
        StationPlcConfig[] stations = PlcConfigStore.LoadStations(AppPaths.ConfigIni);
        for (int i = 0; i < 8; i++)
        {
            Stations.Add(new StationRowViewModel(i)
            {
                Enabled = stations[i].Enabled,
                Ip = stations[i].Ip,
                PortText = stations[i].Port.ToString(),
                ProtoIndex = stations[i].UseAscii ? 1 : 0,
            });
        }
        RefreshProtoOptions();
        // ---- 接口页 ----
        DataLengthText = MesConfig.DataLength.ToString();
        foreach (string api in MesConfig.ApiCodes)
        {
            var sec = new ApiSectionViewModel(api);
            sec.Rows.Add(new ConfigTextRow($"trg:{api}", "触发地址",
                MesConfig.GetTrigger(api)));
            int idx = Array.IndexOf(MesConfig.ApiCodes, api);
            var set = MesConfig.ApiAddresses[idx];
            sec.Rows.Add(new ConfigTextRow($"url:{api}", "URL", MesConfig.GetUrl(api), 340));
            sec.Rows.Add(new ConfigTextRow($"dat:{api}", "数据地址", set.PlcData));
            sec.Rows.Add(new ConfigTextRow($"msg:{api}", "MSG地址", set.MesMsg));
            sec.Rows.Add(new ConfigTextRow($"data:{api}", "DATA地址", set.MesData));
            sec.Rows.Add(new ConfigTextRow($"st:{api}", "状态地址", set.MesStatus));
            sec.Rows.Add(new ConfigTextRow($"done:{api}", "完成地址", set.MesComplete));
            ApiSections.Add(sec);
        }

        TestSingleCommand = new RelayCommand(
            async () => await TestSingleAsync(), () => !SingleTesting);
        TestStationCommand = new RelayCommand<int?>(async id =>
        {
            if (id.HasValue)
                await TestStationAsync(id.Value);
        });
        TestAllStationsCommand = new RelayCommand(
            async () => await TestAllStationsAsync(), () => !AnyStationTesting);
        RestoreSingleCommand = new RelayCommand(
            () => FillSingle(SinglePlcConfig.Default));
        RestoreStationsCommand = new RelayCommand(RestoreStationsFromIni);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    /// <summary>本次保存是否改了配置（主窗据此决定是否自动重启）。</summary>
    public bool PlcChanged { get; private set; }

    public event Action<bool?>? RequestClose;

    public ObservableCollection<ConfigTextRow> SingleRows { get; } = new();
    public ObservableCollection<StationRowViewModel> Stations { get; } = new();
    public ObservableCollection<ApiSectionViewModel> ApiSections { get; } = new();

    private string _dataLengthText = "799";
    public string DataLengthText
    {
        get => _dataLengthText;
        set => Set(ref _dataLengthText, value ?? "");
    }

    /// <summary>协议下拉选项（二进制/ASCII，语言切换重建）。</summary>
    public List<string> ProtoOptions { get; private set; } = new();

    public string SingleResultText
    {
        get => _singleResultText;
        private set => Set(ref _singleResultText, value);
    }

    public string SingleResultForeground
    {
        get => _singleResultForeground;
        private set => Set(ref _singleResultForeground, value);
    }

    public bool SingleTesting
    {
        get => _singleTesting;
        private set
        {
            if (Set(ref _singleTesting, value))
                TestSingleCommand.RaiseCanExecuteChanged();
        }
    }

    public bool AnyStationTesting
    {
        get
        {
            foreach (StationRowViewModel s in Stations)
            {
                if (s.Testing)
                    return true;
            }
            return false;
        }
    }

    public string TitleText => Tr("PLC配置");
    public string TabSingleText => Tr("单机PLC");
    public string TabMultiText => Tr("多机PLC");
    public string TabApiText => Tr("接口地址");
    public string SaveText => Tr("保存并重启");
    public string CancelText => Tr("取消");
    public string TestText => Tr("测试连接");
    public string TestAllText => Tr("全部测试");
    public string RestoreText => Tr("恢复缺省");
    public string HintText => Tr("测试用表单值直测保存后重启生效")
        + (_useSim ? "（" + Tr("模拟模式下配置仅对真机有效") + "）" : "");
    public string DataLengthLabel => Tr("数据长度");

    public RelayCommand TestSingleCommand { get; }
    public RelayCommand<int?> TestStationCommand { get; }
    public RelayCommand TestAllStationsCommand { get; }
    public RelayCommand RestoreSingleCommand { get; }
    public RelayCommand RestoreStationsCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public void RefreshTexts()
    {
        RefreshAll();
        RefreshProtoOptions();
        foreach (ConfigTextRow r in SingleRows)
            r.RefreshTexts();
        foreach (StationRowViewModel s in Stations)
            s.RefreshTexts();
        foreach (ApiSectionViewModel s in ApiSections)
            s.RefreshTexts();
    }

    private static string Tr(string zh) => LanguageService.Tr(zh);

    private void RefreshProtoOptions()
    {
        ProtoOptions = new List<string> { Tr("二进制"), "ASCII" };
        Raise(nameof(ProtoOptions));
    }

    private void AddSingle(string labelKey, string initial, int boxWidth = 170) =>
        SingleRows.Add(new ConfigTextRow(labelKey, labelKey, initial, boxWidth));

    private ConfigTextRow Single(string key)
    {
        foreach (ConfigTextRow r in SingleRows)
        {
            if (r.LabelKey == key)
                return r;
        }
        throw new KeyNotFoundException("单机行缺失: " + key);
    }

    private void FillSingle(SinglePlcConfig c)
    {
        Single("IP地址").Value = c.Ip;
        Single("端口").Value = c.Port.ToString();
        Single("命令字").Value = c.Cmd;
        Single("完成位").Value = c.Done;
        Single("心跳地址").Value = c.Live;
        Single("固化时间地址").Value = c.Curing;
        Single("进入时间地址").Value = c.EntryTime;
        Single("PC时间地址").Value = c.PcTime;
        Single("库位地址").Value = c.BinId;
        Single("库位长度").Value = c.BinLen.ToString();
        Single("报警地址").Value = c.Alarm;
        Single("产品前缀").Value = c.ProdPrefix;
        Single("产品起始").Value = c.ProdStart.ToString();
        Single("产品步长").Value = c.ProdStep.ToString();
        Single("产品个数").Value = c.ProdCount.ToString();
        Single("产品读长").Value = c.ProdReadLen.ToString();
        Single("产品写长").Value = c.ProdWriteLen.ToString();
    }

    private SinglePlcConfig GatherSingle() => new()
    {
        Ip = Single("IP地址").Value,
        Port = ParseInt(Single("端口").Value),
        Cmd = Single("命令字").Value,
        Done = Single("完成位").Value,
        Live = Single("心跳地址").Value,
        Curing = Single("固化时间地址").Value,
        EntryTime = Single("进入时间地址").Value,
        PcTime = Single("PC时间地址").Value,
        BinId = Single("库位地址").Value,
        BinLen = ParseInt(Single("库位长度").Value),
        Alarm = Single("报警地址").Value,
        ProdPrefix = Single("产品前缀").Value,
        ProdStart = ParseInt(Single("产品起始").Value),
        ProdStep = ParseInt(Single("产品步长").Value),
        ProdCount = ParseInt(Single("产品个数").Value),
        ProdReadLen = ParseInt(Single("产品读长").Value),
        ProdWriteLen = ParseInt(Single("产品写长").Value),
    };

    private async Task TestSingleAsync()
    {
        SingleTesting = true;
        SingleResultText = Tr("测试中...");
        try
        {
            SinglePlcConfig c = GatherSingle();
            var (ok, msg, _) = await PlcConnectionTester.TestAsync(
                () => new MelsecPlcClient(false), c.Ip.Trim(), c.Port,
                PlcConfigValidator.NormAddr(c.Cmd)).ConfigureAwait(false);
            UiInvoke(() =>
            {
                SingleResultText = (ok ? Tr("连接成功") + " " : Tr("连接失败") + " ") + msg;
                SingleResultForeground = ok ? "DarkGreen" : "Red";
            });
        }
        finally
        {
            UiInvoke(() => SingleTesting = false);
        }
    }

    // ================= 多机 =================

    private void RestoreStationsFromIni()
    {
        // 语义原样：恢复到 ini 存盘值（撤销本次未保存修改），不是出厂缺省。
        StationPlcConfig[] cs = PlcConfigStore.LoadStations(AppPaths.ConfigIni);
        for (int i = 0; i < 8; i++)
        {
            Stations[i].Enabled = cs[i].Enabled;
            Stations[i].Ip = cs[i].Ip;
            Stations[i].PortText = cs[i].Port.ToString();
            Stations[i].ProtoIndex = cs[i].UseAscii ? 1 : 0;
        }
    }

    private StationPlcConfig[] GatherStations()
    {
        var cs = new StationPlcConfig[8];
        for (int i = 0; i < 8; i++)
        {
            cs[i] = new StationPlcConfig
            {
                Enabled = Stations[i].Enabled,
                Ip = Stations[i].Ip,
                Port = ParseInt(Stations[i].PortText),
                UseAscii = Stations[i].ProtoIndex == 1,
            };
        }
        return cs;
    }

    private string Trigger0027()
    {
        // 读 0027 触发字验证可读（触发地址是全局配置，读当前接口页表单值）。
        foreach (ApiSectionViewModel sec in ApiSections)
        {
            if (sec.ApiCode != "API0027")
                continue;
            foreach (ConfigTextRow r in sec.Rows)
            {
                if (r.LabelKey == "trg:API0027")
                    return r.Value;
            }
        }
        return MesConfig.TriggerApi0027;
    }

    private async Task TestOneStationAsync(int id, StationPlcConfig[] cs, string trg)
    {
        StationRowViewModel row = Stations[id];
        row.Testing = true;
        TestAllStationsCommand.RaiseCanExecuteChanged();
        try
        {
            bool ascii = cs[id].UseAscii;
            var (ok, msg, _) = await PlcConnectionTester.TestAsync(
                () => new MelsecPlcClient(ascii), cs[id].Ip.Trim(), cs[id].Port,
                PlcConfigValidator.NormAddr(trg)).ConfigureAwait(false);
            UiInvoke(() =>
            {
                row.ResultText = ok ? Tr("连接成功") : Tr("连接失败");
                row.ResultForeground = ok ? "DarkGreen" : "Red";
                row.ResultTip = msg;
            });
        }
        finally
        {
            UiInvoke(() =>
            {
                row.Testing = false;
                TestAllStationsCommand.RaiseCanExecuteChanged();
            });
        }
    }

    private async Task TestStationAsync(int id)
    {
        StationPlcConfig[] cs = GatherStations();
        await TestOneStationAsync(id, cs, Trigger0027()).ConfigureAwait(false);
    }

    private async Task TestAllStationsAsync()
    {
        StationPlcConfig[] cs = GatherStations();
        string trg = Trigger0027();
        foreach (StationRowViewModel row in Stations)
            row.Testing = true;
        TestAllStationsCommand.RaiseCanExecuteChanged();
        try
        {
            for (int i = 0; i < 8; i++)
            {
                if (!cs[i].Enabled)
                {
                    Stations[i].ResultText = "—";
                    continue;
                }
                Stations[i].ResultText = Tr("测试中...");
                await TestOneStationAsync(i, cs, trg).ConfigureAwait(false);
            }
        }
        finally
        {
            UiInvoke(() =>
            {
                foreach (StationRowViewModel row in Stations)
                    row.Testing = false;
                TestAllStationsCommand.RaiseCanExecuteChanged();
            });
        }
    }

    // ================= 保存 =================

    private void Save()
    {
        SinglePlcConfig single = GatherSingle();
        StationPlcConfig[] stations = GatherStations();
        var errs = PlcConfigValidator.ValidateSingle(single);
        errs.AddRange(PlcConfigValidator.ValidateStations(stations));
        errs.AddRange(ValidateApi());

        if (errs.Count > 0)
        {
            string lines = string.Join("\r\n",
                errs.Select(x => $"{Tr(x.Field)}：{Tr(x.Reason)}"));
            _dialogs.ShowMessage($"{Tr("以下配置有误")}：\r\n{lines}",
                Tr("PLC配置"), DialogImage.Warning);
            return;
        }

        string ini = AppPaths.ConfigIni;
        PlcConfigStore.SaveSingle(ini, single);
        PlcConfigStore.SaveStations(ini, stations);
        SaveApiSection(ini);
        AppConfig.Load(); // 刷新内存（重启后会再读一次，此处保证本进程内一致）
        PlcChanged = true;
        RequestClose?.Invoke(true);
    }

    private List<(string Field, string Reason)> ValidateApi()
    {
        var errs = new List<(string, string)>();
        if (!int.TryParse(DataLengthText.Trim(), out int dl) || dl is < 1 or > 10000)
            errs.Add(("数据长度", "范围1-10000"));
        foreach (ApiSectionViewModel sec in ApiSections)
        {
            string api = sec.ApiCode;
            foreach (ConfigTextRow r in sec.Rows)
            {
                if (r.LabelKey.StartsWith("url:", StringComparison.Ordinal))
                {
                    string url = r.Value.Trim();
                    if (!string.IsNullOrEmpty(url) &&
                        (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
                         (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)))
                        errs.Add(($"{api} URL", "IP地址无效"));
                }
                else if (!PlcConfigValidator.IsPlcAddress(r.Value))
                {
                    errs.Add(($"{api} {r.LabelKey.Split(':')[0]}", "PLC地址格式须为D/R+数字"));
                }
            }
        }
        return errs;
    }

    private string ApiValue(string key)
    {
        foreach (ApiSectionViewModel sec in ApiSections)
        {
            foreach (ConfigTextRow r in sec.Rows)
            {
                if (r.LabelKey == key)
                    return r.Value;
            }
        }
        return "";
    }

    private void SaveApiSection(string ini)
    {
        string[] names = { "API0027", "API0028", "API0030", "API0031", "API0032", "API0033" };
        string[] urlKeys = { "Url_API0027", "Url_API0028", "Url_API0030", "Url_API0031", "Url_API0032", "Url_API0033" };
        string[] trgKeys = { "TriggerAddrAPI0027", "TriggerAddrAPI0028", "TriggerAddrAPI0030", "TriggerAddrAPI0031", "TriggerAddrAPI0032", "TriggerAddrAPI0033" };
        for (int i = 0; i < 6; i++)
        {
            IniFile.Write(ini, "MES", urlKeys[i], ApiValue($"url:{names[i]}").Trim());
            IniFile.Write(ini, "MES", trgKeys[i],
                PlcConfigValidator.NormAddr(ApiValue($"trg:{names[i]}")));
            IniFile.Write(ini, "MES", $"{names[i]}PLCDataAddr",
                PlcConfigValidator.NormAddr(ApiValue($"dat:{names[i]}")));
            IniFile.Write(ini, "MES", $"{names[i]}MESMSGAddr",
                PlcConfigValidator.NormAddr(ApiValue($"msg:{names[i]}")));
            IniFile.Write(ini, "MES", $"{names[i]}MESDataAddr",
                PlcConfigValidator.NormAddr(ApiValue($"data:{names[i]}")));
            IniFile.Write(ini, "MES", $"{names[i]}MESSTATUSAddr",
                PlcConfigValidator.NormAddr(ApiValue($"st:{names[i]}")));
            IniFile.Write(ini, "MES", $"{names[i]}MESCompleteAddr",
                PlcConfigValidator.NormAddr(ApiValue($"done:{names[i]}")));
        }
        IniFile.Write(ini, "MES", "DataLength", DataLengthText.Trim());
    }

    private static int ParseInt(string s) =>
        int.TryParse((s ?? "").Trim(), out int v) ? v : 0;
}
