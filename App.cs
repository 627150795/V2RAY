using System.Windows;

namespace V2rayNMonitor;

public sealed class App : System.Windows.Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--worker"))
        {
            Environment.ExitCode = Worker.RunAsync(args).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--diagnose"))
        {
            Environment.ExitCode = Diagnostics.RunAsync().GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--list-nodes"))
        {
            Environment.ExitCode = Diagnostics.ListNodesAsync().GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--scores"))
        {
            Environment.ExitCode = Diagnostics.ScoresAsync(args).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--recommendations"))
        {
            Environment.ExitCode = Diagnostics.RecommendationsAsync().GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--self-test"))
        {
            Environment.ExitCode = SelfTest.RunAsync().GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--render-preview"))
        {
            Environment.ExitCode = VisualPreview.Render();
            return;
        }
        if (args.Contains("--migrate-old-data"))
        {
            Environment.ExitCode = DataMigrator.MigrateOldData();
            return;
        }

        using var mutex = new Mutex(true, "ProxyMonitor.MultiClient.SingleInstance", out var first);
        if (!first) return;
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow(startHidden: args.Contains("--background"));
        app.Run(window);
    }
}
