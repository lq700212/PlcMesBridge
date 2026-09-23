// =========================================================================
// ProductRowViewModel：产品表行（产品号 1..160 + 产品 ID 双向通知）。
// =========================================================================

namespace PlcMesBridge.ViewModels;

public class ProductRowViewModel : ViewModelBase
{
    private string _productId = "";

    public ProductRowViewModel(int no)
    {
        No = no;
    }

    public int No { get; }

    public string ProductId
    {
        get => _productId;
        set => Set(ref _productId, value ?? "");
    }
}
