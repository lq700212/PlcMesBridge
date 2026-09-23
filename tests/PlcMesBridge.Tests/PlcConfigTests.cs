// =========================================================================
// PLC 配置测试：缺省 heritage + 自愈 + 往返 + 校验 + 报警读链
//
// 锁定的兼容（老现场 ini 无新键也能跑）：
// ① 缺省=老硬编码（IP 192.168.1.80:6060、D30001…、产品 D3/50/40/160）；
// ② 报警位读链：SinglePLC.Alarm → setting.MESPLCALarm → D30013；
// ③ 多机 ASCII 缺省 = 老项目 1/2/5 号机（下标 1,2,5）。
// 全部临时 ini（TestScope），不碰现场 config。
// =========================================================================

using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Tests.Mocks;

namespace PlcMesBridge.Tests;

public class PlcConfigTests
{
    [Fact(DisplayName = "单机缺省等于老硬编码")]
    public void Single_Defaults_MatchHeritage()
    {
        var c = SinglePlcConfig.Default;
        Assert.Equal("192.168.1.80", c.Ip);
        Assert.Equal(6060, c.Port);
        Assert.Equal("D30001", c.Cmd);
        Assert.Equal("D30011", c.Done);
        Assert.Equal("D30010", c.Live);
        Assert.Equal("D30005", c.Curing);
        Assert.Equal("D30032", c.EntryTime);
        Assert.Equal("D30014", c.PcTime);
        Assert.Equal("D30040", c.BinId);
        Assert.Equal(10, c.BinLen);
        Assert.Equal("D30013", c.Alarm);
        // 产品地址与老算法逐字一致：D3+(i*40+10).D4
        Assert.Equal("D30050", c.ProductAddr(1));
        Assert.Equal("D30090", c.ProductAddr(2));
        Assert.Equal("D3" + (160 * 40 + 10).ToString("D4"), c.ProductAddr(160));
    }

    [Fact(DisplayName = "单机缺键自愈缺省")]
    public void Single_Missing_SelfHeals()
    {
        using var scope = new TestScope();
        var c = PlcConfigStore.LoadSingle(scope.IniPath());
        Assert.Equal("D30001", c.Cmd);
        Assert.Equal("192.168.1.80", c.Ip);
        Assert.Equal("D30001", IniFile.ReadStr(scope.IniPath(), "SinglePLC", "Cmd", ""));
    }

    [Fact(DisplayName = "单机存取往返小写转大写")]
    public void Single_SaveLoad_RoundTrip_Normalizes()
    {
        using var scope = new TestScope();
        var c = SinglePlcConfig.Default;
        c.Ip = " 192.168.1.81 ";
        c.Cmd = "d50001";
        PlcConfigStore.SaveSingle(scope.IniPath(), c);
        var r = PlcConfigStore.LoadSingle(scope.IniPath());
        Assert.Equal("192.168.1.81", r.Ip);
        Assert.Equal("D50001", r.Cmd);
    }

    [Fact(DisplayName = "报警位新键优先老键兼容")]
    public void Alarm_NewKey_Wins_OldKey_Fallback()
    {
        using var scope = new TestScope();
        // 老键兼容：只写老键 → 读到老值
        IniFile.Write(scope.IniPath(), "setting", "MESPLCALarm", "D39999");
        Assert.Equal("D39999", PlcConfigStore.LoadSingle(scope.IniPath()).Alarm);
        // 新键优先
        IniFile.Write(scope.IniPath(), "SinglePLC", "Alarm", "D38888");
        Assert.Equal("D38888", PlcConfigStore.LoadSingle(scope.IniPath()).Alarm);
    }

