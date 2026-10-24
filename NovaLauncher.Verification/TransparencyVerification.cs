using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using NovaLauncher.App.Views;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static int RunTransparencySystem(string rootPath, string output)
    {
        ApplicationDataPaths.ConfigureVerificationRoot(["--verification-data-root=" + Path.GetFullPath(rootPath)]);
        _screenshots = Path.GetFullPath(output); Directory.CreateDirectory(_screenshots);
        var app = new NovaLauncher.App.App(); app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            Window? companion = null;
            bool desktopShown = false;
            try
            {
                await app.ReadyTask;
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                await shell.Settings!.Appearance!.Background.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.Transparent);
                await WaitForAsync(() => app.MainWindow.AllowsTransparency);
                var window = app.MainWindow;
                window.Topmost = false; window.WindowState = WindowState.Normal;
                window.Width = 1000; window.Height = 650; window.Left = 100; window.Top = 70;
                companion = new Window { Title = "Nova Alt-Tab verification companion", Width = 1000, Height = 650,
                    Left = 110, Top = 80, Background = Brushes.LightBlue };
                companion.Show();
                // IsActive is WPF thread state, not proof that Windows accepted foreground
                // activation. Establish the MRU pair with real input before testing Alt+Tab.
                await ActivateSystemTestWindowAsync(companion);
                await ActivateSystemTestWindowAsync(window);
                IntPtr originalHandle = new WindowInteropHelper(window).Handle;
                Check(TransparencyNative.GetForegroundWindow() == originalHandle && !window.Topmost && !companion.Topmost,
                    "Alt+Tab precondition: Nova is actual foreground and neither test window is topmost");
                Console.WriteLine($"AltTab before: {DescribeSystemForeground()} nova={originalHandle} companion={new WindowInteropHelper(companion).Handle}");
                await SendSystemChordAsync(0x12, 0x09);
                await WaitForAsync(() => TransparencyNative.GetForegroundWindow() == new WindowInteropHelper(companion).Handle);
                Console.WriteLine($"AltTab away: {DescribeSystemForeground()}");
                Check(TransparencyNative.GetForegroundWindow() == new WindowInteropHelper(companion).Handle, "Native Alt+Tab switches from transparent Nova to companion");
                await SendSystemChordAsync(0x12, 0x09);
                await WaitForAsync(() => TransparencyNative.GetForegroundWindow() == originalHandle);
                Console.WriteLine($"AltTab return: {DescribeSystemForeground()}");
                Check(TransparencyNative.GetForegroundWindow() == new WindowInteropHelper(window).Handle, "Native Alt+Tab returns to transparent Nova");
                Check(window.IsVisible && window.AllowsTransparency && window.Opacity == 1 && new WindowInteropHelper(window).Handle == originalHandle,
                    "Alt+Tab preserves visible Glass HWND and full window opacity");
                await SendSystemChordAsync(0x11, 0x4B);
                await WaitForAsync(() => shell.Content.QuickLauncher.IsOpen && Keyboard.FocusedElement is TextBox { Name: "QueryBox" });
                Check(shell.Content.QuickLauncher.IsOpen, "Ctrl+K remains interactive and focuses Quick Launcher after Alt+Tab");
                CaptureCategoryWindow(window, "alt-tab-restored-quick-launcher.png", activate: false);
                ProfileKey(0x1B);
                await WaitForAsync(() => !shell.Content.QuickLauncher.IsOpen);
                SendSystemChord(0x5B, 0x44); desktopShown = true; await Task.Delay(500);
                Check(TransparencyNative.GetForegroundWindow() != new WindowInteropHelper(window).Handle, "Win+D exposes desktop instead of leaving transparent Nova active");
                SendSystemChord(0x5B, 0x44); desktopShown = false; await Task.Delay(500);
                Check(window.IsVisible && window.WindowState != WindowState.Minimized, "Second Win+D restores transparent Nova");
                window.Activate(); LaunchNative.SetForegroundWindow(new WindowInteropHelper(window).Handle);
                SendSystemChord(0x5B, 0x5A); await Task.Delay(700);
                CaptureCategoryWindow(window, "snap-layout-panel.png", activate: false);
                ProfileKey(0x1B); await Task.Delay(150);
                double originalWidth = window.ActualWidth;
                SendSystemChord(0x5B, 0x25); await Task.Delay(500); ProfileKey(0x1B);
                Check(window.ActualWidth < originalWidth && window.AllowsTransparency, "Win+Left snaps transparent window without losing layered surface");
                window.WindowState = WindowState.Normal; window.Width = 1000; window.Height = 650;
                await shell.Settings.Appearance.Background.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.Default);
                await WaitForAsync(() => !app.MainWindow.AllowsTransparency);
                Console.WriteLine($"PASS: {_checks} system-window assertions; Win+Z screenshot requires visual review");
                companion.Close(); companion = null; app.RequestExplicitExit();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); companion?.Close(); app.Shutdown(1); }
            finally { if (desktopShown) SendSystemChord(0x5B, 0x44); }
        }));
        return app.Run();
    }

    private static void SendSystemChord(byte modifier, byte key)
    {
        LaunchNative.keybd_event(modifier, 0, 0, UIntPtr.Zero);
        try { ProfileKey(key); }
        finally { LaunchNative.keybd_event(modifier, 0, 2, UIntPtr.Zero); }
    }

    private static async Task ActivateSystemTestWindowAsync(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        // Some command hosts supply SW_HIDE. This only prepares the verification window.
        LaunchNative.ShowWindow(handle, 1);
        try
        {
            window.Topmost = true;
            window.Activate();
            LaunchNative.SetForegroundWindow(handle);
            await Task.Delay(150);
            Point point = window.PointToScreen(new Point(300, 15));
            Native.SetCursorPos((int)point.X, (int)point.Y);
            IntPtr hit = LaunchNative.WindowFromPoint(new((int)point.X, (int)point.Y));
            if (TransparencyNative.GetAncestor(hit, 2) != handle)
            {
                Console.WriteLine($"Foreground setup blocked: target={handle} hit={hit} root={TransparencyNative.GetAncestor(hit, 2)} point={point} {DescribeSystemForeground()}");
                CaptureCategoryWindow(window, "foreground-setup-blocked.png", activate: false);
                throw new InvalidOperationException("System test foreground setup is obstructed; refusing input.");
            }
            LaunchNative.mouse_event(2, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(80);
            LaunchNative.mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            await WaitForAsync(() => TransparencyNative.GetForegroundWindow() == handle);
        }
        finally { window.Topmost = false; }
        await Task.Delay(250);
        await WaitForAsync(() => TransparencyNative.GetForegroundWindow() == handle);
    }

    private static async Task SendSystemChordAsync(byte modifier, byte key)
    {
        LaunchNative.keybd_event(modifier, 0, 0, UIntPtr.Zero);
        try
        {
            await Task.Delay(50);
            LaunchNative.keybd_event(key, 0, 0, UIntPtr.Zero);
            try { await Task.Delay(80); }
            finally { LaunchNative.keybd_event(key, 0, 2, UIntPtr.Zero); }
            await Task.Delay(50);
        }
        finally { LaunchNative.keybd_event(modifier, 0, 2, UIntPtr.Zero); }
    }

    private static string DescribeSystemForeground()
    {
        IntPtr handle = TransparencyNative.GetForegroundWindow();
        var title = new System.Text.StringBuilder(256);
        TransparencyNative.GetWindowText(handle, title, title.Capacity);
        return $"foreground={handle} title={title}";
    }

    private static int RunTransparencyLive(string rootPath, string output, bool polish = false)
    {
        ApplicationDataPaths.ConfigureVerificationRoot(["--verification-data-root=" + Path.GetFullPath(rootPath)]);
        _screenshots = Path.GetFullPath(output); Directory.CreateDirectory(_screenshots);
        var app = new NovaLauncher.App.App(); app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            Window? behind = null;
            Window? shield = null;
            try
            {
                await app.ReadyTask;
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                var appearance = shell.Settings!.Appearance!;
                Check(shell.Content.Applications.Count == 300, "Live production App loaded 300 isolated applications");
                // Cover unrelated desktop content in evidence captures with another real test window.
                shield = new Window { Left = 0, Top = 0, Width = SystemParameters.PrimaryScreenWidth,
                    Height = SystemParameters.PrimaryScreenHeight, WindowStyle = WindowStyle.None,
                    Background = Brushes.LightGray, Topmost = true, ShowActivated = false, ShowInTaskbar = false };
                shield.Show();
                var pattern = new Grid(); pattern.ColumnDefinitions.Add(new()); pattern.ColumnDefinitions.Add(new());
                pattern.Children.Add(new Border { Background = Brushes.Red });
                var blue = new Border { Background = Brushes.Blue }; Grid.SetColumn(blue, 1); pattern.Children.Add(blue);
                var words = new TextBlock { Text = "LIVE WINDOW BEHIND NOVA\n0123456789 ABCDEFG", FontSize = 40,
                    Foreground = Brushes.Black, Margin = new Thickness(40, 360, 0, 0) };
                Grid.SetColumnSpan(words, 2); pattern.Children.Add(words);
                behind = new Window { Title = "Nova verification — independent live window", Left = 60, Top = 60,
                    Width = 1000, Height = 900, Content = pattern, ShowActivated = false, Topmost = true };
                behind.Show();
                app.MainWindow.Topmost = true;
                await appearance.Background.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.Transparent);
                await WaitForAsync(() => app.MainWindow.AllowsTransparency);
                var window = (MainWindow)app.MainWindow;
                window.WindowState = WindowState.Normal; window.Width = 900; window.Height = 650;
                window.Left = 20; window.Top = 100; await FocusGlassTestWindowAsync(window);
                appearance.GlassOverlayPercent = 40; await appearance.FlushAsync();
                await WaitForAsync(() => Descendants<ApplicationCardView>(window).Count() == 300);
                await Task.Delay(600);
                Check(window.Opacity == 1 && window.AllowsTransparency, "Transparent shell uses per-pixel alpha with fully opaque Window and UI");
                uint before = ReadLivePixel(window);
                CaptureCategoryWindow(window, "transparent-before.png");
                behind.Left += 400; await Task.Delay(600);
                uint moved = ReadLivePixel(window);
                CaptureCategoryWindow(window, "transparent-behind-moved.png");
                window.Left += 200; await Task.Delay(600);
                uint novaMoved = ReadLivePixel(window);
                CaptureCategoryWindow(window, "transparent-nova-moved.png");
                Console.WriteLine($"Live pixels COLORREF: {before:X8}/{moved:X8}/{novaMoved:X8}");
                Check(before != uint.MaxValue && moved != uint.MaxValue && novaMoved != uint.MaxValue && before != moved && moved != novaMoved, "Behind-window movement and Nova movement independently change compositor pixels");
                appearance.GlassOverlayPercent = 80; await appearance.FlushAsync(); await Task.Delay(300);
                Check(ReadLivePixel(window) != novaMoved, "Glass tint changes backdrop pixels without changing Window opacity");
                behind.Activate(); await Task.Delay(200);
                Check(window.AllowsTransparency && ReadLivePixel(window) != uint.MaxValue, "Transparent host survives deactivation");
                await FocusGlassTestWindowAsync(window);
                window.Left = 100; behind.Left = 60;
                appearance.GlassOverlayPercent = 40; await appearance.FlushAsync(); await Task.Delay(200);
                if (polish)
                {
                    // A separate live gradient window provides a repeatable wallpaper-like visual fixture.
                    // The red/blue motion evidence above remains unchanged; no image enters Nova's backdrop.
                    behind.Content = new Border { Background = new LinearGradientBrush(new GradientStopCollection
                    {
                        new(Color.FromRgb(78, 111, 148), 0), new(Color.FromRgb(146, 178, 192), 0.35),
                        new(Color.FromRgb(211, 192, 170), 0.62), new(Color.FromRgb(103, 137, 162), 1)
                    }, new Point(0, 0), new Point(1, 1)) };
                    behind.Width = 1500; behind.Height = 1000; behind.Left = 0; behind.Top = 0;
                    window.Width = 1120; window.Height = 820; window.Left = 80; window.Top = 40;
                    await FocusGlassTestWindowAsync(window);
                    await Task.Delay(300);
                }
                CaptureCategoryWindow(window, "glass-all-applications.png");
                var searchTrigger = Descendants<Button>(window).Single(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x) == "QuickSearchTrigger");
                if (polish) CaptureCategoryWindow(window, "glass-search-normal.png", region: searchTrigger);
                Point searchPoint = searchTrigger.PointToScreen(new Point(80, 20));
                Native.SetCursorPos((int)searchPoint.X, (int)searchPoint.Y);
                await Task.Delay(200);
                CaptureCategoryWindow(window, "glass-search-hover.png");
                if (polish)
                {
                    Check(searchTrigger.IsMouseOver, "Glass search hover is reached by the actual pointer");
                    CaptureCategoryWindow(window, "glass-search-hover-detail.png", region: searchTrigger);
                    LaunchNative.mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                    try
                    {
                        await Task.Delay(150);
                        Check(searchTrigger.IsPressed, "Glass search active state uses the actual pressed button");
                        CaptureCategoryWindow(window, "glass-search-active.png", region: searchTrigger);
                    }
                    finally { LaunchNative.mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
                    await Task.Delay(200); shell.Content.QuickLauncher.Close();
                    await FocusGlassTestWindowAsync(window);
                    Keyboard.Focus(searchTrigger); await Task.Delay(150);
                    Check(searchTrigger.IsKeyboardFocused && !searchTrigger.IsMouseOver, "Glass keyboard focus is visible independently of hover");
                    CaptureCategoryWindow(window, "glass-search-focus.png", region: searchTrigger);
                    appearance.GlassOverlayPercent = 72; await appearance.FlushAsync(); await Task.Delay(150);
                    CaptureCategoryWindow(window, "glass-all-applications-72.png");
                    appearance.GlassOverlayPercent = 40; await appearance.FlushAsync();
                }
                Console.WriteLine("Glass brushes: " + window.FindResource("SidebarBrush") + "/" + window.FindResource("HoverBrush") + "; frozen=" + ((SolidColorBrush)window.FindResource("SidebarBrush")).IsFrozen);
                Check(Descendants<ApplicationCardView>(window).Count() == 300, "All 300 cards rendered once through progressive view");
                await FocusGlassTestWindowAsync(window);
                LaunchNative.keybd_event(0x11, 0, 0, UIntPtr.Zero); ProfileKey(0x4B); LaunchNative.keybd_event(0x11, 0, 2, UIntPtr.Zero);
                await WaitForAsync(() => shell.Content.QuickLauncher.IsOpen);
                shell.Content.QuickLauncher.SearchQuery = "299"; await Task.Delay(300);
                Check(shell.Content.QuickLauncher.IsOpen && shell.Content.QuickLauncher.Results.Count > 0, "Quick Launcher searches full collection in transparent mode");
                CaptureCategoryWindow(window, "transparent-quick-launcher.png");
                shell.Content.QuickLauncher.Close();
                var card = Descendants<ApplicationCardView>(window).First();
                await OpenCardMenuAsync(card, true);
                Check(card.ContextMenu.IsOpen, "Native Shift+F10 opens card management in layered window");
                CaptureCategoryWindow(window, "transparent-context-menu.png");
                card.ContextMenu.IsOpen = false;
                var button = (Button)card.FindName("LaunchButton");
                Point center = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
                Native.SetCursorPos((int)center.X, (int)center.Y); await Task.Delay(350);
                Check(card.IsMouseOver, "Hover reaches application card");
                CaptureCategoryWindow(window, "transparent-hover.png");
                double oldWidth = window.Width; window.Width -= 100; await Task.Delay(150);
                Check(window.ActualWidth < oldWidth, "Transparent resize updates layout"); window.Width = oldWidth;
                Point edge = window.PointToScreen(new Point(window.ActualWidth - 2, 300));
                int packedPoint = ((int)edge.Y << 16) | ((int)edge.X & 0xffff);
                Check(TransparencyNative.SendMessage(new WindowInteropHelper(window).Handle, 0x0084, IntPtr.Zero, new IntPtr(packedPoint)).ToInt64() == 11,
                    "WindowChrome preserves native right-edge resize hit testing");
                window.WindowState = WindowState.Maximized; await Task.Delay(400);
                Check(window.WindowState == WindowState.Maximized, "Transparent maximize"); CaptureCategoryWindow(window, "transparent-maximized.png");
                window.WindowState = WindowState.Normal; await Task.Delay(200);
                Check(Math.Abs(window.ActualWidth - oldWidth) < 2, "Transparent restore preserves normal width");
                window.WindowState = WindowState.Minimized; await Task.Delay(150);
                Check(window.WindowState == WindowState.Minimized, "Transparent minimize");
                window.WindowState = WindowState.Normal; window.Activate();
                var profile = Descendants<ProfileView>(window).Single();
                ((Button)profile.FindName("AvatarButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(200);
                Check(((Popup)profile.FindName("Flyout")).IsOpen, "Profile flyout opens over transparent shell");
                CaptureCategoryWindow(window, "transparent-profile.png");
                ((Popup)profile.FindName("Flyout")).IsOpen = false;
                shell.Sidebar.OpenSettings(); window.UpdateLayout();
                ((Expander)Descendants<SettingsView>(window).Single().FindName("BackgroundExpander")).IsExpanded = true;
                await Task.Delay(200); CaptureCategoryWindow(window, "transparent-settings.png");
                if (polish)
                {
                    ((Expander)Descendants<SettingsView>(window).Single().FindName("BackgroundExpander")).IsExpanded = false;
                    await Task.Delay(200); CaptureCategoryWindow(window, "glass-settings.png");
                    appearance.GlassOverlayPercent = 72; await appearance.FlushAsync(); await Task.Delay(150);
                    CaptureCategoryWindow(window, "glass-settings-72.png");
                    appearance.GlassOverlayPercent = 40; await appearance.FlushAsync();
                    ((Expander)Descendants<SettingsView>(window).Single().FindName("BackgroundExpander")).IsExpanded = true;
                }
                var categoryView = Descendants<CategoryManagementView>(window).Single();
                categoryView.BringIntoView(); await Task.Delay(300);
                CaptureCategoryWindow(window, "glass-category-management.png");
                if (polish) CaptureCategoryWindow(window, "glass-category-detail.png", region: categoryView);
                Descendants<ScrollViewer>(Descendants<SettingsView>(window).Single()).First().ScrollToTop();
                await Task.Delay(150);
                Check(appearance.IsTransparent && !appearance.IsImageMode && appearance.Modes.Count == 4, "Settings separates experimental transparent mode from static images");
                ((ColumnDefinition)window.FindName("SidebarColumn")).Width = new GridLength(260);
                await appearance.Background.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.Default);
                await WaitForAsync(() => !app.MainWindow.AllowsTransparency);
                Check(ReferenceEquals(app.MainWindow.DataContext, shell) && shell.Content.Mode == WorkspaceMode.Settings, "Default immediately restores normal HWND without losing navigation or data");
                Check(!appearance.ShowTransparencyStatus, "Default mode does not claim it is showing a live backdrop");
                Check(((Expander)Descendants<SettingsView>(app.MainWindow).Single().FindName("BackgroundExpander")).IsExpanded &&
                    ((ColumnDefinition)app.MainWindow.FindName("SidebarColumn")).Width.Value == 260, "Host replacement preserves appearance expansion and sidebar width");
                shell.Sidebar.SelectedItem = shell.Sidebar.Items[0]; await Task.Delay(800);
                CaptureCategoryWindow(app.MainWindow, "default-all-applications.png");
                string imagePath = Path.Combine(ApplicationDataPaths.Default.BackgroundDirectory, "performance.png");
                await appearance.Background.ImportAsync(imagePath);
                Check(appearance.IsCustom && appearance.Background.HasImage && !app.MainWindow.AllowsTransparency, "CustomImage remains usable after transparent mode");
                await Task.Delay(300); CaptureCategoryWindow(app.MainWindow, "custom-image-all-applications.png");
                await appearance.Background.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.DesktopWallpaper);
                Check(appearance.IsDesktop && appearance.Background.HasImage, "DesktopWallpaper still reads static system wallpaper");
                Environment.SetEnvironmentVariable("NOVA_DISABLE_TRANSPARENCY", "1");
                await appearance.Background.SetModeAsync(NovaLauncher.App.Models.BackgroundMode.Transparent);
                Check(appearance.Mode == NovaLauncher.App.Models.BackgroundMode.Default && !app.MainWindow.AllowsTransparency,
                    "Unsupported capability falls back to Default without opening transparent window");
                var unavailableSettings = new AppSettingsService(new UnwritableTransparentSettings());
                await unavailableSettings.LoadAsync();
                var unavailable = new AppearanceService(unavailableSettings, new BackgroundImageStore(ApplicationDataPaths.Default),
                    new WallpaperStub("unused"), shell.Content.Toast);
                await unavailable.InitializeAsync();
                Check(!unavailable.IsTransparent && unavailable.Preview.BackgroundMode == NovaLauncher.App.Models.BackgroundMode.Default,
                    "Unsupported startup remains opaque even when saving fallback settings fails");
                Environment.SetEnvironmentVariable("NOVA_DISABLE_TRANSPARENCY", null);
                using (var db = new LauncherDbContextFactory(ApplicationDataPaths.Default).CreateDbContext())
                    Check(!db.Database.HasPendingModelChanges(), "Transparent enum adds no Schema change");
                var fallbackWindow = new MainWindow(); fallbackWindow.ConfigureTransparentSurface(true); fallbackWindow.UseOpaqueSurface();
                Check(((SolidColorBrush)fallbackWindow.Background).Color.A == 255 && !fallbackWindow.Resources.Contains("SidebarBrush"),
                    "Emergency opaque surface restores global accessibility brushes");
                fallbackWindow.Close();
                behind.Close(); behind = null; shield.Close(); shield = null;
                Console.WriteLine($"PASS: {_checks} transparency assertions; Tier={RenderCapability.Tier >> 16}; DPI={VisualTreeHelper.GetDpi(app.MainWindow).PixelsPerDip}");
                app.RequestExplicitExit();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); behind?.Close(); shield?.Close(); app.Shutdown(1); }
            finally { Environment.SetEnvironmentVariable("NOVA_DISABLE_TRANSPARENCY", null); }
        }));
        return app.Run();
    }

    private static async Task FocusGlassTestWindowAsync(Window window)
    {
        // A persisted Topmost=true does not raise the window above a new test backdrop.
        window.Topmost = false; window.Topmost = true;
        window.Activate();
        Point title = window.PointToScreen(new Point(180, 24));
        Native.SetCursorPos((int)title.X, (int)title.Y);
        LaunchNative.mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        LaunchNative.mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        await WaitForAsync(() => TransparencyNative.GetForegroundWindow() == new WindowInteropHelper(window).Handle);
        await Task.Delay(200);
    }

    private static uint ReadLivePixel(Window window)
    {
        Point p = window.PointToScreen(new Point(window.ActualWidth - 12, 400));
        var dc = CaptureNative.GetDC(IntPtr.Zero);
        try { return TransparencyNative.GetPixel(dc, (int)p.X, (int)p.Y); }
        finally { CaptureNative.ReleaseDC(IntPtr.Zero, dc); }
    }

    private sealed class UnwritableTransparentSettings : IAppSettingsRepository
    {
        public Task<NovaLauncher.App.Models.AppSettings> GetOrCreateAsync(CancellationToken token = default) =>
            Task.FromResult(new NovaLauncher.App.Models.AppSettings { BackgroundMode = NovaLauncher.App.Models.BackgroundMode.Transparent });
        public Task<NovaLauncher.App.Models.AppSettings> SaveAsync(NovaLauncher.App.Models.AppSettings original,
            NovaLauncher.App.Models.AppSettings value, CancellationToken token = default) =>
            Task.FromException<NovaLauncher.App.Models.AppSettings>(new IOException("Injected settings write failure"));
    }
    // Screen reads below are verification evidence only; never a product background source.
    private static async Task RunTransparencyPrototypeAsync(MainWindow original)
    {
        await Task.Delay(700);
        original.Hide();
        var pattern = new Grid();
        pattern.ColumnDefinitions.Add(new()); pattern.ColumnDefinitions.Add(new());
        var red = new Border { Background = Brushes.Red };
        var blue = new Border { Background = Brushes.Blue }; Grid.SetColumn(blue, 1);
        pattern.Children.Add(red); pattern.Children.Add(blue);
        pattern.Children.Add(new TextBlock { Text = "LIVE WINDOW BEHIND NOVA\n0123456789 ABCDEFG", FontSize = 36,
            Foreground = Brushes.Black, Margin = new Thickness(30), VerticalAlignment = VerticalAlignment.Center });
        var behind = new Window { Title = "Nova transparency verification behind window", Left = 60, Top = 60,
            Width = 1300, Height = 900, Content = pattern, ShowActivated = false, Topmost = true };
        behind.Show();
        try
        {
            foreach (string route in new[] { "legacy-acrylic", "dwm-none", "dwm-mica", "dwm-tabbed", "dwm-acrylic", "layered" })
            {
                var window = new MainWindow { DataContext = original.DataContext, Left = 100, Top = 100,
                    Width = 1000, Height = 650, WindowStartupLocation = WindowStartupLocation.Manual, Topmost = true };
                var background = Descendants<ShellBackgroundView>(window).FirstOrDefault();
                // XAML's visual tree may not exist until layout; named root is available now.
                var frame = (Border)window.FindName("WindowFrame");
                var root = (Grid)frame.Child;
                background = root.Children.OfType<ShellBackgroundView>().Single();
                if (route == "legacy-acrylic") background.Mode = NovaLauncher.App.Views.BackgroundMode.Desktop;
                else
                {
                    root.Children.Remove(background);
                    root.Children.Insert(0, new Border { Background = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)) });
                    Grid.SetRowSpan(root.Children[0], 2);
                    window.Background = Brushes.Transparent;
                }
                if (route == "layered") window.AllowsTransparency = true;
                window.Show(); window.Activate(); window.UpdateLayout();
                var hwnd = new WindowInteropHelper(window).Handle;
                if (route.StartsWith("dwm-"))
                {
                    HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor = Colors.Transparent;
                    WindowChrome.GetWindowChrome(window).GlassFrameThickness = new Thickness(-1);
                    var margins = new TransparencyNative.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                    int frameResult = TransparencyNative.DwmExtendFrameIntoClientArea(hwnd, ref margins);
                    int type = route == "dwm-none" ? 1 : route == "dwm-mica" ? 2 : route == "dwm-tabbed" ? 4 : 3;
                    int result = TransparencyNative.DwmSetWindowAttribute(hwnd, 38, ref type, sizeof(int));
                    Console.WriteLine($"{route}: frame=0x{frameResult:X8}, backdrop=0x{result:X8}");
                }
                await Task.Delay(900);
                Console.WriteLine($"{route}: active={window.IsActive}; legacyStatus={System.Windows.Automation.AutomationProperties.GetItemStatus(background)}; UIEffects={SystemParameters.UIEffects}; remote={SystemParameters.IsRemoteSession}");
                CaptureCategoryWindow(window, route + "-before.png");
                var before = ReadTransparencyPixel(window);
                behind.Left += 350;
                await Task.Delay(700);
                CaptureCategoryWindow(window, route + "-behind-moved.png");
                var moved = ReadTransparencyPixel(window);
                window.Left += 300;
                await Task.Delay(700);
                CaptureCategoryWindow(window, route + "-nova-moved.png");
                var novaMoved = ReadTransparencyPixel(window);
                Console.WriteLine($"{route}: pixels={before:X8}/{moved:X8}/{novaMoved:X8}; tier={RenderCapability.Tier >> 16}; opacity={window.Opacity}; layered={window.AllowsTransparency}");
                window.Close(); behind.Left = 60;
            }
        }
        finally { behind.Close(); original.Show(); }
    }

    private static uint ReadTransparencyPixel(Window window)
    {
        Point p = window.PointToScreen(new Point(620, 500));
        var dc = CaptureNative.GetDC(IntPtr.Zero);
        try { return TransparencyNative.GetPixel(dc, (int)p.X, (int)p.Y); }
        finally { CaptureNative.ReleaseDC(IntPtr.Zero, dc); }
    }

    private static class TransparencyNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] internal static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("gdi32.dll")] internal static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder title, int capacity);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    }
}
