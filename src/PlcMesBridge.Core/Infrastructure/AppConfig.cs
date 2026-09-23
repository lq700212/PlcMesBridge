// =========================================================================
// AppConfig：全部启动配置加载（复刻自两版 Form1_Load 的 ini 读取，集中一处）
//
// 单机：[setting] Language + [MES] 5 个 URL（缺省 192.168.120.129:5012）。
// 多机：PLCuse 开关串 / PLCIP2..10 / 存图目录 / 6 URL + 6 触发地址 +
//   DataLength + 6 组地址束（xxxPLCDataAddr/MESMSGAddr/MESDataAddr/
//   MESSTATUSAddr/MESCompleteAddr，缺省 R 系，见 MultiMachineGateway 注释）。
// Mode（新增，融合版开关）：Single=单机固化收料 / Multi=多机网关，
//   记 [setting] Mode，缺省 Single（老用户升级无感）。
// =========================================================================

using PlcMesBridge.Core.Mes;

namespace PlcMesBridge.Core.Infrastructure;

public enum RunMode
{
    Single,
    Multi,
}

public static class AppConfig
{
    public static RunMode Mode { get; private set; } = RunMode.Single;
    public static string[] PlcUse { get; private set; } =
        new[] { "1", "1", "1", "1", "1", "1", "1", "1" };

    /// <summary>8 台 PLC IP（键 PLCIP2/3/4/5/6/7/9/10，老项目跳号命名原样）。</summary>
    public static string[] PlcIps { get; private set; } = new string[8];

    /// <summary>8 台端口（新键 [setting] PLCPorts，缺省全 6060，老项目全写死 6060）。</summary>
    public static int[] StationPorts { get; private set; } =
        new[] { 6060, 6060, 6060, 6060, 6060, 6060, 6060, 6060 };

    /// <summary>
    /// 8 台协议（新键 [setting] PLCAscii，缺省 1/2/5 号机 ASCII。
    /// AsciiStations 是出厂缺省常量（供 PlcConfigStore 用），运行时以本数组为准。
    /// </summary>
    public static bool[] StationAscii { get; private set; } =
        new[] { false, true, true, false, false, true, false, false };

    /// <summary>
    /// 单机全套 PLC 配置（新段 [SinglePLC]，缺省=老项目硬编码）。
    /// internal 写：测试隔离（TestScope 快照恢复）用，生产只读。
    /// </summary>
    public static SinglePlcConfig Single { get; internal set; } = SinglePlcConfig.Default;

    /// <summary>ASCII 协议机台（老项目 i=1,2,5 用 MelsecMcAsciiNet，其余二进制）。</summary>
    public static readonly HashSet<int> AsciiStations = new() { 1, 2, 5 };

    private static readonly string[] IpKeys = PlcConfigStore.IpKeys;

    private static readonly string[] DefaultUrls =
    {
        "http://192.168.120.129:5012/MacProduction/MacParameters",
        "http://192.168.120.129:5012/MacProduction/InsMachineDAQ",
        "http://192.168.120.129:5012/MacTraceability/BoardInOut",
        "http://192.168.120.129:5012/MacTraceability/Prelogcheck",
        "http://192.168.120.129:5012/MacTraceability/UploadMaterial",
        "http://192.168.120.129:5012/MacTraceability/UploadBadDetail",
    };

    private static readonly string[] DefaultTriggers =
        { "R13000", "R16000", "R17000", "R18000", "R19000", "R12000" };

    private static readonly string[] DefaultPlcData =
        { "R13001", "R16001", "R17001", "R18001", "R19001", "R12001" };
    private static readonly string[] DefaultMsgs =
        { "R13810", "R16810", "R17810", "R18810", "R19810", "R12810" };
    private static readonly string[] DefaultDatas =
        { "R13900", "R16900", "R17900", "R18900", "R19900", "R12900" };
    private static readonly string[] DefaultStatuses =
        { "R13801", "R16801", "R17801", "R18801", "R19801", "R12801" };
    private static readonly string[] DefaultCompletes =
        { "R13800", "R16800", "R17800", "R18800", "R19800", "R12800" };

