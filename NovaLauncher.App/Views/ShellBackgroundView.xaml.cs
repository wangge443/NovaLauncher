using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using NovaLauncher.App.Helpers;

namespace NovaLauncher.App.Views;

public enum BackgroundMode
{
    Desktop,
    CustomImage,
    SolidColor
}

// Presentation inputs and material lifecycle; HWND interop is isolated in the controller.
public partial class ShellBackgroundView : UserControl
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(BackgroundMode), typeof(ShellBackgroundView),
        new PropertyMetadata(BackgroundMode.Desktop, OnModeChanged));

    private static readonly DependencyPropertyKey IsBackdropActivePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsBackdropActive), typeof(bool), typeof(ShellBackgroundView), new PropertyMetadata(false));
    public static readonly DependencyProperty IsBackdropActiveProperty = IsBackdropActivePropertyKey.DependencyProperty;
    public bool IsBackdropActive => (bool)GetValue(IsBackdropActiveProperty);
    private DesktopBackdropController? _controller;

    public static readonly DependencyProperty BackgroundImageProperty = DependencyProperty.Register(
        nameof(BackgroundImage), typeof(ImageSource), typeof(ShellBackgroundView), new PropertyMetadata(null));

    public static readonly DependencyProperty SolidBackgroundProperty = DependencyProperty.Register(
        nameof(SolidBackground), typeof(Brush), typeof(ShellBackgroundView), new PropertyMetadata(null));

    public BackgroundMode Mode
    {
        get => (BackgroundMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public ImageSource? BackgroundImage
    {
        get => (ImageSource?)GetValue(BackgroundImageProperty);
        set => SetValue(BackgroundImageProperty, value);
    }

    public Brush? SolidBackground
    {
        get => (Brush?)GetValue(SolidBackgroundProperty);
        set => SetValue(SolidBackgroundProperty, value);
    }

    public ShellBackgroundView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (ShellBackgroundView)sender;
        if (view._controller is not null)
        {
            view._controller.Enabled = view.Mode == BackgroundMode.Desktop;
            view._controller.Refresh();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_controller is not null || Window.GetWindow(this) is not Window window) return;
        _controller = new DesktopBackdropController(window, (active, reason) =>
        {
            SetValue(IsBackdropActivePropertyKey, active);
            // This reports the API state, not a guarantee about the compositor's pixels.
            AutomationProperties.SetItemStatus(this, active ? reason : $"Fallback:{reason}");
        });
        _controller.Enabled = Mode == BackgroundMode.Desktop;
        _controller.Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _controller?.Dispose();
        _controller = null;
    }
}
