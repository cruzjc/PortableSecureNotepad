namespace PortableSecureNotepad.Ui;

internal static class AppIcon
{
    // Share one icon for the process lifetime; forms must not dispose it individually.
    private static readonly Lazy<Icon> Shared = new(Load);

    public static Icon Value => Shared.Value;

    private static Icon Load()
    {
        using Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream("PortableSecureNotepad.AppIcon.ico")
            ?? throw new InvalidOperationException("The application icon resource is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
