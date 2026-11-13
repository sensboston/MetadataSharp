using Avalonia;

// Represent the Program state used by desktop viewer parsing and output.
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<ViewerApp>().UsePlatformDetect().LogToTrace();
}
