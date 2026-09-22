// =========================================================================
// 单机边界测试：空库位/查无数据/强制出料/读失败保活/桩 MES 成功链
//
// 说明：
// ① MES 上传全部指本地桩（MesStubServer），不再靠"连不上→报警"的偶然行为；
// ② 无桩时（默认 URL 不可达）上传失败走报警是 heritage 行为，本文件只锁
//    "失败不抛、置报警位"，成功链用桩精确断言报文；
// ③ StatusKey 存中文键、UI 层 Tr 翻译——此处锁定"键保持中文"，防有人顺手改英文。
// =========================================================================

using PlcMesBridge.Core.Comms;
using PlcMesBridge.Core.Data;
using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Core.Mes;
using PlcMesBridge.Core.Modes;
using PlcMesBridge.Tests.Mocks;

namespace PlcMesBridge.Tests;

public class SingleMachineEdgeTests : IDisposable
{
    private readonly TestScope _scope;
    private readonly SimulatedPlcClient _plc;
    private readonly SingleMachineCoordinator _co;
    private readonly CureRecordStore _store;
    private readonly List<string> _logs = new();
    private readonly List<(string, string)> _alarms = new();

    public SingleMachineEdgeTests()
    {
        _scope = new TestScope();
        var db = new SqliteHelper(_scope.DbPath());
        db.EnsureDatabase();
        _store = new CureRecordStore(db);
        _plc = new SimulatedPlcClient();
        _plc.Connect("127.0.0.1", 6060);
        _co = new SingleMachineCoordinator(_plc, _store, _scope.IniPath());
        _co.LogMessage += m => _logs.Add(m);
        _co.AlarmRaised += (m, n) => _alarms.Add((m, n));
    }

    public void Dispose()
    {
        _co.Dispose();
        _plc.Dispose();
        _scope.Dispose();
    }

    [Fact(DisplayName = "空库位装载：不入库+完成位置1+记空日志")]
    public void Load_EmptyBin_NoRecord_DoneBit_LogEmpty()
    {
        PlcScenarios.SeedSingleLoad(_plc, "");
        _plc.PresetWord("D30001", 1);
        _co.TickScan();
        Thread.Sleep(300);

        Assert.Equal(1, _plc.PeekWord("D30011"));
        Assert.Equal(0, _plc.PeekWord("D30001"));
        Assert.Contains(_logs, m => m.Contains("库位ID为空"));
        Assert.Null(_store.GetByBinId(""));
    }

    [Fact(DisplayName = "资料获取查无数据：记未查询+完成位置1")]
    public void Fetch_Miss_LogsNotFound_DoneBit()
    {
        PlcScenarios.SeedSingleLoad(_plc, "KW-MISS");
        _plc.PresetWord("D30001", 4);
        _co.TickScan();

        Assert.Equal("资料获取", _co.StatusKey);
        Assert.Equal(1, _plc.PeekWord("D30011"));
        Assert.Contains(_logs, m => m.Contains("未查询到相应数据"));
    }

    [Fact(DisplayName = "强制出料：删库+完成位置1")]
    public void ForceOut_DeletesRecord()
    {
        using var mes = new MesStubServer();
        MesConfig.UrlApi0027 = mes.Url;
        MesConfig.UrlApi0030 = mes.Url;
        _store.SaveRecord("KW-F", "ABCL001", "---");

        PlcScenarios.SeedSingleLoad(_plc, "KW-F");
        _plc.PresetWord("D30001", 3);
        _co.TickScan();
        mes.WaitForRequests(2); // 固化 + 出站两次 POST
        Thread.Sleep(300);

        Assert.Equal("强制出料", _co.StatusKey);
        Assert.Null(_store.GetByBinId("KW-F"));
        Assert.Equal(1, _plc.PeekWord("D30011"));
        Assert.Empty(_alarms); // 桩回成功，不应报警
        Assert.Contains("PRA_CODE", mes.Bodies[0]);
        Assert.Contains("\"ACTION\":\"O\"", mes.Bodies[1]);
    }

    [Fact(DisplayName = "进站上传成功：报文ACTION=I+无报警")]
    public void Load_WithStub_NoAlarm_ActionI()
    {
        using var mes = new MesStubServer();
        MesConfig.UrlApi0030 = mes.Url;
        PlcScenarios.SeedSingleLoad(_plc, "KW-S", 300, "ABCL001", "ABCR002");
        _plc.PresetWord("D30001", 1);
        _co.TickScan();
        mes.WaitForRequests(1);
        Thread.Sleep(500); // 等后台 CheckMesResponse 跑完

        Assert.Empty(_alarms);
        Assert.Contains("\"ACTION\":\"I\"", mes.Bodies[0]);
        Assert.Contains("ABCL001;ABCR002;", mes.Bodies[0]);
    }

    [Fact(DisplayName = "PLC读失败：保上次命令不误触发")]
    public void TickScan_ReadFail_KeepsLastCmd()
    {
        PlcScenarios.SeedSingleLoad(_plc, "KW-K");
        _plc.PresetWord("D30001", 1);
        _co.TickScan();
        Thread.Sleep(300);
        int n0 = _logs.Count;

        _plc.Disconnect(); // 模拟断线：所有读返回失败
        var ex = Record.Exception(() => { _co.TickScan(); _co.TickScan(); });
        Assert.Null(ex);
        Assert.Equal(n0, _logs.Count); // 无新分支日志
        _plc.Connect("127.0.0.1", 6060);
    }

    [Fact(DisplayName = "固化与进入时间文本格式")]
    public void TickScan_Formats_Curing_And_Time()
    {
        PlcScenarios.SeedSingleLoad(_plc, "KW-T", 300);
        _plc.PresetWords("D30032", new short[] { 2026, 9, 21, 10, 5, 6 });
        _plc.PresetWord("D30001", 0);
        _co.TickScan();

        Assert.Equal("300s", _co.CuringText);
        Assert.Equal("2026-09-21 10:05:06", _co.InTimeText);
    }

    [Fact(DisplayName = "MES拒绝：报警+报警位")]
    public void CheckMesResponse_Reject_Alarms_Bit()
    {
        _co.CheckMesResponse(true, PlcScenarios.NgResponse("bad bc"), "出站上传");
        Assert.Single(_alarms);
        Assert.Contains("bad bc", _alarms[0].Item1);
        Assert.Equal(1, _plc.PeekWord("D30013"));
    }

    [Fact(DisplayName = "MES报文坏了：报警不抛")]
    public void CheckMesResponse_BadJson_Alarms_NoThrow()
    {
        var ex = Record.Exception(
            () => _co.CheckMesResponse(true, "{not json", "固化时间上传"));
        Assert.Null(ex);
        Assert.Single(_alarms);
    }

    [Fact(DisplayName = "StatusKey保持中文键")]
    public void StatusKey_StaysChineseKey()
    {
        LanguageService.CurrentLanguage = "EN";
        PlcScenarios.SeedSingleLoad(_plc, "KW-E");
        _plc.PresetWord("D30001", 1);
        _co.TickScan();
        Thread.Sleep(300);
        Assert.Equal("物料装载", _co.StatusKey); // 翻译是 UI 层 Tr 的事
        Assert.Equal("Material Loading", LanguageService.Tr(_co.StatusKey));
        LanguageService.CurrentLanguage = "CH";
    }
}
