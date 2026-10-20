using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    // Separate STA process: the invoking script can enforce a hard timeout on OLE.
    private static int RunExternalDrop(string[] args)
    {
        string path = System.IO.Path.GetFullPath(args[1]);
        int x = int.Parse(args[2]), y = int.Parse(args[3]);
        uint targetPid = uint.Parse(args[4]);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(() =>
        {
            var source = new Window { Width = 160, Height = 100, Left = 0, Top = 0,
                ShowInTaskbar = false, Topmost = true, Content = new TextBlock { Text = "Nova publish verification" } };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            int step = 0; bool finish = false, cancel = false;
            source.QueryContinueDrag += (_, e) => { e.Action = finish ? (cancel ? DragAction.Cancel : DragAction.Drop) : DragAction.Continue; e.Handled = true; };
            timer.Tick += (_, _) =>
            {
                ++step;
                if (step >= 3)
                {
                    ProfileDialogNative.GetWindowThreadProcessId(LaunchNative.WindowFromPoint(new(x, y)), out uint hitPid);
                    cancel = hitPid != targetPid; finish = true;
                }
                Native.SetCursorPos(x + step % 2, y);
            };
            int result = 1;
            try
            {
                source.Show();
                Native.SetCursorPos(30, 30);
                timer.Start();
                var effect = DragDrop.DoDragDrop(source, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
                result = effect == DragDropEffects.Copy ? 0 : 1;
                Console.WriteLine($"OLE external effect={effect}; steps={step}");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { timer.Stop(); source.Close(); app.Shutdown(result); }
        }));
        return app.Run();
    }
}
