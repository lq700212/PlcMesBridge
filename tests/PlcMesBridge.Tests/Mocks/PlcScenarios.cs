// =========================================================================
// PlcScenarios：可复用的 PLC/报文 mock 数据（一处改，全用例生效）
//
// 两类数据：
// ① 单机固化收料：库位 D30040 + 160 产品地址 D3xxxx + 固化秒 D30005 +
//    进入时间 D30032（含 SeedSingleLoad 一键预置，开箱即扫）。
// ② 多机网关：6 个接口的标准 JSON（字段按 MesModels 对齐 W68 文档），
//    拿来就能 PresetString 进 R 区触发全链路。
//
// 产品地址算法（老项目原样，勿改）：i=1..160 → "D3" + (i*40+10).ToString("D4")。
// =========================================================================

using Newtonsoft.Json;
using PlcMesBridge.Core.Comms;
using PlcMesBridge.Core.Mes;

namespace PlcMesBridge.Tests.Mocks;

public static class PlcScenarios
{
    /// <summary>第 i 个产品地址（1..160），老项目算法原样。</summary>
    public static string ProductAddr(int i) => "D3" + (i * 40 + 10).ToString("D4");

    /// <summary>
    /// 单机装载一键预置：库位 + 产品串 + 固化秒 + 进入时间（当前时间）。
    /// products 为空表示"空库位读数"场景（全空，由 HandleLoad 逐个读空串）。
    /// </summary>
    public static void SeedSingleLoad(
        SimulatedPlcClient sim, string binId, int curingSeconds = 300,
        params string[] products)
    {
        sim.PresetString("D30040", binId);
        for (int i = 0; i < products.Length && i < 160; i++)
            sim.PresetString(ProductAddr(i + 1), products[i]);
        sim.PresetInt32("D30005", new[] { curingSeconds });
        var t = DateTime.Now;
        sim.PresetWords("D30032", new short[]
        {
            (short)t.Year, (short)t.Month, (short)t.Day,
            (short)t.Hour, (short)t.Minute, (short)t.Second,
        });
    }

    /// <summary>160 个产品全预置（扫全表性能/统计口径用），idFactory(i)→产品ID。</summary>
    public static void SeedAllProducts(SimulatedPlcClient sim, Func<int, string> idFactory)
    {
        for (int i = 1; i <= 160; i++)
            sim.PresetString(ProductAddr(i), idFactory(i));
    }

    // ================= 网关 6 接口标准 JSON =================

    public static string Api0027Json(string rc = "RC1", string praValue = "300") =>
        JsonConvert.SerializeObject(new[]
        {
            new MesModels.Api0027Request
            {
                RC_NO = rc, PT_NO = "PT1", MAC_NO = "M1", MAC_LOC_NO = "L1",
                PRA_CODE = "CURE", PRA_Value = praValue,
                Collect_Datetime = "2026-09-21 10:00:00.000", PLANT = "P1",
            },
        });

    public static string Api0028Json(string rc = "RC1") =>
        JsonConvert.SerializeObject(new[]
        {
            new MesModels.Api0028Request
            {
                RC_NO = rc, PT_NO = "PT1", MAC_NO = "M1", MAC_LOC_NO = "L1",
                PRA_CODE = "DOWN", PRA_Value = "1", PLANT = "P1",
            },
        });

    public static string Api0030Json(string action = "I", string bc = "B1;") =>
        JsonConvert.SerializeObject(new MesModels.Api0030Request
        {
            RC_NO = "R1", ACTION = action, BC_NO = bc, PT_NO = "PT1",
            SP_MEMO = "SP1", MAC_NO = "M1", MAC_LOC_NO = "L1", TL_EL_NO = "T1",
            IN_DATETIME = "2026-09-21 10:00:00.000", CHECK_RESULT = "0", PLANT = "P1",
        });

    public static string Api0031Json(string bc = "B1") =>
        JsonConvert.SerializeObject(new MesModels.Api0031Request
        {
            RC_NO = "R1", BC_NO = bc, PT_NO = "PT1", EMP_NO = "E1",
            MAC_NO = "M1", MAC_LOC_NO = "L1", BO_NUMBER = "BO1", PLANT = "P1",
        });

    public static string Api0032Json(string bc = "B1") =>
        JsonConvert.SerializeObject(new MesModels.Api0032Request
        {
            RC_NO = "R1", BC_NO = bc, PT_NO = "PT1", EMP_NO = "E1",
            MAC_NO = "M1", MAC_LOC_NO = "L1",
            IN_DATETIME = "2026-09-21 10:00:00.000", PLANT = "P1",
        });

    public static string Api0033Json(string sn = "SN1") =>
        JsonConvert.SerializeObject(new[]
        {
            new MesModels.Api0033Request
            {
                RC_NO = "R1", BOARD_SN = sn, TOP_BTM = "TOP", LINE_NO = "L1",
                PT_NO = "PT1", S_TEST_TIME = "2026-09-21 10:00:00",
                E_TEST_TIME = "2026-09-21 10:01:00", CHK_STATUS = "1",
                DOT_LOC = "D1", BIN_LOC = "B1", IMG_LOC = "I1",
                ERR_NO = "E0", SCR_ACTUAL = "5", SCR_STD = "5", SCR_MAX = "6",
            },
        });

    // ================= MES 应答 =================

    /// <summary>成功应答（DATA 可为 JSON 数组串或 null）。</summary>
    public static string OkResponse(string msg = "ok", string dataJson = "null") =>
        "{\"STATUS\":\"1\",\"MSG\":\"" + msg + "\",\"DATA\":" + dataJson + "}";

    /// <summary>拒绝应答（STATUS=0）。</summary>
    public static string NgResponse(string msg = "no such barcode") =>
        "{\"STATUS\":\"0\",\"MSG\":\"" + msg + "\",\"DATA\":null}";

    /// <summary>0030 的 DATA 三段（OUT_RESULT/REMARK/NEXTOP）。</summary>
    public static string Data30(string result = "PASS", string remark = "rm", string next = "next") =>
        "[{\"OUT_RESULT\":\"" + result + "\",\"OUT_REMARK\":\"" + remark +
        "\",\"OUT_NEXTOP\":\"" + next + "\"}]";

    /// <summary>0031 的 DATA 三段（BC_NO/CHECK_RESULT/REMARK）。</summary>
    public static string Data31(string bc = "B1", string check = "1", string remark = "rm") =>
        "[{\"BC_NO\":\"" + bc + "\",\"CHECK_RESULT\":\"" + check +
        "\",\"REMARK\":\"" + remark + "\"}]";
}
