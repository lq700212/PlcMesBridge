// =========================================================================
// PlcConfig：PLC 可配置模型 + ini 落盘 + 校验（去现场零写代码的关键件）
//
// 为什么有这个文件：原来单机 IP/11 个命令地址、多机端口/协议全写死在代码里，
// 换机房/换 PLC 点位就得找开发改代码。收敛成配置对象后，现场只点鼠标：
// PLC配置窗填表 → 测试连接变绿 → 保存重启，全程不碰代码不碰记事本。
//
// 三段式（与 RunModeSettings 同一思想，纯逻辑进 Core 可测）：
// ① SinglePlcConfig / StationPlcConfig：数据 + 缺省（缺省=老项目硬编码，
//    老 ini 无新键时行为与原来逐字一致）；
// ② PlcConfigStore：ini 自愈读写（缺键回填缺省，见 IniFile）；
// ③ PlcConfigValidator：保存前校验（三菱地址 ^[DR]\d+$ / 端口 1..65535 /
//    IP 或主机名），错在界面就拦，不进轮询。
//
// ini 落盘位置：
// [SinglePLC] 单机全套；[setting] 多机沿用老键（PLCuse/PLCIP2..10 跳号沿用）
//   + 新增 PLCPorts（8 个逗号串，0..7 号机顺序）/ PLCAscii（8 个 0/1，
//   缺省 0,1,1,0,0,1,0,0 = 老项目 1/2/5 号机 ASCII）；
// [MES] 6 接口地址束沿用 AppConfig 现有键，本文件只提供 SaveApiSection
//   给配置窗统一落盘（读仍走 AppConfig.Load）。
// =========================================================================

using System.Net;
using System.Text.RegularExpressions;

namespace PlcMesBridge.Core.Infrastructure;

public class SinglePlcConfig
{
    public string Ip { get; set; } = "192.168.1.80";
    public int Port { get; set; } = 6060;

    public string Cmd { get; set; } = "D30001";
    public string Done { get; set; } = "D30011";
    public string Live { get; set; } = "D30010";
    public string Curing { get; set; } = "D30005";
    public string EntryTime { get; set; } = "D30032";
    public string PcTime { get; set; } = "D30014";
    public string BinId { get; set; } = "D30040";
    public int BinLen { get; set; } = 10;

    /// <summary>产品区前缀（老项目 "D3"）。</summary>
    public string ProdPrefix { get; set; } = "D3";
    /// <summary>产品区起始号（老项目 i=1 时 1*40+10=50）。</summary>
    public int ProdStart { get; set; } = 50;
    /// <summary>产品区步长（老项目每产品 40 字）。</summary>
    public int ProdStep { get; set; } = 40;
    public int ProdCount { get; set; } = 160;
    public int ProdReadLen { get; set; } = 40;
    public int ProdWriteLen { get; set; } = 80;

    /// <summary>MES 报警位（老项目缺省 D30013；读链：新键→老 MESPLCALarm→缺省）。</summary>
    public string Alarm { get; set; } = "D30013";

    public static SinglePlcConfig Default => new();

    public SinglePlcConfig Clone() => (SinglePlcConfig)MemberwiseClone();

    /// <summary>
    /// 第 i 个产品地址（1 起）。缺省 D3/50/40 → i=1 得 "D30050"，
    /// 与老项目 "D3"+(i*40+10).ToString("D4") 逐字一致。
    /// </summary>
    public string ProductAddr(int i) =>
        $"{ProdPrefix}{(ProdStart + (i - 1) * ProdStep):D4}";
}