    /// <summary>加载全部配置（启动时调一次；ini 缺键自动回填缺省，见 IniFile）。</summary>
    public static void Load(string? iniPath = null)
    {
        string ini = iniPath ?? AppPaths.ConfigIni;

        LanguageService.CurrentLanguage = IniFile.ReadStr(ini, "setting", "Language", "CH");
        string mode = IniFile.ReadStr(ini, "setting", "Mode", "Single");
        Mode = mode.Equals("Multi", StringComparison.OrdinalIgnoreCase)
            ? RunMode.Multi : RunMode.Single;

        string use = IniFile.ReadStr(ini, "setting", "PLCuse", "1,1,1,1,1,1,1,1");
        string[] parts = use.Split(',');
        for (int i = 0; i < 8; i++)
            PlcUse[i] = i < parts.Length ? parts[i].Trim() : "1";

        for (int i = 0; i < 8; i++)
            PlcIps[i] = IniFile.ReadStr(ini, "setting", IpKeys[i], "127.0.0.1");

        // 单机 + 多机端口/协议走 PlcConfigStore（自愈缺省，见 PlcConfig.cs）。
        Single = PlcConfigStore.LoadSingle(ini);
        StationPlcConfig[] stations = PlcConfigStore.LoadStations(ini);
        for (int i = 0; i < 8; i++)
        {
            PlcUse[i] = stations[i].Enabled ? "1" : "0";
            PlcIps[i] = stations[i].Ip;
            StationPorts[i] = stations[i].Port;
            StationAscii[i] = stations[i].UseAscii;
        }

        MesConfig.UrlApi0027 = IniFile.ReadStr(ini, "MES", "Url_API0027", DefaultUrls[0]);
        MesConfig.UrlApi0028 = IniFile.ReadStr(ini, "MES", "Url_API0028", DefaultUrls[1]);
        MesConfig.UrlApi0030 = IniFile.ReadStr(ini, "MES", "Url_API0030", DefaultUrls[2]);
        MesConfig.UrlApi0031 = IniFile.ReadStr(ini, "MES", "Url_API0031", DefaultUrls[3]);
        MesConfig.UrlApi0032 = IniFile.ReadStr(ini, "MES", "Url_API0032", DefaultUrls[4]);
        MesConfig.UrlApi0033 = IniFile.ReadStr(ini, "MES", "Url_API0033", DefaultUrls[5]);

        MesConfig.TriggerApi0027 = IniFile.ReadStr(ini, "MES", "TriggerAddrAPI0027", DefaultTriggers[0]);
        MesConfig.TriggerApi0028 = IniFile.ReadStr(ini, "MES", "TriggerAddrAPI0028", DefaultTriggers[1]);
        MesConfig.TriggerApi0030 = IniFile.ReadStr(ini, "MES", "TriggerAddrAPI0030", DefaultTriggers[2]);
        MesConfig.TriggerApi0031 = IniFile.ReadStr(ini, "MES", "TriggerAddrAPI0031", DefaultTriggers[3]);
        MesConfig.TriggerApi0032 = IniFile.ReadStr(ini, "MES", "TriggerAddrAPI0032", DefaultTriggers[4]);
        MesConfig.TriggerApi0033 = IniFile.ReadStr(ini, "MES", "TriggerAddrAPI0033", DefaultTriggers[5]);
        // DataLength 必须容错：现场手改 ini 写错一个字符不能让启动崩（老项目无此键，
        // 新项目新增；非法值回填缺省 799，保证网关读 JSON 长度永远可用）。
        string dataLenRaw = IniFile.ReadStr(ini, "MES", "DataLength", "799");
        if (int.TryParse(dataLenRaw, out int dataLen) && dataLen > 0)
        {
            MesConfig.DataLength = dataLen;
        }
        else
        {
            // 非法/非正数：回填缺省再取缺省（与 IniFile 自愈同一思想，见类头）。
            IniFile.Write(ini, "MES", "DataLength", "799");
            MesConfig.DataLength = 799;
        }

        string[] names = { "API0027", "API0028", "API0030", "API0031", "API0032", "API0033" };
        for (int i = 0; i < 6; i++)
        {
            var set = MesConfig.ApiAddresses[i];
            set.PlcData = IniFile.ReadStr(ini, "MES", $"{names[i]}PLCDataAddr", DefaultPlcData[i]);
            set.MesMsg = IniFile.ReadStr(ini, "MES", $"{names[i]}MESMSGAddr", DefaultMsgs[i]);
            set.MesData = IniFile.ReadStr(ini, "MES", $"{names[i]}MESDataAddr", DefaultDatas[i]);
            set.MesStatus = IniFile.ReadStr(ini, "MES", $"{names[i]}MESSTATUSAddr", DefaultStatuses[i]);
            set.MesComplete = IniFile.ReadStr(ini, "MES", $"{names[i]}MESCompleteAddr", DefaultCompletes[i]);
        }

        MesParamStore.LoadParams();
    }
}
