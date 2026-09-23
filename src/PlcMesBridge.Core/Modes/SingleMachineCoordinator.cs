// =========================================================================
// SingleMachineCoordinator：单机固化收料业务（复刻自单机版 Form1.vb 全逻辑）
//
// 干什么：把 Form1 里"Timer 轮询 + 命令字边沿 + 4 分支 + MES 上传 + 统计"
//   从 WinForms 控件里剥出来，UI（WPF）只订阅事件。对 WinForms 逻辑 1:1，
//   唯一改动：PLC 访问经 IPlcClient（真机/模拟可换），日志/弹框走事件。
//
// 轮询节拍（老项目原样）：心跳 1s 翻转（D30010）；业务 200ms 一次；
//   统计每 5 次业务轮询跑一次（约 1s）。
// 命令字边沿触发（old != new 才执行），地址全来自 SinglePlcConfig
// （缺省=老项目硬编码 D30001/D30011/…，PLC配置窗可改，改后重启生效）。
// 4 分支：1 装载（读产品→入库→进站上传）/ 2 卸载（固化+出站上传→删库）/
//   3 强制出料（同 2）/ 4 资料获取（查库回写 PLC + 回显）。
// 产品地址：Cfg.ProductAddr(i)（缺省 D3 前缀 + 50 起 + 40 步长 + D4 补零）；
// MES 报警位来自配置（读链 新键→老 MESPLCALarm→D30013）+ AlarmRaised 事件。
// =========================================================================

using Newtonsoft.Json;
using PlcMesBridge.Core.Comms;
using PlcMesBridge.Core.Data;
using PlcMesBridge.Core.Infrastructure;
using PlcMesBridge.Core.Mes;

namespace PlcMesBridge.Core.Modes;

public class SingleMachineCoordinator : IDisposable
{
    private readonly IPlcClient _plc;
    private readonly CureRecordStore _store;
    private readonly SinglePlcConfig _cfg;
    private short _lastCmd;
    private short _live;
    private int _statsCounter;
    private bool _disposed;

    /// <summary>PLC 配置（构造注入；缺省=老项目硬编码，见 PlcConfig.cs）。</summary>
    public SinglePlcConfig Cfg => _cfg;

    public string Ip => _cfg.Ip;
    public int Port => _cfg.Port;

    // ---- UI 状态缓存（老项目 currentXXX 变量，供中英切换重绘）----
    public string CuringText { get; private set; } = "";
    public string InTimeText { get; private set; } = "";
    public string BinIdText { get; private set; } = "";
    public string StatusKey { get; private set; } = "物料状态";
    public CureStats Stats { get; private set; } = new();
    public int LastCuringSeconds { get; private set; }

    /// <summary>操作日志（UI 追加到日志列表 + 写 log 文件由调用方决定，此处只发事件）。</summary>
    public event Action<string>? LogMessage;
    /// <summary>状态/统计/产品表变化（UI 刷新对应控件）。</summary>
    public event Action? UiChanged;
    /// <summary>产品表整表刷新（160 行产品 ID，UI 重填表格）。</summary>
    public event Action<IReadOnlyList<string>>? ProductTable;
    /// <summary>MES 报警（UI 弹报警窗 + 写 PLC 报警位已在本类完成）。</summary>
    public event Action<string, string>? AlarmRaised;

    public SingleMachineCoordinator(IPlcClient plc, CureRecordStore store, string? iniPath = null)
        : this(plc, store, PlcConfigStore.LoadSingle(iniPath ?? AppPaths.ConfigIni))
    {
    }

    /// <summary>配置注入构造（配置窗/测试用；传进来的对象会被克隆，外部改不影响运行中）。</summary>
    public SingleMachineCoordinator(IPlcClient plc, CureRecordStore store, SinglePlcConfig cfg)
    {
        _plc = plc;
        _store = store;
        _cfg = (cfg ?? SinglePlcConfig.Default).Clone();
    }

    /// <summary>连接（老项目 Button1_Click；成功调用方起节拍器，失败 UI 弹框）。</summary>
    public bool Connect(out string message)
    {
        _plc.Disconnect();
        if (_plc.Connect(Ip, Port))
        {
            message = $"{LanguageService.Tr("连接PLC成功")},{Ip}";
            LogMessage?.Invoke(message);
            return true;
        }
        message = $"{LanguageService.Tr("连接PLC失败")},{Ip}";
        LogMessage?.Invoke(message);
        return false;
    }

