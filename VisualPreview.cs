using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace V2rayNMonitor;

public static class VisualPreview
{
    public static int Render()
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow(true) { Width = 1260, Height = 900, WindowStyle = System.Windows.WindowStyle.None, ShowInTaskbar = false };
        window.Show();
        window.UpdateLayout();
        var width = (int)window.ActualWidth;
        var height = (int)window.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Paths.DataDir);
        var output = Path.Combine(Paths.DataDir, "ui-preview.png");
        using (var stream = File.Create(output)) encoder.Save(stream);
        window.Close();
        Console.WriteLine(output);
        return File.Exists(output) && new FileInfo(output).Length > 10_000 ? 0 : 1;
    }
}

public static class PreviewData
{
    public static List<NodeScore> Scores() =>
    [
        Score("v2rayN", "示例订阅 A", "香港 W05 | IEPL", 96, 88, 92, .99, 82, 12.4, 36, 7, true),
        Score("v2rayN", "示例订阅 A", "日本 W01 | IEPL", 87, 81, 85, .96, 165, 8.1, 32, 7, true),
        Score("Clash / Mihomo", "机场备用", "新加坡 Premium 02", 75, 93, 82, .92, 238, 18.6, 28, 5, true),
        Score("Clash / Mihomo", "机场备用", "美国 Los Angeles", 61, 78, 67, .86, 520, 6.7, 24, 3, true),
        Score("v2rayN", "订阅 1", "台湾 W02", 42, 54, 48, .63, 880, 2.2, 12, 0, false),
        Score("v2rayN", "订阅 1", "暂未测试节点", 0, 0, 0, 0, 0, 0, 0, 0, false)
    ];

    private static NodeScore Score(string client, string subscription, string name, double stable, double speedScore,
        double combined, double success, double delay, double speedMiB, int samples, int speedSamples, bool enough) =>
        new()
        {
            NodeId = $"{client}:{name}", ClientName = client, ClientId = client, Subscription = subscription, Name = name,
            Samples = samples, SpeedSamples = speedSamples, SuccessRate = success, MedianDelay = delay,
            RecentSuccessRate = success, RecentMedianDelay = delay, RecentP95Delay = delay > 0 ? delay * 1.2 : 0,
            MedianSpeed = speedMiB * 1024 * 1024, StabilityScore = stable, SpeedScore = speedScore,
            DelayScore = delay > 0 ? 100 * Math.Exp(-delay / 500) : 0, CombinedScore = combined,
            Confidence = enough ? 1 : samples / 24d, RecentFailures = success < .7 ? 3 : 0
        };
}
