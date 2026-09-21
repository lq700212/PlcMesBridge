using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Core.Mes;

namespace PlcMesBridge;

/// <summary>
/// 调试窗（复刻单机版 FormMESDebug，追加 API0033）。
/// API0033 字段说明：多机版新增的不良明细接口，15 个字段，不在 mes_params.json
/// 记忆范围内（文件格式保持 16 字段不变，保证老配置兼容）。调试窗内扩展定义：
/// 填值可预览/上传；点"保存"时仅 16 记忆字段落盘，0033 专有字段不保存。
/// </summary>
public partial class MesDebugWindow : Window
{
    private readonly Dictionary<string, TextBox> _edits = new();

    /// <summary>0033 专有字段（字段名, 中文说明）。记忆参数里没有，仅本窗使用。</summary>
    private static readonly (string Field, string Desc)[] Extra0033 =
    {
        ("BOARD_SN", "板号SN"), ("TOP_BTM", "顶底层"), ("LINE_NO", "线别"),
        ("S_TEST_TIME", "测试开始时间"), ("E_TEST_TIME", "测试结束时间"),
        ("CHK_STATUS", "检测状态"), ("DOT_LOC", "点位"),
        ("BIN_LOC", "料仓位"), ("IMG_LOC", "图片位置"),
        ("ERR_NO", "错误码"), ("SCR_ACTUAL", "实测值"),
        ("SCR_STD", "标准值"), ("SCR_MAX", "上限"),
    };

    private static readonly Dictionary<string, string[]> Required0033 = new()
    {
        ["API0033"] = new[]
        {
            "RC_NO", "BOARD_SN", "TOP_BTM", "LINE_NO", "PT_NO",
            "S_TEST_TIME", "E_TEST_TIME", "CHK_STATUS", "DOT_LOC", "BIN_LOC",
            "IMG_LOC", "ERR_NO", "SCR_ACTUAL", "SCR_STD", "SCR_MAX", "PLANT",
        },
    };

    public MesDebugWindow()
    {
        InitializeComponent();
        BuildForm();
        MesParamStore.LoadParams();
        LoadToUi();
        CmbApi.Items.Add("API0027");
        CmbApi.Items.Add("API0028");
        CmbApi.Items.Add("API0030");
        CmbApi.Items.Add("API0031");
        CmbApi.Items.Add("API0032");
        CmbApi.Items.Add("API0033");
        CmbApi.SelectedIndex = 0;
        UpdateUI();
    }

    /// <summary>动态表单：16 记忆字段 + 13 个 0033 专有字段（去重 RC_NO/PT_NO/PLANT）。</summary>
    private void BuildForm()
    {
        PanelParams.Children.Clear();
        _edits.Clear();
        foreach (var (field, desc) in MesParamStore.AllFields)
            AddField(field, desc);
        foreach (var (field, desc) in Extra0033)
            AddField(field, desc);
    }

    private void AddField(string field, string desc)
    {
        var panel = new StackPanel
        {
            Width = 240, Height = 52, Margin = new Thickness(5), Tag = field,
        };
        var lbl = new Label
        {
            Content = $"{field} ({desc})", Foreground = Brushes.DarkBlue,
            Padding = new Thickness(0),
        };
        var txt = new TextBox { Name = "txt_" + field };
        txt.TextChanged += (_, _) => TxtPreview.Text = BuildJson();
        panel.Children.Add(lbl);
        panel.Children.Add(txt);
        PanelParams.Children.Add(panel);
        _edits[field] = txt;
    }

    private void CmbApi_Changed(object sender, SelectionChangedEventArgs e) =>
        ApplyVisibility();

