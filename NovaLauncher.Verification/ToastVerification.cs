using System.IO;
using System.Windows;
using System.Windows.Input;
using NovaLauncher.App.Services;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private static async Task RunToastAsync(MainWindow window)
    {
        window.Topmost = true;
        window.Activate();
        var vm = ((MainWindowViewModel)window.DataContext).Content;
        await vm.InitializeAsync();
        var content = Descendants<ContentView>(window).Single();
        string exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        string link = MakeShortcut("Toast 同目标", exe);
        await DropAsync(window, content, [exe]);
        await WaitForAsync(() => vm.Applications.Count == 1 && !vm.IsImporting);
        Check(!vm.Toast.IsOpen, "First real EXE drop adds a card without duplicate toast");
        window.UpdateLayout();
        var card = Descendants<ApplicationCardView>(window).Single();
        var position = card.TranslatePoint(new Point(), content);
        var size = card.RenderSize;
        var focus = Keyboard.FocusedElement;
        await DropAsync(window, content, [exe]);
        await WaitForAsync(() => !vm.IsImporting && vm.Toast.IsOpen);
        Check(vm.Applications.Count == 1 && vm.Toast.Message == "该应用已添加", "Second real EXE drop shows duplicate toast with one card");
        await Task.Delay(220);
        var toast = Descendants<ToastView>(window).Single();
        Check(toast.Visibility == Visibility.Visible && toast.Opacity > 0.99 && !toast.IsHitTestVisible,
            "Toast fades in and never intercepts pointer input");
        Check(Keyboard.FocusedElement == focus, "Toast does not steal keyboard focus");
        Check(card.TranslatePoint(new Point(), content) == position && card.RenderSize == size,
            "Toast overlay preserves card position and size");
        SaveWindow(window, "duplicate-toast.png");
        await DropAsync(window, content, [link]);
        await WaitForAsync(() => !vm.IsImporting && vm.Toast.IsOpen);
        Check(vm.Applications.Count == 1 && vm.Toast.Message == "该应用已添加", "LNK to existing EXE shows the same duplicate toast");
        await Task.Delay(1200);
        await DropAsync(window, content, [exe]);
        await WaitForAsync(() => !vm.IsImporting);
        await Task.Delay(1100);
        Check(vm.Toast.IsOpen, "Repeated drop resets lifetime; stale timer cannot hide new toast");
        await WaitForAsync(() => !vm.Toast.IsOpen && toast.Visibility == Visibility.Hidden);
        Check(toast.Opacity < 0.01 && vm.Applications.Count == 1, "Toast automatically fades out after about two seconds");
        for (int i = 0; i < 4; i++)
        {
            await DropAsync(window, content, [i % 2 == 0 ? exe : link]);
            await WaitForAsync(() => !vm.IsImporting);
        }
        Check(vm.Applications.Count == 1 && vm.Toast.IsOpen && Descendants<ToastView>(window).Count() == 1,
            "Consecutive duplicate OLE drops reuse one toast without extra cards");
        using var notifications = new ToastService();
        notifications.Show("test");
        notifications.Dispose();
        await Task.Delay(2200);
        Check(!notifications.IsOpen, "Disposed notification cancels pending dismissal safely");
        Check(card.TranslatePoint(new Point(), content) == position && card.RenderSize == size,
            "Hidden toast preserves card layout");
    }
}