public class StationPlcConfig
{
    public bool Enabled { get; set; } = true;
    public string Ip { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6060;
    public bool UseAscii { get; set; }

    public StationPlcConfig Clone() => (StationPlcConfig)MemberwiseClone();
}

/// <summary>校验结果：空=通过；否则每条 (字段中文键, 原因中文键)，UI 层 Tr 后显示。</summary>
public static class PlcConfigValidator
{
    private static readonly Regex AddrRx =
        new(@"^[DR]\d{1,5}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HostRx =
        new(@"^[A-Za-z0-9]([A-Za-z0-9.\-]*[A-Za-z0-9])?$", RegexOptions.Compiled);

    /// <summary>三菱字地址（只认 D/R 区：X/Y 是位区，读字会失败，保存时直接拦）。</summary>
    public static bool IsPlcAddress(string? s) =>
        AddrRx.IsMatch((s ?? "").Trim());

    public static string NormAddr(string s) => s.Trim().ToUpperInvariant();

    public static bool IsIpOrHost(string? s)
    {
        string v = (s ?? "").Trim();
        if (v.Length == 0)
            return false;
        return IPAddress.TryParse(v, out _) || HostRx.IsMatch(v);
    }

    public static List<(string Field, string Reason)> ValidateSingle(SinglePlcConfig c)
    {
        var errs = new List<(string, string)>();
        if (!IsIpOrHost(c.Ip))
            errs.Add(("IP地址", "IP地址无效"));
        if (c.Port is < 1 or > 65535)
            errs.Add(("端口", "端口范围1-65535"));
        CheckAddr(errs, "命令字", c.Cmd);
        CheckAddr(errs, "完成位", c.Done);
        CheckAddr(errs, "心跳地址", c.Live);
        CheckAddr(errs, "固化时间地址", c.Curing);
        CheckAddr(errs, "进入时间地址", c.EntryTime);
        CheckAddr(errs, "PC时间地址", c.PcTime);
        CheckAddr(errs, "库位地址", c.BinId);
        CheckAddr(errs, "报警地址", c.Alarm);
        if (c.BinLen is < 1 or > 1000)
            errs.Add(("库位长度", "长度范围1-1000"));
        if (string.IsNullOrWhiteSpace(c.ProdPrefix))
            errs.Add(("产品前缀", "不能为空"));
        if (c.ProdStart is < 0 or > 99999)
            errs.Add(("产品起始", "范围0-99999"));
        if (c.ProdStep is < 1 or > 10000)
            errs.Add(("产品步长", "范围1-10000"));
        if (c.ProdCount is < 1 or > 1000)
            errs.Add(("产品个数", "范围1-1000"));
        if (c.ProdReadLen is < 1 or > 1000)
            errs.Add(("产品读长", "长度范围1-1000"));
        if (c.ProdWriteLen is < 1 or > 1000)
            errs.Add(("产品写长", "长度范围1-1000"));
        return errs;
    }

    public static List<(string Field, string Reason)> ValidateStations(
        StationPlcConfig[] cs)
    {
        var errs = new List<(string, string)>();
        for (int i = 0; i < cs.Length; i++)
        {
            if (!cs[i].Enabled)
                continue; // 不启用的机台不校验（省得现场为备用机填假 IP 还被拦）。
            string tag = $"{i + 1}号机";
            if (!IsIpOrHost(cs[i].Ip))
                errs.Add((tag + "IP地址", "IP地址无效"));
            if (cs[i].Port is < 1 or > 65535)
                errs.Add((tag + "端口", "端口范围1-65535"));
        }
        return errs;
    }

    private static void CheckAddr(
        List<(string, string)> errs, string field, string? addr)
    {
        if (!IsPlcAddress(addr))
            errs.Add((field, "PLC地址格式须为D/R+数字"));
    }
}

public static class PlcConfigStore
{
    /// <summary>多机 IP 跳号键（历史沿用，对应 0..7 号机，见 AppConfig）。</summary>
    internal static readonly string[] IpKeys =
        { "PLCIP2", "PLCIP3", "PLCIP4", "PLCIP5", "PLCIP6", "PLCIP7", "PLCIP9", "PLCIP10" };

    /// <summary>ASCII 缺省（老项目 1/2/5 号机走 ASCII，即下标 1,2,5）。</summary>
    internal const string DefaultAscii = "0,1,1,0,0,1,0,0";

    /// <summary>读单机配置（缺键自愈回填缺省，老 ini 直接可用）。</summary>
    public static SinglePlcConfig LoadSingle(string iniPath)
    {
        var c = new SinglePlcConfig
        {
            Ip = IniFile.ReadStr(iniPath, "SinglePLC", "IP", "192.168.1.80"),
            Port = IniFile.ReadInt(iniPath, "SinglePLC", "Port", 6060),
            Cmd = PlcConfigValidator.NormAddr(IniFile.ReadStr(iniPath, "SinglePLC", "Cmd", "D30001")),
            Done = PlcConfigValidator.NormAddr(IniFile.ReadStr(iniPath, "SinglePLC", "Done", "D30011")),
            Live = PlcConfigValidator.NormAddr(IniFile.ReadStr(iniPath, "SinglePLC", "Live", "D30010")),
            Curing = PlcConfigValidator.NormAddr(IniFile.ReadStr(iniPath, "SinglePLC", "Curing", "D30005")),
            EntryTime = PlcConfigValidator.NormAddr(IniFile.ReadStr(iniPath, "SinglePLC", "EntryTime", "D30032")),
            PcTime = PlcConfigValidator.NormAddr(IniFile.ReadStr(iniPath, "SinglePLC", "PcTime", "D30014")),
            BinId = PlcConfigValidator.NormAddr(IniFile.ReadStr(iniPath, "SinglePLC", "BinId", "D30040")),
            BinLen = IniFile.ReadInt(iniPath, "SinglePLC", "BinLen", 10),
            ProdPrefix = IniFile.ReadStr(iniPath, "SinglePLC", "ProdPrefix", "D3").Trim(),
            ProdStart = IniFile.ReadInt(iniPath, "SinglePLC", "ProdStart", 50),
            ProdStep = IniFile.ReadInt(iniPath, "SinglePLC", "ProdStep", 40),
            ProdCount = IniFile.ReadInt(iniPath, "SinglePLC", "ProdCount", 160),
            ProdReadLen = IniFile.ReadInt(iniPath, "SinglePLC", "ProdReadLen", 40),
            ProdWriteLen = IniFile.ReadInt(iniPath, "SinglePLC", "ProdWriteLen", 80),
        };
        // 报警位读链（兼容老现场）：新键 SinglePLC.Alarm → 老键 setting.MESPLCALarm → D30013。
        string alarmNew = IniFile.ReadStr(iniPath, "SinglePLC", "Alarm", "");
        if (!string.IsNullOrWhiteSpace(alarmNew))
            c.Alarm = PlcConfigValidator.NormAddr(alarmNew);
        else
            c.Alarm = PlcConfigValidator.NormAddr(
                IniFile.ReadStr(iniPath, "setting", "MESPLCALarm", "D30013"));
        if (string.IsNullOrWhiteSpace(c.ProdPrefix))
            c.ProdPrefix = "D3";
        return c;
    }