    /// <summary>按接口显隐字段（老项目 cmbApiType_SelectedIndexChanged 语义）。</summary>
    private void ApplyVisibility()
    {
        string api = CmbApi.SelectedItem?.ToString() ?? "API0027";
        string[] required = api == "API0033"
            ? Required0033[api]
            : MesParamStore.RequiredFields.TryGetValue(api, out var r) ? r : Array.Empty<string>();
        foreach (System.Windows.UIElement child in PanelParams.Children)
        {
            if (child is StackPanel p && p.Tag is string f)
                p.Visibility = required.Contains(f) ? Visibility.Visible : Visibility.Collapsed;
        }
        TxtPreview.Text = BuildJson();
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        foreach (System.Windows.UIElement child in PanelParams.Children)
        {
            if (child is not StackPanel p || p.Tag is not string f)
                continue;
            string desc = FindDesc(f);
            if (p.Children[0] is Label lbl)
                lbl.Content = $"{f} ({LanguageService.Tr(desc)})";
        }
    }

    private string FindDesc(string field)
    {
        foreach (var (f, d) in MesParamStore.AllFields)
            if (f == field)
                return d;
        foreach (var (f, d) in Extra0033)
            if (f == field)
                return d;
        return field;
    }

    private void LoadToUi()
    {
        foreach (var (field, _) in MesParamStore.AllFields)
            if (_edits.TryGetValue(field, out var txt))
                txt.Text = MesParamStore.Get(field);
    }

    private void SaveFromUi()
    {
        foreach (var (field, _) in MesParamStore.AllFields)
            if (_edits.TryGetValue(field, out var txt))
                MesParamStore.Set(field, txt.Text);
        MesParamStore.SaveParams();
    }

    private string Get(string field) =>
        _edits.TryGetValue(field, out var txt) ? txt.Text : "";

    /// <summary>组报文（老项目 BuildJsonString 语义：0027/0028/0033 外层数组，其余对象）。</summary>
    private string BuildJson()
    {
        string api = CmbApi.SelectedItem?.ToString() ?? "API0027";
        return api switch
        {
            "API0027" => JsonConvert.SerializeObject(new List<MesModels.Api0027Request>
                { New0027() }, Formatting.None),
            "API0028" => JsonConvert.SerializeObject(new List<MesModels.Api0028Request>
                { New0028() }, Formatting.None),
            "API0030" => JsonConvert.SerializeObject(New0030(), Formatting.None),
            "API0031" => JsonConvert.SerializeObject(New0031(), Formatting.None),
            "API0032" => JsonConvert.SerializeObject(New0032(), Formatting.None),
            "API0033" => JsonConvert.SerializeObject(new List<MesModels.Api0033Request>
                { New0033() }, Formatting.None),
            _ => "",
        };
    }

    private MesModels.Api0027Request New0027() => new()
    {
        RC_NO = Get("RC_NO"), PT_NO = Get("PT_NO"), MAC_NO = Get("MAC_NO"),
        MAC_LOC_NO = Get("MAC_LOC_NO"), PRA_CODE = Get("PRA_CODE"),
        PRA_Value = Get("PRA_Value"), Collect_Datetime = Get("Collect_Datetime"),
        PLANT = Get("PLANT"),
    };

    private MesModels.Api0028Request New0028() => new()
    {
        RC_NO = Get("RC_NO"), PT_NO = Get("PT_NO"), MAC_NO = Get("MAC_NO"),
        MAC_LOC_NO = Get("MAC_LOC_NO"), PRA_CODE = Get("PRA_CODE"),
        PRA_Value = Get("PRA_Value"), PLANT = Get("PLANT"),
    };

    private MesModels.Api0030Request New0030() => new()
    {
        RC_NO = Get("RC_NO"), ACTION = Get("ACTION"), BC_NO = Get("BC_NO"),
        PT_NO = Get("PT_NO"), SP_MEMO = Get("SP_MEMO"), MAC_NO = Get("MAC_NO"),
        MAC_LOC_NO = Get("MAC_LOC_NO"), TL_EL_NO = Get("TL_EL_NO"),
        IN_DATETIME = Get("IN_DATETIME"), CHECK_RESULT = Get("CHECK_RESULT"),
        PLANT = Get("PLANT"),
    };

