using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

// Configure and start the desktop sample with its platform-specific application lifecycle.
internal sealed class ViewerApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    // Keep OnFrameworkInitializationCompleted focused on desktop viewer and its format-specific validation contract.
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MetadataWindow(Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)));
        base.OnFrameworkInitializationCompleted();
    }
}