    /// <summary>存单机配置（地址统一大写去空格，保证 ini 干净）。</summary>
    public static void SaveSingle(string iniPath, SinglePlcConfig c)
    {
        IniFile.Write(iniPath, "SinglePLC", "IP", (c.Ip ?? "").Trim());
        IniFile.Write(iniPath, "SinglePLC", "Port", c.Port.ToString());
        IniFile.Write(iniPath, "SinglePLC", "Cmd", PlcConfigValidator.NormAddr(c.Cmd));
        IniFile.Write(iniPath, "SinglePLC", "Done", PlcConfigValidator.NormAddr(c.Done));
        IniFile.Write(iniPath, "SinglePLC", "Live", PlcConfigValidator.NormAddr(c.Live));
        IniFile.Write(iniPath, "SinglePLC", "Curing", PlcConfigValidator.NormAddr(c.Curing));
        IniFile.Write(iniPath, "SinglePLC", "EntryTime", PlcConfigValidator.NormAddr(c.EntryTime));
        IniFile.Write(iniPath, "SinglePLC", "PcTime", PlcConfigValidator.NormAddr(c.PcTime));
        IniFile.Write(iniPath, "SinglePLC", "BinId", PlcConfigValidator.NormAddr(c.BinId));
        IniFile.Write(iniPath, "SinglePLC", "BinLen", c.BinLen.ToString());
        IniFile.Write(iniPath, "SinglePLC", "ProdPrefix", (c.ProdPrefix ?? "D3").Trim());
        IniFile.Write(iniPath, "SinglePLC", "ProdStart", c.ProdStart.ToString());
        IniFile.Write(iniPath, "SinglePLC", "ProdStep", c.ProdStep.ToString());
        IniFile.Write(iniPath, "SinglePLC", "ProdCount", c.ProdCount.ToString());
        IniFile.Write(iniPath, "SinglePLC", "ProdReadLen", c.ProdReadLen.ToString());
        IniFile.Write(iniPath, "SinglePLC", "ProdWriteLen", c.ProdWriteLen.ToString());
        IniFile.Write(iniPath, "SinglePLC", "Alarm", PlcConfigValidator.NormAddr(c.Alarm));
    }

    /// <summary>读 8 台配置（IP 键跳号沿用；端口/协议是新键，缺省 6060/二进制）。</summary>
    public static StationPlcConfig[] LoadStations(string iniPath)
    {
        string use = IniFile.ReadStr(iniPath, "setting", "PLCuse", "1,1,1,1,1,1,1,1");
        string[] useParts = use.Split(',');
        string ports = IniFile.ReadStr(iniPath, "setting", "PLCPorts",
            "6060,6060,6060,6060,6060,6060,6060,6060");
        string[] portParts = ports.Split(',');
        string ascii = IniFile.ReadStr(iniPath, "setting", "PLCAscii", DefaultAscii);
        string[] asciiParts = ascii.Split(',');
        var cs = new StationPlcConfig[8];
        for (int i = 0; i < 8; i++)
        {
            cs[i] = new StationPlcConfig
            {
                Enabled = (i < useParts.Length ? useParts[i].Trim() : "1") == "1",
                Ip = IniFile.ReadStr(iniPath, "setting", IpKeys[i], "127.0.0.1"),
                Port = int.TryParse(i < portParts.Length ? portParts[i].Trim() : "",
                    out int p) && p is >= 1 and <= 65535 ? p : 6060,
                UseAscii = (i < asciiParts.Length ? asciiParts[i].Trim() : "0") == "1",
            };
        }
        return cs;
    }

    /// <summary>存 8 台配置（IP 仍写跳号键，老 ini/老版本程序互认）。</summary>
    public static void SaveStations(string iniPath, StationPlcConfig[] cs)
    {
        IniFile.Write(iniPath, "setting", "PLCuse",
            string.Join(",", cs.Select(c => c.Enabled ? "1" : "0")));
        for (int i = 0; i < 8 && i < cs.Length; i++)
            IniFile.Write(iniPath, "setting", IpKeys[i], (cs[i].Ip ?? "").Trim());
        IniFile.Write(iniPath, "setting", "PLCPorts",
            string.Join(",", cs.Select(c => c.Port.ToString())));
        IniFile.Write(iniPath, "setting", "PLCAscii",
            string.Join(",", cs.Select(c => c.UseAscii ? "1" : "0")));
    }
}