    private MesModels.Api0031Request New0031() => new()
    {
        RC_NO = Get("RC_NO"), BC_NO = Get("BC_NO"), PT_NO = Get("PT_NO"),
        EMP_NO = Get("EMP_NO"), MAC_NO = Get("MAC_NO"),
        MAC_LOC_NO = Get("MAC_LOC_NO"), BO_NUMBER = Get("BO_NUMBER"),
        PLANT = Get("PLANT"),
    };

    private MesModels.Api0032Request New0032() => new()
    {
        RC_NO = Get("RC_NO"), BC_NO = Get("BC_NO"), PT_NO = Get("PT_NO"),
        EMP_NO = Get("EMP_NO"), MAC_NO = Get("MAC_NO"),
        MAC_LOC_NO = Get("MAC_LOC_NO"), IN_DATETIME = Get("IN_DATETIME"),
        PLANT = Get("PLANT"),
    };

    private MesModels.Api0033Request New0033() => new()
    {
        RC_NO = Get("RC_NO"), BOARD_SN = Get("BOARD_SN"), TOP_BTM = Get("TOP_BTM"),
        LINE_NO = Get("LINE_NO"), PT_NO = Get("PT_NO"),
        S_TEST_TIME = Get("S_TEST_TIME"), E_TEST_TIME = Get("E_TEST_TIME"),
        CHK_STATUS = Get("CHK_STATUS"), DOT_LOC = Get("DOT_LOC"),
        BIN_LOC = Get("BIN_LOC"), IMG_LOC = Get("IMG_LOC"),
        ERR_NO = Get("ERR_NO"), SCR_ACTUAL = Get("SCR_ACTUAL"),
        SCR_STD = Get("SCR_STD"), SCR_MAX = Get("SCR_MAX"),
    };

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveFromUi();
        MessageBox.Show(LanguageService.Tr("参数已成功保存到本地并生效全局!"));
    }

    /// <summary>手动上传（老项目 btnUpload_Click：先保存→后台 POST→回 UI 显示→非 1 弹报警）。</summary>
    private void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        SaveFromUi();
        string api = CmbApi.SelectedItem?.ToString() ?? "API0027";
        string url = MesConfig.GetUrl(api);
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show(LanguageService.Tr("发生错误:") + " URL 未在 MESConfig 中配置");
            return;
        }
        string json = BuildJson();
        BtnUpload.IsEnabled = false;
        TxtResp.Text = $"{LanguageService.Tr("请求中...")} URL: {url}\r\nJSON: {json}";
        Task.Run(async () =>
        {
            var (ok, resp) = await MesHttpClient.PostJsonAsync(url, json).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                TxtResp.Text = resp;
                BtnUpload.IsEnabled = true;
                try
                {
                    var obj = JsonConvert.DeserializeObject<MesModels.BaseResponse>(resp);
                    if (!ok || (obj != null && obj.STATUS != "1"))
                        new MesAlarmWindow(resp, "MES Debug Return").Show();
                }
                catch
                {
                    new MesAlarmWindow(
                        LanguageService.Tr("发生错误:") + "\r\n" + resp, "Parse Error").Show();
                }
            });
        });
    }

    private void BtnLang_Click(object sender, RoutedEventArgs e)
    {
        LanguageService.CurrentLanguage =
            LanguageService.CurrentLanguage == "CH" ? "EN" : "CH";
        IniFile.Write(AppPaths.ConfigIni, "setting", "Language",
            LanguageService.CurrentLanguage);
        UpdateUI();
        RefreshLabels();
    }

    private void UpdateUI()
    {
        Title = LanguageService.Tr("MES 接口调试界面");
        LbApi.Content = LanguageService.Tr("接口类型:");
        BtnSave.Content = LanguageService.Tr("保存参数到本地");
        BtnUpload.Content = LanguageService.Tr("手动上传到MES");
        LbPreview.Content = LanguageService.Tr("发送报文预览:");
        LbResp.Content = LanguageService.Tr("MES 返回信息:");
        BtnLang.Content = LanguageService.CurrentLanguage == "CH" ? "English" : "中文";
    }
}
