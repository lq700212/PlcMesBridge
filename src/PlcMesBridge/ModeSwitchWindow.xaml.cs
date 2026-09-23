using System.Windows;
using PlcMesBridge.ViewModels;

namespace PlcMesBridge;

/// <summary>
/// 运行模式切换窗（MVVM 的 View：只设 DataContext + 响应关窗请求）。
/// 结果由 ViewModel.ModeChanged 带出（DialogService.ShowModeSwitch 读它）。
/// </summary>
public partial class ModeSwitchWindow : Window
{
    public ModeSwitchViewModel ViewModel { get; }

    public ModeSwitchWindow()
    {
        ViewModel = new ModeSwitchViewModel();
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };
    }
}
