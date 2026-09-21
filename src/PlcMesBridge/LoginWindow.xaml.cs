using System.Windows;
using PlcMesBridge.Core.Infrastructure;

namespace PlcMesBridge;

/// <summary>
/// 登录窗（设置按钮门禁：账号 admin / 密码 123456，本地防误触）。
/// 用法：ShowDialog() 返回 true 表示验证通过，调用方可继续弹设置窗。
/// 校验与记忆走 Core.RunModeSettings（可测），本窗只做"事件→控件"搬运。
/// 记住密码：勾选后把账号密码存 config.ini [setting]，下次自动预填；
///   取消勾选则清除已存账号（见 RunModeSettings.SaveLogin）。
/// </summary>
public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Title = LanguageService.Tr("登录");
        LbTitle.Content = LanguageService.Tr("请登录后进入设置");
        LbUser.Content = LanguageService.Tr("用户名");
        LbPwd.Content = LanguageService.Tr("密码");
        ChkRemember.Content = LanguageService.Tr("记住密码");
        BtnOk.Content = LanguageService.Tr("确定");
        BtnCancel.Content = LanguageService.Tr("取消");
        // 预填记忆（无记忆时返回空，不影响首次使用）。
        var (remember, user, pwd) = RunModeSettings.ReadSavedLogin(AppPaths.ConfigIni);
        if (remember)
        {
            TxtUser.Text = user;
            TxtPwd.Password = pwd;
            ChkRemember.IsChecked = true;
        }
        if (string.IsNullOrEmpty(TxtUser.Text))
            TxtUser.Focus();
        else
            TxtPwd.Focus();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (RunModeSettings.VerifyLogin(TxtUser.Text, TxtPwd.Password))
        {
            RunModeSettings.SaveLogin(AppPaths.ConfigIni,
                ChkRemember.IsChecked == true, TxtUser.Text, TxtPwd.Password);
            DialogResult = true;
            Close();
        }
        else
        {
            LbTip.Content = LanguageService.Tr("用户名或密码错误");
            TxtPwd.Clear();
            TxtPwd.Focus();
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
