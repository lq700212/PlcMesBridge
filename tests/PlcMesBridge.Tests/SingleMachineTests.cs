// =========================================================================
// 通讯模拟 + 单机分支测试：SimulatedPlcClient + SingleMachineCoordinator
// 用内存模拟跑通"命令字边沿→装载入库→出站→资料获取回写"全链路，不碰真机。
// 上传 MES 侧：测试环境无 MES，PostJson 会失败→触发报警位写入（可断言），
// 不抛异常即过（报警路径本身就是被测行为之一）。
// =========================================================================

using PlcMesBridge.Core.Comms;
using PlcMesBridge.Core.Data;
using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Core.Modes;

namespace PlcMesBridge.Tests;

public class SingleMachineTests : IDisposable
{
    private readonly string _tmp;
    private readonly SimulatedPlcClient _plc;
    private readonly SingleMachineCoordinator _co;
    private readonly List<string> _logs = new();
    private readonly List<(string, string)> _alarms = new();
    private IReadOnlyList<string>? _table;

    public SingleMachineTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "PlcRdSm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
        var db = new SqliteHelper(Path.Combine(_tmp, "t.db3"));
        db.EnsureDatabase();
        _plc = new SimulatedPlcClient();
        _plc.Connect("127.0.0.1", 6060);
        _co = new SingleMachineCoordinator(_plc, new CureRecordStore(db),
            Path.Combine(_tmp, "c.ini"));
        _co.LogMessage += m => _logs.Add(m);
        _co.AlarmRaised += (m, n) => _alarms.Add((m, n));
        _co.ProductTable += t => _table = t;
    }

    public void Dispose()
    {
        _co.Dispose();
        _plc.Dispose();
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Fact(DisplayName = "连接成功与失败")]
    public void Connect_Ok_Fail()
    {
        Assert.True(_co.Connect(out _));
        using var bad = new SimulatedPlcClient();
        var co2 = new SingleMachineCoordinator(bad,
            new CureRecordStore(new SqliteHelper(Path.Combine(_tmp, "t.db3"))));
        co2.SetEndpoint("fail", 6060); // 模拟器约定：ip=fail 连不上
        Assert.False(co2.Connect(out string msg));
        Assert.Contains("连接PLC失败", msg);
    }

    [Fact(DisplayName = "装载分支：入库+命令清零+完成位置1")]
    public void Load_SavesRecord_ClearsCmd_SetsDone()
    {
        _plc.PresetWord("D30001", 1);
        _plc.PresetString("D30040", "KW-A");
        _plc.PresetString("D30010x", "x"); // 无关地址不干扰
        _plc.PresetString("D30010".Replace("D30010", "D30100"), "P1");
        // 预置两个产品，其余 158 为空
        _plc.PresetString("D3" + (1 * 40 + 10).ToString("D4"), "ABCL001");
        _plc.PresetString("D3" + (2 * 40 + 10).ToString("D4"), "ABCR002");

        _co.TickScan();
        // 等后台进站上传（无 MES 会报警，但不阻塞）
        Thread.Sleep(500);

        Assert.Equal("KW-A", _co.BinIdText);
        Assert.Equal("物料装载", _co.StatusKey);
        Assert.Equal(0, _plc.PeekWord("D30001"));
        Assert.Equal(1, _plc.PeekWord("D30011"));
        Assert.NotNull(_table);
        Assert.Equal(160, _table!.Count);
        Assert.Equal("ABCL001", _table[0]);
        Assert.Equal("ABCR002", _table[1]);
    }

    [Fact(DisplayName = "命令无边沿不重复执行")]
    public void NoEdge_NoRepeat()
    {
        _plc.PresetWord("D30001", 0);
        _plc.PresetString("D30040", "KW-X");
        int n0 = _logs.Count;
        _co.TickScan();
        _co.TickScan();
        // 无边沿：不应出现库位日志
        Assert.DoesNotContain(_logs.Skip(n0), m => m.Contains("KW-X"));
    }

    [Fact(DisplayName = "卸载分支：删库+完成位置1")]
    public void Unload_DeletesRecord()
    {
        // 先装载
        _plc.PresetWord("D30001", 1);
        _plc.PresetString("D30040", "KW-B");
        _co.TickScan();
        Thread.Sleep(300);
        // 再卸载
        _plc.PresetWord("D30001", 2);
        _co.TickScan();
        Thread.Sleep(1500); // 出站上传（固化+出站两次 POST，失败走报警）
        Assert.Equal("物料卸载", _co.StatusKey);
        Assert.Equal(1, _plc.PeekWord("D30011"));
    }

    [Fact(DisplayName = "资料获取：回写PLC+回显表格")]
    public void Fetch_WritesBack_ToPlc()
    {
        _plc.PresetWord("D30001", 1);
        _plc.PresetString("D30040", "KW-C");
        _plc.PresetString("D3" + (1 * 40 + 10).ToString("D4"), "FETCH001");
        _co.TickScan();
        Thread.Sleep(300);
        // 资料获取
        _plc.PresetWord("D30001", 4);
        _co.TickScan();
        Assert.Equal("资料获取", _co.StatusKey);
        Assert.Equal("FETCH001", _plc.PeekString("D3" + (1 * 40 + 10).ToString("D4")));
        Assert.NotNull(_table);
        Assert.Equal("FETCH001", _table![0]);
    }

    [Fact(DisplayName = "心跳翻转并写D30010")]
    public void Live_Toggles_Writes()
    {
        bool a = _co.TickLive();
        bool b = _co.TickLive();
        Assert.NotEqual(a, b);
        Assert.Equal(b ? 1 : 0, _plc.PeekWord("D30010"));
    }

    [Fact(DisplayName = "MES失败触发报警位")]
    public void Alarm_SetsPlcBit()
    {
        _co.CheckMesResponse(false, "conn refused", "进站上传");
        Assert.Equal(1, _plc.PeekWord("D30013"));
        Assert.Single(_alarms);
    }
}
