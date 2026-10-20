using System.Windows;
using System.Windows.Threading;

namespace NovaLauncher.App.Diagnostics;

public sealed class UnhandledExceptionLogging : IDisposable
{
    private readonly Application _application;
    private readonly NovaLog _log;
    public UnhandledExceptionLogging(Application application, NovaLog log)
    {
        _application = application; _log = log;
        application.DispatcherUnhandledException += OnDispatcherException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Write(LogLevel.Critical, "Dispatcher", "Unhandled exception; termination permitted", e.Exception);
        _log.FlushEmergency(TimeSpan.FromSeconds(1));
        // Never set Handled: continuing could leave persisted and in-memory state inconsistent.
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        => _log.Write(LogLevel.Error, "TaskScheduler", "Unobserved task exception", e.Exception);

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        _log.Write(LogLevel.Critical, "AppDomain", "Unhandled exception", e.ExceptionObject as Exception);
        _log.FlushEmergency(TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        _application.DispatcherUnhandledException -= OnDispatcherException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainException;
    }
}
