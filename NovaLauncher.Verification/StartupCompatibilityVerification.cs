using System.IO;
using System.Text.Json;
using NovaLauncher.App.Models;
using NovaLauncher.App.Persistence;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.Verification;

internal static partial class Program
{
    private sealed record StartupCompatibilityRequest(string Root, StartupPage Page);

    private static int RunStartupCompatibility(string requestPath)
    {
        var request = JsonSerializer.Deserialize<StartupCompatibilityRequest>(File.ReadAllText(requestPath))!;
        ApplicationDataPaths.ConfigureVerificationRoot(["--verification-data-root=" + request.Root]);
        var app = new NovaLauncher.App.App();
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await app.ReadyTask;
                var shell = (MainWindowViewModel)app.MainWindow.DataContext;
                var expected = request.Page switch
                {
                    StartupPage.Favorites => WorkspaceMode.Favorites,
                    StartupPage.Recent => WorkspaceMode.Recent,
                    _ => WorkspaceMode.AllApplications
                };
                Check(shell.Content.Mode == expected, "Actual process startup routes saved page");
                Check(shell.Settings!.StartupPage == request.Page, "Saved startup value loads without fallback");
                if (request.Page == StartupPage.Home)
                {
                    Check(Descendants<AllApplicationsView>(app.MainWindow).Any(), "Legacy Home opens application library");
                    Check(shell.Settings.StartupPages.Single(x => x.Value == request.Page).Label == "全部应用", "Legacy Home has known Settings label");
                }
                app.RequestExplicitExit();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }
}
