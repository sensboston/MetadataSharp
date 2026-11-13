using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

internal sealed class ViewerApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MetadataWindow(Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)));
        base.OnFrameworkInitializationCompleted();
    }
}
