using System.Windows;
using PlcMesBridge.ViewModels;

namespace PlcMesBridge;

/// <summary>图片查看（MVVM 的 View）。用法：new ImageWindow(全路径).Show()。</summary>
public partial class ImageWindow : Window
{
    public ImageViewModel ViewModel { get; }

    public ImageWindow(string imagePath)
    {
        ViewModel = new ImageViewModel(imagePath);
        DataContext = ViewModel;
        InitializeComponent();
    }
}
