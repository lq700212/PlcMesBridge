using System.Windows;
using System.Windows.Media.Imaging;
using PlcMesBridge.Core.Infrastructure;

namespace PlcMesBridge;

/// <summary>图片查看（复刻 FormImage.New(imagepath)：设图 + 显示路径 + Show）。</summary>
public partial class ImageWindow : Window
{
    public ImageWindow(string imagePath)
    {
        InitializeComponent();
        Title = LanguageService.Tr("图片查看");
        LbPath.Content = imagePath;
        try
        {
            Img.Source = new BitmapImage(new Uri(imagePath));
        }
        catch
        {
            LbPath.Content = imagePath + "（加载失败）";
        }
    }
}
