using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private sealed class ProfilePicker : IAvatarFilePicker { public string? Path { get; set; } public string? Pick() => Path; }
    private static UserProfileService ProfileService(ApplicationDataPaths paths) => new(new UserProfileRepository(new LauncherDbContextFactory(paths)), paths);
    private static string MakeAvatar(string directory, string name, bool alternate = false, BitmapEncoder? encoder = null)
    {
        Directory.CreateDirectory(directory);
        var pixels = new byte[96 * 64 * 4];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 96; x++)
        {
            int i = (y * 96 + x) * 4;
            bool mark = (x is >= 30 and <= 38 && y is >= 15 and <= 48) || (x is >= 57 and <= 65 && y is >= 15 and <= 48)
                || (x is >= 38 and <= 57 && Math.Abs(y - (x - 38) * 1.7 - 15) < 4);
            pixels[i] = mark ? (byte)248 : alternate ? (byte)158 : (byte)180;
            pixels[i + 1] = mark ? (byte)248 : alternate ? (byte)102 : (byte)145;
            pixels[i + 2] = mark ? (byte)248 : alternate ? (byte)118 : (byte)68;
            pixels[i + 3] = 255;
        }
        var source = BitmapSource.Create(96, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 96 * 4);
        encoder ??= new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        string path = Path.Combine(directory, name);
        using var file = File.Create(path); encoder.Save(file); return path;
    }
    private static void ProfileKey(byte key)
    {
        LaunchNative.keybd_event(key, 0, 0, UIntPtr.Zero);
        LaunchNative.keybd_event(key, 0, 2, UIntPtr.Zero);
    }
    private static async Task RunProfileAsync(MainWindow window)
    {
        var paths = new ApplicationDataPaths(Path.Combine(_fixtures, "profile"));
        using var content = RecentContent(paths);
        await content.InitializeAsync();
        await content.ImportAsync([Path.Combine(Environment.SystemDirectory, "notepad.exe")]);
        string before = ProfileExistingState(paths);
        var service = ProfileService(paths);
        var picker = new ProfilePicker();
        var profile = new UserProfileViewModel(service, picker, content.Toast);
        await profile.InitializeAsync();
        var shell = new MainWindowViewModel(content, profile);
        window.DataContext = shell; window.Topmost = true; window.Activate(); window.UpdateLayout();
        Check(profile.DisplayName == UserProfileService.DefaultName() && profile.AvatarSource is null, "Default profile uses OS name and Nova avatar");
        await ProfileService(paths).LoadAsync();
        using (var db = new LauncherDbContextFactory(paths).CreateDbContext())
        {
            Check(await db.UserProfiles.CountAsync() == 1, "Repeated initialization creates exactly one profile");
            Check(!db.Database.HasPendingModelChanges() && !(await db.Database.GetPendingMigrationsAsync()).Any(), "Profile migration and snapshot match");
        }
        Check(!shell.Sidebar.FooterItems.Any(x => x.IsSettings), "Sidebar no longer exposes Settings navigation");
        var view = Descendants<ProfileView>(window).Single();
        var avatar = (Button)view.FindName("AvatarButton");
        var popup = (Popup)view.FindName("Flyout");
        SaveWindow(window, "sidebar-default.png");
        Check(avatar.ActualWidth == 48 && avatar.ActualHeight == 48 && (string)avatar.ToolTip == profile.DisplayName, "Avatar has 48 DIP hit area and name tooltip");
        await ClickFavoriteElementAsync(window, avatar);
        await WaitForAsync(() => popup.IsOpen);
        await Task.Delay(250);
        CaptureCategoryWindow(window, "profile-default.png");
        var surface = (Border)popup.Child;
        double defaultHeight = surface.ActualHeight;
        Check(double.IsNaN(surface.Height) && defaultHeight < 240, "Default Flyout sizes naturally to two actions");
        Check(popup.IsOpen && profile.IsProfileFlyoutOpen, "Native avatar click opens Profile Flyout");
        await ClickFavoriteElementAsync(window, avatar);
        await Task.Delay(180);
        Check(!popup.IsOpen, "Second avatar click closes without reopening");
        avatar.Focus(); ProfileKey(0x0D);
        await WaitForAsync(() => popup.IsOpen);
        ProfileKey(0x1B); await WaitForAsync(() => !popup.IsOpen);
        Check(avatar.IsKeyboardFocused, "Enter opens and Escape closes returning focus");
        ProfileKey(0x20); await WaitForAsync(() => popup.IsOpen);
        await ClickFavoriteElementAsync(window, Descendants<ContentView>(window).Single());
        await WaitForAsync(() => !popup.IsOpen);
        Check(!profile.IsProfileFlyoutOpen, "Space opens and outside click closes");
        profile.ToggleProfileFlyoutCommand.Execute(null); await Task.Delay(120);
        profile.BeginEditNameCommand.Execute(null);
        var editor = (TextBox)view.FindName("NameEditor"); window.UpdateLayout(); editor.Focus();
        profile.DraftName = "  Nova 本地用户  "; ProfileKey(0x0D);
        await WaitForAsync(() => !profile.IsBusy && !profile.IsEditingName);
        Check(profile.DisplayName == "Nova 本地用户" && (string)avatar.ToolTip == profile.DisplayName, "Enter trims and saves name with immediate tooltip update");
        await Task.Delay(100);
        Check(Math.Abs(surface.ActualHeight - defaultHeight) < 1, "Saved name preserves compact Flyout layout");
        profile.BeginEditNameCommand.Execute(null); profile.DraftName = "discard"; editor.Focus(); ProfileKey(0x1B);
        await Task.Delay(150);
        Check(!profile.IsEditingName && profile.DisplayName == "Nova 本地用户" && popup.IsOpen, "Editing Escape cancels draft without closing Flyout");
        foreach (var pair in new[] { ("  ", "名称不能为空"), (new string('a', 25), "名称过长") })
        {
            profile.BeginEditNameCommand.Execute(null); profile.DraftName = pair.Item1;
            await profile.SaveNameCommand.ExecuteAsync(null);
            Check(content.Toast.Message == pair.Item2 && profile.DisplayName == "Nova 本地用户", "Invalid name rejected: " + pair.Item2);
        }
        profile.CancelEditNameCommand.Execute(null);
        string first = MakeAvatar(_fixtures, "avatar-one.png");
        string second = MakeAvatar(_fixtures, "avatar-two.png", true);
        profile.IsProfileFlyoutOpen = false;
        Check(await NativeProfilePickerAsync(first) == first, "Native Windows picker selects PNG from owned test fixture");
        Check(await NativeProfilePickerAsync(null) is null, "Native Windows picker cancellation returns no selection");
        profile.IsProfileFlyoutOpen = true; await Task.Delay(150);
        picker.Path = null; await profile.ChangeAvatarCommand.ExecuteAsync(null);
        Check(!profile.HasCustomAvatar, "Cancelled picker leaves profile unchanged");
        picker.Path = first; await profile.ChangeAvatarCommand.ExecuteAsync(null);
        Check(profile.HasCustomAvatar && profile.AvatarSource is not null, "PNG updates both bound avatar views");
        var loaded = (await service.LoadAsync()).Profile!;
        Check(loaded.AvatarPath != first && File.Exists(loaded.AvatarPath) && Path.GetDirectoryName(loaded.AvatarPath) == paths.ProfileDirectory, "Avatar is copied to owned Profile storage");
        File.Delete(first);
        using (new FileStream(loaded.AvatarPath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        picker.Path = second; await profile.ChangeAvatarCommand.ExecuteAsync(null);
        Check(profile.AvatarSource is not null && !File.Exists(loaded.AvatarPath), "Consecutive replacement succeeds with no WPF file lock");
        await WaitForAsync(() => !content.Toast.IsOpen); await Task.Delay(200); CaptureCategoryWindow(window, "profile-custom.png");
        double customHeight = surface.ActualHeight;
        Check(Math.Abs(customHeight - defaultHeight - 36) < 1, "Custom Flyout adds exactly one action row without extra whitespace");
        foreach (var pair in new[] { ("photo.jpg", (BitmapEncoder)new JpegBitmapEncoder()), ("photo.jpeg", new JpegBitmapEncoder()), ("photo.bmp", new BmpBitmapEncoder()) })
        {
            var result = await service.ChangeAvatarAsync(MakeAvatar(_fixtures, pair.Item1, encoder: pair.Item2));
            Check(result.Error is null && result.Avatar![0] == 137 && result.Avatar[1] == 80, "Converts supported image to PNG: " + pair.Item1);
        }
        picker.Path = Path.Combine(_fixtures, "bad.gif"); await profile.ChangeAvatarCommand.ExecuteAsync(null);
        Check(content.Toast.Message == "不支持此图片格式", "Unsupported image uses existing toast");
        picker.Path = Path.Combine(_fixtures, "bad.png"); File.WriteAllText(picker.Path, "broken");
        await profile.ChangeAvatarCommand.ExecuteAsync(null);
        Check(content.Toast.Message == "无法读取该图片" && profile.HasCustomAvatar, "Corrupt image keeps previous avatar and shows toast");
        var restored = await ProfileService(paths).LoadAsync();
        Check(restored.Profile!.DisplayName == profile.DisplayName && restored.Avatar is { Length: > 0 }, "New service graph reloads name and avatar");
        using (var connection = OpenDatabase(paths.DatabasePath))
        using (var transaction = connection.BeginTransaction())
        {
            profile.BeginEditNameCommand.Execute(null); profile.DraftName = "must fail";
            await profile.SaveNameCommand.ExecuteAsync(null);
            Check(profile.DisplayName == "Nova 本地用户" && profile.DraftName == profile.DisplayName && content.Toast.Message == "名称保存失败", "Locked SQLite leaves displayed and persisted name unchanged");
            var old = restored.Profile.AvatarPath;
            var result = await service.ChangeAvatarAsync(second);
            Check(result.Error == "头像保存失败" && File.Exists(old), "Avatar database failure preserves old PNG");
        }
        profile.CancelEditNameCommand.Execute(null);
        await profile.ResetAvatarCommand.ExecuteAsync(null);
        Check(!profile.HasCustomAvatar && profile.AvatarSource is null && (await ProfileService(paths).LoadAsync()).Profile!.AvatarPath is null, "Reset commits null and restores both default avatars");
        await Task.Delay(150);
        Check(popup.IsOpen && Math.Abs(surface.ActualHeight - defaultHeight) < 1, "Reset immediately shrinks open Flyout to default height");
        profile.IsProfileFlyoutOpen = true; await Task.Delay(100);
        var anchor = avatar.PointToScreen(new Point()); var flyoutPoint = popup.Child.PointToScreen(new Point());
        window.Left += 25; window.Top += 10; await Task.Delay(180);
        var anchorNow = avatar.PointToScreen(new Point()); var flyoutNow = popup.Child.PointToScreen(new Point());
        Check(Math.Abs((flyoutNow.X - flyoutPoint.X) - (anchorNow.X - anchor.X)) < 2, "Flyout follows window movement");
        window.Width = 800; window.Height = 560; window.UpdateLayout(); await Task.Delay(150);
        var sidebarColumn = (ColumnDefinition)window.FindName("SidebarColumn"); sidebarColumn.Width = new GridLength(220);
        window.UpdateLayout(); await Task.Delay(150);
        Check(avatar.IsVisible && avatar.TranslatePoint(new Point(), window).Y + 48 < window.ActualHeight, "Bottom avatar stays inside minimum window with scrolling navigation");
        Check(Math.Abs(surface.PointToScreen(new Point()).X - avatar.PointToScreen(new Point()).X) < 2,
            "Sidebar width change preserves Flyout horizontal anchor");
        CaptureCategoryWindow(window, "profile-small.png");
        window.WindowState = WindowState.Maximized; await Task.Delay(220);
        Check(popup.IsOpen && popup.Child.PointToScreen(new Point()).Y < avatar.PointToScreen(new Point()).Y, "Flyout remains above anchor after maximize");
        window.WindowState = WindowState.Normal; await Task.Delay(150);
        Check(popup.IsOpen && surface.PointToScreen(new Point()).Y < avatar.PointToScreen(new Point()).Y,
            "Restore preserves Flyout above avatar");
        profile.OpenSettingsCommand.Execute(null); window.UpdateLayout();
        Check(content.Mode == WorkspaceMode.Settings && !popup.IsOpen && shell.Sidebar.TopSelectedItem is null && shell.Sidebar.FooterSelectedItem is null, "Settings closes Flyout and has no Sidebar active item");
        SaveWindow(window, "settings-placeholder.png");
        shell.Sidebar.SelectedItem = shell.Sidebar.Items[0];
        Check(content.IsAllApplications, "Home exits Settings normally");
        foreach (var target in new[] { shell.Sidebar.Items.OfType<CategoryViewModel>().First(), shell.Sidebar.FooterItems[0], shell.Sidebar.FooterItems[1] })
        {
            profile.OpenSettingsCommand.Execute(null);
            shell.Sidebar.SelectedItem = target;
            var expectedMode = target is CategoryViewModel ? WorkspaceMode.Category : target.Mode;
            Check(content.Mode == expectedMode && shell.Sidebar.SelectedItem == target,
                $"Settings exits to {expectedMode} with synchronized Sidebar selection");
        }
        Check(ProfileExistingState(paths) == before, "Profile operations preserve applications categories stats favorites and icon bytes");
        await VerifyProfileFailureAsync(content.Toast);
    }
    private sealed class BrokenProfileRepository : IUserProfileRepository
    {
        public Task<UserProfile> GetOrCreateAsync(string name) => Task.FromException<UserProfile>(new IOException("test read failure"));
        public Task<UserProfile> SetNameAsync(string name) => throw new NotSupportedException();
        public Task<UserProfile> SetAvatarAsync(string? path) => throw new NotSupportedException();
    }
    private static class ProfileDialogNative
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string? className, string title);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr GetDlgItem(IntPtr handle, int id);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        internal static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, string text);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    }
    private static async Task<string?> NativeProfilePickerAsync(string? path, string title = "更换头像", Func<string?>? pick = null)
    {
        var worker = Task.Run(async () =>
        {
            IntPtr dialog = IntPtr.Zero;
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (dialog == IntPtr.Zero && timeout.Elapsed < TimeSpan.FromSeconds(15))
            {
                var candidate = ProfileDialogNative.FindWindow(null, title);
                ProfileDialogNative.GetWindowThreadProcessId(candidate, out uint process);
                if (process == Environment.ProcessId) dialog = candidate;
                else await Task.Delay(100);
            }
            if (dialog == IntPtr.Zero) throw new TimeoutException("Native avatar picker not found");
            try
            {
                await Task.Delay(1000);
                if (path is not null)
                {
                    var field = ProfileDialogNative.GetDlgItem(dialog, 1148);
                    if (field == IntPtr.Zero) throw new InvalidOperationException("File name field not found");
                    ProfileDialogNative.SendMessage(field, 0x000C, IntPtr.Zero, path);
                    await Task.Delay(250);
                }
                ProfileDialogNative.PostMessage(dialog, 0x0111, new IntPtr(path is null ? 2 : 1), IntPtr.Zero);
            }
            catch
            {
                ProfileDialogNative.PostMessage(dialog, 0x0111, new IntPtr(2), IntPtr.Zero);
                throw;
            }
        });
        string? selected = pick is null ? new AvatarFilePicker().Pick() : pick();
        await worker;
        return selected;
    }
    private static async Task VerifyProfileFailureAsync(IToastService toast)
    {
        var vm = new UserProfileViewModel(new UserProfileService(new BrokenProfileRepository(), new ApplicationDataPaths(_fixtures)), new ProfilePicker(), toast);
        await vm.InitializeAsync();
        Check(vm.DisplayName == "User" && vm.AvatarSource is null, "Profile read failure falls back without crashing launcher");
    }
    private static string ProfileExistingState(ApplicationDataPaths paths)
    {
        using var connection = OpenDatabase(paths.DatabasePath, true);
        string apps = Query(connection, "SELECT * FROM Applications ORDER BY Id");
        string categories = Query(connection, "SELECT * FROM Categories ORDER BY Id");
        var icons = Directory.Exists(paths.IconsDirectory) ? Directory.GetFiles(paths.IconsDirectory).OrderBy(x => x).Select(x => Path.GetFileName(x) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x)))).ToArray() : [];
        return JsonSerializer.Serialize(new { apps, categories, icons });
    }
    private sealed record ProfileSnapshot(string Existing, string Name, string AvatarHash, string? OriginalName = null, byte[]? OriginalAvatar = null);
    private static async Task<int> VerifyRealProfileAsync()
    {
        var paths = ApplicationDataPaths.Default;
        string snapshot = Path.Combine(Path.GetTempPath(), "Nova-profile-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Directory.CreateDirectory(paths.BackupsDirectory);
            string backup = Path.Combine(paths.BackupsDirectory, $"nova-before-profile-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}.db");
            using (File.Create(backup)) { }
            using (var source = OpenDatabase(paths.DatabasePath, true)) using (var destination = OpenDatabase(backup)) source.BackupDatabase(destination);
            Console.WriteLine("Backup: " + backup);
            File.WriteAllText(snapshot, JsonSerializer.Serialize(new ProfileSnapshot(ProfileExistingState(paths), "", "")));
            await RunCategoryChildAsync("--profile-real-run", snapshot);
            await RunCategoryChildAsync("--profile-real-check", snapshot);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { File.Delete(snapshot); }
    }
    private static int RunProfileWindow(string snapshotPath, bool exercise)
    {
        var app = new NovaLauncher.App.App(); app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            string? fixture = null;
            try
            {
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                await shell.Content.InitializeAsync(); await shell.Sidebar.Profile.InitializeAsync();
                var profile = shell.Sidebar.Profile;
                var paths = ApplicationDataPaths.Default;
                var expected = JsonSerializer.Deserialize<ProfileSnapshot>(File.ReadAllText(snapshotPath))!;
                Check(ProfileExistingState(paths) == expected.Existing, "Real migration preserves every application/category field and icon cache hash");
                using (var db = new LauncherDbContextFactory(paths).CreateDbContext())
                    Check(await db.UserProfiles.CountAsync() == 1 && !(await db.Database.GetPendingMigrationsAsync()).Any(), "Real UserProfiles singleton and migration history valid");
                if (exercise)
                {
                    var original = await ProfileService(paths).LoadAsync();
                    profile.BeginEditNameCommand.Execute(null); profile.DraftName = "Nova 用户";
                    await profile.SaveNameCommand.ExecuteAsync(null);
                    fixture = Path.Combine(Path.GetTempPath(), "Nova-profile-image-" + Guid.NewGuid().ToString("N"));
                    string png = MakeAvatar(fixture, "avatar.png");
                    var service = ProfileService(paths); await service.LoadAsync();
                    var result = await service.ChangeAvatarAsync(png);
                    Check(profile.DisplayName == "Nova 用户" && result.Error is null, "Real name and custom PNG committed");
                    // Refresh the actual running VM from its persisted state, without launching any application.
                    var refreshed = new UserProfileViewModel(ProfileService(paths), new AvatarFilePicker(), shell.Content.Toast);
                    await refreshed.InitializeAsync();
                    shell = new MainWindowViewModel(shell.Content, refreshed); app.MainWindow.DataContext = shell; profile = refreshed;
                    var current = await ProfileService(paths).LoadAsync();
                    expected = new(expected.Existing, profile.DisplayName, Convert.ToHexString(SHA256.HashData(current.Avatar!)), original.Profile!.DisplayName, original.Avatar);
                    File.WriteAllText(snapshotPath, JsonSerializer.Serialize(expected));
                    Directory.Delete(fixture, true); fixture = null;
                }
                else
                {
                    var current = await ProfileService(paths).LoadAsync();
                    Check(profile.DisplayName == expected.Name && profile.AvatarSource is not null && Convert.ToHexString(SHA256.HashData(current.Avatar!)) == expected.AvatarHash, "Independent real restart restores name and avatar after source deletion");
                }
                _screenshots = Path.GetFullPath("docs/screenshots/6.1"); Directory.CreateDirectory(_screenshots);
                app.MainWindow.Topmost = true; app.MainWindow.Activate(); app.MainWindow.UpdateLayout();
                SaveWindow(app.MainWindow, exercise ? "real-sidebar.png" : "real-restarted.png");
                profile.IsProfileFlyoutOpen = true; await Task.Delay(250);
                CaptureCategoryWindow(app.MainWindow, exercise ? "real-profile.png" : "real-profile-restarted.png");
                profile.IsProfileFlyoutOpen = false;
                Check(ProfileExistingState(paths) == expected.Existing, "Real profile changes leave all launcher data unchanged");
                if (!exercise)
                {
                    var restore = ProfileService(paths); await restore.LoadAsync();
                    var nameResult = await restore.SaveNameAsync(expected.OriginalName!);
                    ProfileResult avatarResult;
                    if (expected.OriginalAvatar is { } originalImage)
                    {
                        fixture = Path.Combine(Path.GetTempPath(), "Nova-profile-restore-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
                        string source = Path.Combine(fixture, "original.png"); await File.WriteAllBytesAsync(source, originalImage);
                        avatarResult = await restore.ChangeAvatarAsync(source);
                    }
                    else avatarResult = await restore.ResetAvatarAsync();
                    Check(nameResult.Error is null && avatarResult.Error is null, "Real test restores original profile after persistence proof");
                }
                app.MainWindow.Close();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
            finally { if (fixture is not null && Directory.Exists(fixture)) Directory.Delete(fixture, true); }
        }));
        return app.Run();
    }
}