    [Fact(DisplayName = "校验抓坏配置放行好配置")]
    public void ValidateSingle_Bad_Flagged_Good_Passes()
    {
        Assert.Empty(PlcConfigValidator.ValidateSingle(SinglePlcConfig.Default));

        var bad = SinglePlcConfig.Default;
        bad.Ip = "";
        bad.Port = 70000;
        bad.Cmd = "X1";
        bad.Done = "d30011"; // 小写合法（保存时转大写）
        bad.BinLen = 0;
        bad.ProdStep = 0;
        bad.ProdCount = 9999;
        var errs = PlcConfigValidator.ValidateSingle(bad);
        Assert.Contains(errs, e => e.Field == "IP地址");
        Assert.Contains(errs, e => e.Field == "端口");
        Assert.Contains(errs, e => e.Field == "命令字");
        Assert.Contains(errs, e => e.Field == "库位长度");
        Assert.Contains(errs, e => e.Field == "产品步长");
        Assert.Contains(errs, e => e.Field == "产品个数");
        Assert.DoesNotContain(errs, e => e.Field == "完成位");
    }

    [Fact(DisplayName = "地址格式只认DR区")]
    public void IsPlcAddress_OnlyDR()
    {
        Assert.True(PlcConfigValidator.IsPlcAddress("D30001"));
        Assert.True(PlcConfigValidator.IsPlcAddress("r13000"));
        Assert.True(PlcConfigValidator.IsPlcAddress("  D10  "));
        Assert.False(PlcConfigValidator.IsPlcAddress("X1"));
        Assert.False(PlcConfigValidator.IsPlcAddress("Y10"));
        Assert.False(PlcConfigValidator.IsPlcAddress("D"));
        Assert.False(PlcConfigValidator.IsPlcAddress(""));
        Assert.False(PlcConfigValidator.IsPlcAddress(null));
    }

    [Fact(DisplayName = "多机缺省ASCII即1235号机")]
    public void Stations_Defaults_Ascii125()
    {
        using var scope = new TestScope();
        var cs = PlcConfigStore.LoadStations(scope.IniPath());
        Assert.Equal(8, cs.Length);
        Assert.All(cs, c => Assert.Equal(6060, c.Port));
        Assert.All(cs, c => Assert.True(c.Enabled));
        for (int i = 0; i < 8; i++)
            Assert.Equal(i is 1 or 2 or 5, cs[i].UseAscii);
    }

    [Fact(DisplayName = "多机存取往返")]
    public void Stations_SaveLoad_RoundTrip()
    {
        using var scope = new TestScope();
        var cs = PlcConfigStore.LoadStations(scope.IniPath());
        cs[0].Ip = "192.168.1.11";
        cs[0].Port = 6061;
        cs[0].UseAscii = true;
        cs[7].Enabled = false;
        PlcConfigStore.SaveStations(scope.IniPath(), cs);
        var r = PlcConfigStore.LoadStations(scope.IniPath());
        Assert.Equal("192.168.1.11", r[0].Ip);
        Assert.Equal(6061, r[0].Port);
        Assert.True(r[0].UseAscii);
        Assert.False(r[7].Enabled);
        // 跳号键沿用：0 号机写 PLCIP2
        Assert.Equal("192.168.1.11", IniFile.ReadStr(scope.IniPath(), "setting", "PLCIP2", ""));
    }

    [Fact(DisplayName = "多机校验跳过未启用")]
    public void Stations_Validate_Skips_Disabled()
    {
        var cs = new StationPlcConfig[8];
        for (int i = 0; i < 8; i++)
            cs[i] = new StationPlcConfig { Enabled = false };
        Assert.Empty(PlcConfigValidator.ValidateStations(cs));

        cs[3].Enabled = true;
        cs[3].Ip = "bad ip!!";
        cs[3].Port = 0;
        var errs = PlcConfigValidator.ValidateStations(cs);
        Assert.Equal(2, errs.Count);
    }

    [Fact(DisplayName = "AppConfig.Load带出单机与端口协议")]
    public void AppConfig_Load_Pulls_Plc()
    {
        using var scope = new TestScope();
        IniFile.Write(scope.IniPath(), "SinglePLC", "IP", "10.0.0.5");
        IniFile.Write(scope.IniPath(), "setting", "PLCPorts",
            "6060,6061,6060,6060,6060,6060,6060,6060");
        AppConfig.Load(scope.IniPath());
        Assert.Equal("10.0.0.5", AppConfig.Single.Ip);
        Assert.Equal(6061, AppConfig.StationPorts[1]);
        Assert.True(AppConfig.StationAscii[1]);
        Assert.False(AppConfig.StationAscii[0]);
    }
}
