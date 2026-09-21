// =========================================================================
// 运行模式设置测试：登录门禁 + 模式解析/落盘 + 重启判断。
// 全部用临时 ini，不碰现场 config；不调 AppConfig.Load（它会连带读参数文件）。
// =========================================================================

using PlcMesBridge.Core.Infrastructure;

namespace PlcMesBridge.Tests;

public class RunModeSettingsTests : IDisposable
{
    private readonly string _tmp;

    public RunModeSettingsTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "PlcRdMode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Fact(DisplayName = "登录正确账号通过")]
    public void Login_Correct_Passes()
    {
        Assert.True(RunModeSettings.VerifyLogin("admin", "123456"));
    }

    [Fact(DisplayName = "登录错误账号拒绝")]
    public void Login_Wrong_Rejects()
    {
        Assert.False(RunModeSettings.VerifyLogin("admin", "wrong"));
        Assert.False(RunModeSettings.VerifyLogin("user", "123456"));
        Assert.False(RunModeSettings.VerifyLogin("", ""));
        Assert.False(RunModeSettings.VerifyLogin(null, null));
        // 用户名大小写敏感：ADMIN 不是 admin
        Assert.False(RunModeSettings.VerifyLogin("ADMIN", "123456"));
    }

    [Fact(DisplayName = "模式解析大小写空格不敏感非法回退单机")]
    public void ParseMode_Tolerant_InvalidFallsBackSingle()
    {
        Assert.Equal(RunMode.Multi, RunModeSettings.ParseMode("Multi"));
        Assert.Equal(RunMode.Multi, RunModeSettings.ParseMode("multi"));
        Assert.Equal(RunMode.Multi, RunModeSettings.ParseMode("  Multi  "));
        Assert.Equal(RunMode.Single, RunModeSettings.ParseMode("Single"));
        Assert.Equal(RunMode.Single, RunModeSettings.ParseMode("xxx"));
        Assert.Equal(RunMode.Single, RunModeSettings.ParseMode(""));
        Assert.Equal(RunMode.Single, RunModeSettings.ParseMode(null));
    }

    [Fact(DisplayName = "模式写入读出往返")]
    public void SaveRead_RoundTrip()
    {
        string ini = Path.Combine(_tmp, "c.ini");
        Assert.Equal("Multi", RunModeSettings.SaveMode(ini, RunMode.Multi));
        Assert.Equal(RunMode.Multi, RunModeSettings.ReadMode(ini));
        Assert.Equal("Single", RunModeSettings.SaveMode(ini, RunMode.Single));
        Assert.Equal(RunMode.Single, RunModeSettings.ReadMode(ini));
    }

    [Fact(DisplayName = "缺键读模式回填单机")]
    public void ReadMode_Missing_SelfHealsSingle()
    {
        string ini = Path.Combine(_tmp, "c.ini");
        Assert.Equal(RunMode.Single, RunModeSettings.ReadMode(ini));
        // 自愈：文件里已补上缺省
        Assert.Equal("Single", IniFile.ReadStr(ini, "setting", "Mode", "Multi"));
    }

    [Fact(DisplayName = "模式变化才需重启")]
    public void NeedsRestart_OnlyOnChange()
    {
        Assert.False(RunModeSettings.NeedsRestart(RunMode.Single, RunMode.Single));
        Assert.False(RunModeSettings.NeedsRestart(RunMode.Multi, RunMode.Multi));
        Assert.True(RunModeSettings.NeedsRestart(RunMode.Single, RunMode.Multi));
        Assert.True(RunModeSettings.NeedsRestart(RunMode.Multi, RunMode.Single));
    }

    [Fact(DisplayName = "登录记忆缺键默认不记住")]
    public void SavedLogin_Missing_DefaultsNotRemember()
    {
        string ini = Path.Combine(_tmp, "c.ini");
        var (remember, user, pwd) = RunModeSettings.ReadSavedLogin(ini);
        Assert.False(remember);
        Assert.Equal("", user);
        Assert.Equal("", pwd);
    }

    [Fact(DisplayName = "登录记忆勾选存取往返")]
    public void SavedLogin_Checked_RoundTrip()
    {
        string ini = Path.Combine(_tmp, "c.ini");
        RunModeSettings.SaveLogin(ini, true, "admin", "123456");
        var (remember, user, pwd) = RunModeSettings.ReadSavedLogin(ini);
        Assert.True(remember);
        Assert.Equal("admin", user);
        Assert.Equal("123456", pwd);
    }

    [Fact(DisplayName = "登录记忆取消勾选清账号")]
    public void SavedLogin_Unchecked_ClearsSaved()
    {
        string ini = Path.Combine(_tmp, "c.ini");
        RunModeSettings.SaveLogin(ini, true, "admin", "123456");
        RunModeSettings.SaveLogin(ini, false, "admin", "123456");
        var (remember, user, pwd) = RunModeSettings.ReadSavedLogin(ini);
        Assert.False(remember);
        Assert.Equal("", user);
        Assert.Equal("", pwd);
        // RememberLogin=0 保留，Saved 键已删（读回缺省空）
        Assert.Equal("", IniFile.ReadStr(ini, "setting", "SavedUser", ""));
    }
}