    public void SetEndpoint(string ip, int port)
    {
        _cfg.Ip = ip;
        _cfg.Port = port;
    }

    /// <summary>心跳 1s（老项目 TimerLive_Tick：灯翻转 + 心跳地址）。返回灯状态供 UI。</summary>
    public bool TickLive()
    {
        _live = _live == 0 ? (short)1 : (short)0;
        _plc.WriteInt16(_cfg.Live, _live);
        return _live == 1;
    }

    /// <summary>业务轮询一次（老项目 TimerScan_Tick 全量，200ms 调一次）。</summary>
    public void TickScan()
    {
        var (ghOk, gh) = _plc.ReadInt32(_cfg.Curing, 2);
        if (ghOk && gh != null && gh.Length > 0)
        {
            LastCuringSeconds = gh[0];
            CuringText = $"{gh[0]}s";
        }

        var (tOk, tm) = _plc.ReadInt16(_cfg.EntryTime, 6);
        if (tOk && tm != null && tm.Length >= 6)
        {
            InTimeText = $"{tm[0]}-{tm[1]:D2}-{tm[2]:D2} {tm[3]:D2}:{tm[4]:D2}:{tm[5]:D2}";
        }

        var (cOk, cmdArr) = _plc.ReadInt16(_cfg.Cmd, 1);
        short cmd = cOk && cmdArr != null && cmdArr.Length > 0 ? cmdArr[0] : _lastCmd;

        if (cOk && _lastCmd != cmd)
        {
            switch (cmd)
            {
                case 1: HandleLoad(); break;
                case 2: HandleUnload(); break;
                case 3: HandleForceOut(); break;
                case 4: HandleFetch(); break;
            }
        }
        _lastCmd = cmd;

        UiChanged?.Invoke();

        _statsCounter++;
        if (_statsCounter >= 5)
        {
            _statsCounter = 0;
            RefreshStats();
        }
    }

    /// <summary>分支1 物料装载（老项目 Case 1 原样）。</summary>
    internal void HandleLoad()
    {
        StatusKey = "物料装载";
        _plc.WriteInt16(_cfg.Cmd, 0);
        var now = DateTime.Now;
        _plc.WriteInt16(_cfg.PcTime, new short[]
            { (short)now.Year, (short)now.Month, (short)now.Day,
              (short)now.Hour, (short)now.Minute, (short)now.Second });

        string kw = ReadBinId();
        BinIdText = kw;
        LogMessage?.Invoke($"{LanguageService.Tr("库位ID: ")}{kw}");

        if (!string.IsNullOrEmpty(kw))
        {
            var ps = new List<string>(_cfg.ProdCount);
            for (int i = 1; i <= _cfg.ProdCount; i++)
            {
                string addr = _cfg.ProductAddr(i);
                var (ok, s) = _plc.ReadString(addr, (ushort)_cfg.ProdReadLen);
                string ret = ok ? (s ?? string.Empty).Replace("\0", string.Empty) : string.Empty;
                ps.Add(ret);
            }
            ProductTable?.Invoke(ps);
            _store.SaveRecord(kw, string.Join("⚫", ps), "---");
            LogMessage?.Invoke($"{LanguageService.Tr("物料装载")}," +
                $"{LanguageService.Tr("库位ID=")}{kw}," +
                $"{LanguageService.Tr("产品ID=")}{string.Join("⚫", ps)}");
            UploadBoardIn(ps);
        }
        else
        {
            LogMessage?.Invoke($"{LanguageService.Tr("物料装载")},{LanguageService.Tr("库位ID为空")}");
        }
        _plc.WriteInt16(_cfg.Done, 1);
    }

    /// <summary>分支2 物料卸载（老项目 Case 2 原样：先查库上传，再删库）。</summary>
    internal void HandleUnload()
    {
        StatusKey = "物料卸载";
        LogMessage?.Invoke(LanguageService.Tr("物料卸载"));
        _plc.WriteInt16(_cfg.Cmd, 0);
        string kw = ReadBinId();
        BinIdText = kw;
        LogMessage?.Invoke($"{LanguageService.Tr("库位ID: ")}{kw}");
        if (!string.IsNullOrEmpty(kw))
        {
            UploadBoardOutAndCuring(kw, LastCuringSeconds);
            _store.DeleteRecord(kw);
        }
        else
        {
            LogMessage?.Invoke($"{LanguageService.Tr("物料卸载")},{LanguageService.Tr("库位ID为空")}");
        }
        _plc.WriteInt16(_cfg.Done, 1);
    }

