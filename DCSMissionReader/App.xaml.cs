using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace DCSMissionReader;

public partial class App : Application
{
    private static readonly string _crashLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DCSMissionReader_crash.log");

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            File.AppendAllText(_crashLog, $"[{DateTime.Now:HH:mm:ss}] UI: {e.Exception}\n");
            e.Handled = true;
            MessageBox.Show($"CRASH:\n{e.Exception.Message}\n\nSee crash log on Desktop.", "DCSMissionReader", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            File.AppendAllText(_crashLog, $"[{DateTime.Now:HH:mm:ss}] DOMAIN: {e.ExceptionObject}\n");
            MessageBox.Show($"Fatal crash on background thread:\n{e.ExceptionObject}\n\nSee crash log on Desktop.", "DCSMissionReader", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            File.AppendAllText(_crashLog, $"[{DateTime.Now:HH:mm:ss}] TASK: {e.Exception}\n");
            e.SetObserved();
        };
    }
}

