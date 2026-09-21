// =========================================================================
// RunModeSettings：运行模式（单机/多机）切换的纯逻辑（供设置窗 + 测试共用）
//
// 干什么：登录校验 + 模式字符串解析/落盘 + 是否需重启判断。
// 为什么独立成类：WPF 层只做"事件→控件"搬运（项目约定），业务进 Core 可测；
//   模式读写走 ini 文件，纯函数方便用临时 ini 做单元测试，不碰现场 config。
// 怎么改：
//   - 登录账号按需求固定为 admin/123456（本地简易门禁，防误触，不是真安全；
//     如需真权限请接独立账号体系，不要在这里加复杂度）。
//   - 模式缺省 Single（与 AppConfig.Load 一致，老用户升级无感）；
//     大小写/前后空格不敏感，非法值一律回退 Single。
//   - 模式切换重启生效（主窗启动时一次性 InitSingle/InitMulti，不支持热切）。
// =========================================================================

namespace PlcMesBridge.Core.Infrastructure;

public static class RunModeSettings
{
    /// <summary>设置门禁固定用户名（按需求，本地防误触）。</summary>
    public const string AdminUser = "admin";

    /// <summary>设置门禁固定密码（按需求，本地防误触，不是真安全）。</summary>
    public const string AdminPassword = "123456";

    /// <summary>
    /// 登录校验。用户名/密码前后空格忽略，大小写敏感（与老项目密码框语义一致）。
    /// </summary>
    public static bool VerifyLogin(string? user, string? password)
    {
        return string.Equals(user?.Trim(), AdminUser, StringComparison.Ordinal)
            && string.Equals(password ?? "", AdminPassword, StringComparison.Ordinal);
    }

    /// <summary>
    /// 解析配置文本为模式。"Multi"（忽略大小写与空格）→ Multi，其余/空 → Single。
    /// </summary>
    public static RunMode ParseMode(string? text)
    {
        return string.Equals(text?.Trim(), "Multi", StringComparison.OrdinalIgnoreCase)
            ? RunMode.Multi : RunMode.Single;
    }

    /// <summary>模式转配置文本（Single→"Single"，Multi→"Multi"）。</summary>
    public static string ModeToConfigText(RunMode mode) =>
        mode == RunMode.Multi ? "Multi" : "Single";

    /// <summary>
    /// 从 ini 读当前模式（读不到按 IniFile 自愈语义回填 "Single"）。
    /// </summary>
    public static RunMode ReadMode(string iniPath) =>
        ParseMode(IniFile.ReadStr(iniPath, "setting", "Mode", "Single"));

    /// <summary>把模式写入 ini，返回写入的文本（Single/Multi）。</summary>
    public static string SaveMode(string iniPath, RunMode mode)
    {
        string text = ModeToConfigText(mode);
        IniFile.Write(iniPath, "setting", "Mode", text);
        return text;
    }

    /// <summary>
    /// 是否需要重启生效。纯函数（WPF 调用时传 current: AppConfig.Mode）。
    /// </summary>
    public static bool NeedsRestart(RunMode current, RunMode selected) =>
        current != selected;

    // ================= 登录记忆（记住密码） =================
    // 存 config.ini [setting]：RememberLogin=1/0 + SavedUser + SavedPwd。
    // 说明：门禁本身就是本地固定账号（见类头），记忆只是免重复输入，
    //   与 ini 其他现场配置同等保管要求；取消勾选即删 saved 键。

    /// <summary>读登录记忆。缺键回填缺省（不记住、空账号）。</summary>
    public static (bool Remember, string User, string Password) ReadSavedLogin(string iniPath)
    {
        bool remember = IniFile.ReadStr(iniPath, "setting", "RememberLogin", "0") == "1";
        if (!remember)
            return (false, "", "");
        string user = IniFile.ReadStr(iniPath, "setting", "SavedUser", "");
        string pwd = IniFile.ReadStr(iniPath, "setting", "SavedPwd", "");
        return (true, user, pwd);
    }

    /// <summary>
    /// 写登录记忆。勾选记住则存账号密码，否则删 saved 键（只留 RememberLogin=0）。
    /// </summary>
    public static void SaveLogin(string iniPath, bool remember, string? user, string? password)
    {
        if (remember)
        {
            IniFile.Write(iniPath, "setting", "RememberLogin", "1");
            IniFile.Write(iniPath, "setting", "SavedUser", user?.Trim() ?? "");
            IniFile.Write(iniPath, "setting", "SavedPwd", password ?? "");
        }
        else
        {
            IniFile.Write(iniPath, "setting", "RememberLogin", "0");
            IniFile.Delete(iniPath, "setting", "SavedUser");
            IniFile.Delete(iniPath, "setting", "SavedPwd");
        }
    }
}