    /// <summary>分支3 强制出料（老项目 Case 3：同卸载亦上报）。</summary>
    internal void HandleForceOut()
    {
        StatusKey = "强制出料";
        LogMessage?.Invoke(LanguageService.Tr("强制出料"));
        _plc.WriteInt16(_cfg.Cmd, 0);
        string kw = ReadBinId();
        BinIdText = kw;
        LogMessage?.Invoke($"{LanguageService.Tr("库位ID: ")}{kw}");
        if (!string.IsNullOrEmpty(kw))
        {
            UploadBoardOutAndCuring(kw, LastCuringSeconds);
            _store.DeleteRecord(kw);
        }
        else
        {
            LogMessage?.Invoke($"{LanguageService.Tr("强制出料")},{LanguageService.Tr("库位ID为空")}");
        }
        _plc.WriteInt16(_cfg.Done, 1);
    }

    /// <summary>分支4 资料获取（老项目 Case 4：查库→逐条回写 PLC→回显表格）。</summary>
    internal void HandleFetch()
    {
        StatusKey = "资料获取";
        _plc.WriteInt16(_cfg.Cmd, 0);
        string kw = ReadBinId();
        BinIdText = kw;
        LogMessage?.Invoke($"{LanguageService.Tr("库位ID: ")}{kw}");
        if (!string.IsNullOrEmpty(kw))
        {
            var row = _store.GetByBinId(kw);
            if (row != null)
            {
                string pid = row["产品ID"]?.ToString() ?? string.Empty;
                LogMessage?.Invoke($"{LanguageService.Tr("资料获取")}," +
                    $"{LanguageService.Tr("库位ID=")}{kw},{LanguageService.Tr("产品ID=")}{pid}");
                string[] ps = pid.Split('⚫');
                var echo = new List<string>(_cfg.ProdCount);
                for (int i = 1; i <= _cfg.ProdCount; i++)
                {
                    string addr = _cfg.ProductAddr(i);
                    if (i <= ps.Length)
                    {
                        _plc.WriteString(addr, ps[i - 1], _cfg.ProdWriteLen);
                        echo.Add(ps[i - 1]);
                    }
                    else
                    {
                        echo.Add(string.Empty);
                    }
                }
                ProductTable?.Invoke(echo);
            }
            else
            {
                LogMessage?.Invoke($"{LanguageService.Tr("资料获取")}," +
                    $"{LanguageService.Tr("库位ID=")}{kw},{LanguageService.Tr("未查询到相应数据")}");
            }
        }
        else
        {
            LogMessage?.Invoke($"{LanguageService.Tr("资料获取")},{LanguageService.Tr("库位ID为空")}");
        }
        _plc.WriteInt16(_cfg.Done, 1);
    }

    private string ReadBinId()
    {
        var (ok, s) = _plc.ReadString(_cfg.BinId, (ushort)_cfg.BinLen);
        return ok ? (s ?? string.Empty).Replace("\0", string.Empty) : string.Empty;
    }

    /// <summary>统计刷新（老项目 UpdateStats：DB 搬运 + ComputeStats 纯算）。</summary>
    public void RefreshStats()
    {
        try
        {
            var dt = _store.LoadStatsRows();
            var rows = dt.Rows.Cast<System.Data.DataRow>()
                .Select(r => (r["Time"]?.ToString() ?? string.Empty,
                              r["产品ID"]?.ToString() ?? string.Empty));
            Stats = CureRecordStore.ComputeStats(rows, LastCuringSeconds, DateTime.Now);
            UiChanged?.Invoke();
        }
        catch
        {
            // 防 DB 偶然锁定崩程序（老项目空 catch 原注）。
        }
    }

    // ---- MES 上传（老项目 UploadMES_BoardIn/BoardOutAndCuring/CheckMESResponse）----

