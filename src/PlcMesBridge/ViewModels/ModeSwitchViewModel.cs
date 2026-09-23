// =========================================================================
// ModeSwitchViewModel：运行模式切换窗的状态 + 命令（MVVM）。
//
// 绑什么：单机/多机二选一（双向互斥）+ 全部静态文本 + 保存取消命令。
// 保存写 ini（Core.RunModeSettings），模式变了置 ModeChanged 由主窗重启；
// 没变弹"已保存"提示（经 IDialogService，VM 不碰 MessageBox）。
// =========================================================================

using PlcMesBridge.Core.Infrastructure;

namespace PlcMesBridge.ViewModels;

public class ModeSwitchViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private bool _isSingle = AppConfig.Mode != RunMode.Multi;
    private bool _isMulti = AppConfig.Mode == RunMode.Multi;

    public ModeSwitchViewModel(IDialogService? dialogs = null)
    {
        _dialogs = dialogs ?? DialogService.Instance;
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    /// <summary>本次保存是否切换了模式（主窗据此决定是否自动重启）。</summary>
    public bool ModeChanged { get; private set; }

    public event Action<bool?>? RequestClose;

    public bool IsSingle
    {
        get => _isSingle;
        set
        {
            if (!Set(ref _isSingle, value) || !value)
                return;
            Set(ref _isMulti, false);
        }
    }

    public bool IsMulti
    {
        get => _isMulti;
        set
        {
            if (!Set(ref _isMulti, value) || !value)
                return;
            Set(ref _isSingle, false);
        }
    }

    public string TitleText => Tr("运行模式");
    public string HeadText => Tr("运行模式");
    public string SingleText => Tr("单机固化收料");
    public string MultiText => Tr("多机通用网关");
    public string HintText => Tr("切换模式保存后自动重启软件");
    public string SaveText => Tr("保存");
    public string CancelText => Tr("取消");

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public void RefreshTexts() => RefreshAll();

    private static string Tr(string zh) => LanguageService.Tr(zh);

    private void Save()
    {
        RunMode selected = IsMulti ? RunMode.Multi : RunMode.Single;
        ModeChanged = RunModeSettings.NeedsRestart(AppConfig.Mode, selected);
        RunModeSettings.SaveMode(AppPaths.ConfigIni, selected);
        if (!ModeChanged)
            _dialogs.ShowMessage(Tr("设置已保存"), Tr("运行模式"));
        RequestClose?.Invoke(true);
    }
}
