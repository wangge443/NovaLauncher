using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NovaLauncher.App.Models;

namespace NovaLauncher.App.ViewModels;

public sealed class ApplicationCardViewModel
{
    public LauncherApplication Application { get; }
    public string DisplayName => Application.DisplayName;
    public ImageSource Icon { get; }

    public ApplicationCardViewModel(LauncherApplication application)
    {
        Application = application;
        if (application.Icon is { Length: > 0 } bytes)
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            Icon = image;
        }
        else
        {
            Icon = DefaultIcon;
        }
    }

    // Vector fallback stays sharp at any DPI and never owns a native resource.
    private static readonly ImageSource DefaultIcon = CreateDefaultIcon();

    private static ImageSource CreateDefaultIcon()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(82, 107, 181)), null,
            new RectangleGeometry(new Rect(0, 0, 72, 72), 16, 16)));
        group.Children.Add(new GeometryDrawing(null, new Pen(Brushes.White, 5),
            Geometry.Parse("M 23,51 L 23,21 49,51 49,21")));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