    /// <summary>进站上传 API0030 ACTION=I（老项目后台 Task.Run，此处调用方已在后台线程，直接同步调）。</summary>
    internal void UploadBoardIn(List<string> productIds)
    {
        Task.Run(() =>
        {
            try
            {
                var req = new MesModels.Api0030Request
                {
                    RC_NO = MesParamStore.RC_NO,
                    ACTION = "I",
                    BC_NO = string.Join(";", productIds.Where(p => !string.IsNullOrEmpty(p))) + ";",
                    PT_NO = MesParamStore.PT_NO,
                    MAC_NO = MesParamStore.MAC_NO,
                    MAC_LOC_NO = MesParamStore.MAC_LOC_NO,
                    IN_DATETIME = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    CHECK_RESULT = MesParamStore.CHECK_RESULT,
                    PLANT = MesParamStore.PLANT,
                };
                string json = JsonConvert.SerializeObject(req);
                bool ok = MesHttpClient.PostJson(MesConfig.UrlApi0030, json, out string resp);
                CheckMesResponse(ok, resp, "进站上传");
            }
            catch (Exception ex)
            {
                TriggerPlcAlarm("进站上传异常: " + ex.Message);
            }
        });
    }

    /// <summary>固化上传 API0027（数组包一层）+ 出站上传 API0030 ACTION=O（老项目前台同步，此处同线程）。</summary>
    internal void UploadBoardOutAndCuring(string kw, int curingTime)
    {
        try
        {
            var row = _store.GetByBinId(kw);
            string productIds = "";
            if (row != null)
                productIds = (row["产品ID"]?.ToString() ?? string.Empty).Replace("⚫", ";") + ";";

            var param = new MesModels.Api0027Request
            {
                RC_NO = MesParamStore.RC_NO,
                PT_NO = MesParamStore.PT_NO,
                MAC_NO = MesParamStore.MAC_NO,
                MAC_LOC_NO = MesParamStore.MAC_LOC_NO,
                PRA_CODE = MesParamStore.PRA_CODE,
                PRA_Value = curingTime.ToString(),
                Collect_Datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                PLANT = MesParamStore.PLANT,
            };
            string jsonParam = JsonConvert.SerializeObject(new List<MesModels.Api0027Request> { param });
            bool ok1 = MesHttpClient.PostJson(MesConfig.UrlApi0027, jsonParam, out string resp1);
            CheckMesResponse(ok1, resp1, "固化时间上传");

            var outReq = new MesModels.Api0030Request
            {
                RC_NO = MesParamStore.RC_NO,
                ACTION = "O",
                BC_NO = productIds,
                PT_NO = MesParamStore.PT_NO,
                MAC_NO = MesParamStore.MAC_NO,
                MAC_LOC_NO = MesParamStore.MAC_LOC_NO,
                IN_DATETIME = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                CHECK_RESULT = MesParamStore.CHECK_RESULT,
                PLANT = MesParamStore.PLANT,
            };
            string jsonOut = JsonConvert.SerializeObject(outReq);
            bool ok2 = MesHttpClient.PostJson(MesConfig.UrlApi0030, jsonOut, out string resp2);
            CheckMesResponse(ok2, resp2, "出站上传");
        }
        catch (Exception ex)
        {
            TriggerPlcAlarm("出库上传异常: " + ex.Message);
        }
    }

    /// <summary>检查 MES 返回（STATUS != "1" 即报警，老项目 CheckMESResponse 原样）。</summary>
    internal void CheckMesResponse(bool success, string respMsg, string actionName)
    {
        if (!success)
        {
            TriggerPlcAlarm($"{actionName}网络请求失败: {respMsg}");
            return;
        }
        try
        {
            var resp = JsonConvert.DeserializeObject<MesModels.BaseResponse>(respMsg);
            if (resp != null && resp.STATUS != "1")
                TriggerPlcAlarm($"{actionName}被MES拒绝: {resp.MSG}");
        }
        catch
        {
            TriggerPlcAlarm($"{actionName}报文解析失败: {respMsg}");
        }
    }

    /// <summary>
    /// 报警：写 PLC 报警位 + 事件（UI 弹窗，老项目 TriggerPLCAlarm 原样）。
    /// 地址来自配置（读链 新键→老 MESPLCALarm→D30013，见 PlcConfigStore）。
    /// </summary>
    internal void TriggerPlcAlarm(string errMsg)
    {
        try
        {
            _plc.WriteInt16(_cfg.Alarm, 1);
            AlarmRaised?.Invoke(errMsg, "MES Error");
            LogMessage?.Invoke($"MES 报警触发: {errMsg}");
        }
        catch
        {
            // 报警路径自身异常吞掉（老项目空 catch 原注）。
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
    }
}
