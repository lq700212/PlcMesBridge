// =========================================================================
// 自定义地址测试：证明业务真的吃配置（不止缺省地址能跑）
//
// 方法：构造一套全改过的地址（D500xx + 产品 D5/100/10×3），
// 在模拟器上预置对应地址，跑 TickScan，全链路断言。
// 若将来有人把某处地址写回硬编码，本文件立刻飘红。
// =========================================================================

using PlcMesBridge.Core.Comms;
using PlcMesBridge.Core.Data;
using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Core.Modes;
using PlcMesBridge.Tests.Mocks;

namespace PlcMesBridge.Tests;

public class CoordinatorCustomAddrTests : IDisposable
{
    private readonly TestScope _scope;
    private readonly SimulatedPlcClient _plc;
    private readonly SingleMachineCoordinator _co;
    private readonly SinglePlcConfig _cfg;

    public CoordinatorCustomAddrTests()
    {
        _scope = new TestScope();
        var db = new SqliteHelper(_scope.DbPath());
        db.EnsureDatabase();
        _cfg = new SinglePlcConfig
        {
            Ip = "127.0.0.1",
            Port = 6060,
            Cmd = "D50001",
            Done = "D50011",
            Live = "D50010",
            Curing = "D50005",
            EntryTime = "D50032",
            PcTime = "D50014",
            BinId = "D50040",
            BinLen = 10,
            ProdPrefix = "D5",
            ProdStart = 100,
            ProdStep = 10,
            ProdCount = 3,
            ProdReadLen = 20,
            ProdWriteLen = 40,
            Alarm = "D50013",
        };
        _plc = new SimulatedPlcClient();
        _plc.Connect("127.0.0.1", 6060);
        _co = new SingleMachineCoordinator(_plc, new CureRecordStore(db), _cfg);
    }

    public void Dispose()
    {
        _co.Dispose();
        _plc.Dispose();
        _scope.Dispose();
    }

    [Fact(DisplayName = "自定义命令装载全链路")]
    public void CustomCmd_Load_FullLoop()
    {
        IReadOnlyList<string>? table = null;
        _co.ProductTable += t => table = t;
        _plc.PresetString("D50040", "KW-5");
        _plc.PresetString(_cfg.ProductAddr(1), "CUST-A");
        _plc.PresetString(_cfg.ProductAddr(2), "CUST-B");
        _plc.PresetWords("D50032", new short[] { 2026, 9, 21, 10, 0, 0 });
        _plc.PresetWord("D50001", 1);

        _co.TickScan();
        Thread.Sleep(300);

        Assert.Equal("KW-5", _co.BinIdText);
        Assert.Equal(0, _plc.PeekWord("D50001")); // 命令清零走新地址
        Assert.Equal(1, _plc.PeekWord("D50011")); // 完成位走新地址
        Assert.NotNull(table);
        Assert.Equal(3, table!.Count); // 产品个数走新配置
        Assert.Equal("CUST-A", table[0]);
        // 老地址纹丝不动（证明没走硬编码）
        Assert.Equal(0, _plc.PeekWord("D30001"));
        Assert.Equal(0, _plc.PeekWord("D30011"));
    }

    [Fact(DisplayName = "自定义心跳与报警位")]
    public void CustomLive_And_Alarm()
    {
        _co.TickLive();
        Assert.Equal(1, _plc.PeekWord("D50010"));
        Assert.Equal(0, _plc.PeekWord("D30010"));

        _co.CheckMesResponse(false, "x", "进站上传");
        Assert.Equal(1, _plc.PeekWord("D50013"));
    }

    [Fact(DisplayName = "构造克隆外部改不动运行中")]
    public void Ctor_Clones_ExternalMut_Ignored()
    {
        _cfg.Cmd = "D59999";
        Assert.Equal("D50001", _co.Cfg.Cmd);
    }
}
