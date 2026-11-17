using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Operations;
using MetadataSharp.Formats;

// Present metadata editing and preview actions for the selected image in the desktop sample.
internal sealed class MetadataWindow : Window
{
    private readonly TextBox search = new() { Watermark = "Search keys and values…", MinWidth = 220 };
    private readonly TreeView tree = new() { MinWidth = 340 };
    private readonly TextBlock pathLabel = new() { Text = "Drop an image here or choose Open", Foreground = Brushes.SlateGray };
    private readonly TextBox details = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150 };
    private readonly TextBlock status = new() { Text = "Read-only metadata viewer", Foreground = Brushes.SlateGray };
    private readonly List<Row> rows = [];

    // Capture validated inputs and stable state for this desktop viewer object.
    public MetadataWindow(string? initialPath = null)
    {
        Title = "MetadataSharp Viewer";
        Width = 1080;
        Height = 720;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.Parse("#F5F7FA"));
        tree.SelectionChanged += (_, _) => ShowSelection();
        search.TextChanged += (_, _) => BuildTree();
        var open = new Button { Content = "Open file", Padding = new Thickness(14, 8) };
        open.Click += async (_, _) => await OpenPicker();
        var copy = new Button { Content = "Copy value", Padding = new Thickness(14, 8) };
        copy.Click += async (_, _) =>
        {
            if (tree.SelectedItem is TreeViewItem { Tag: Row row } && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(row.Raw ?? row.Interpreted ?? row.Label);
        };
        var top = new DockPanel { LastChildFill = true, Margin = new Thickness(18, 14) };
        DockPanel.SetDock(open, Dock.Left);
        DockPanel.SetDock(copy, Dock.Right);
        DockPanel.SetDock(search, Dock.Right);
        top.Children.Add(open);
        top.Children.Add(copy);
        top.Children.Add(search);
        top.Children.Add(pathLabel);

        var left = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.Parse("#E1E6ED")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Child = tree };
        var rightStack = new StackPanel { Spacing = 12 };
        rightStack.Children.Add(new TextBlock { Text = "Metadata", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#172B4D")) });
        rightStack.Children.Add(details);
        var right = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.Parse("#E1E6ED")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Child = rightStack };
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*"), Margin = new Thickness(18, 0, 18, 12) };
        split.Children.Add(left);
        Grid.SetColumn(right, 1);
        split.Children.Add(right);
        var footer = new Border { Padding = new Thickness(18, 8), Child = status };
        var layout = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(top); layout.Children.Add(footer); layout.Children.Add(split);
        Content = layout;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        if (initialPath is not null) Opened += async (_, _) => await LoadFile(initialPath);
    }

    // Select an image file and load it through the shared metadata document API.
    private async Task OpenPicker()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open image or sidecar",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Metadata files") { Patterns = ["*.*"] }]
        });
        if (files.Count != 0) await LoadFile(files[0].Path.LocalPath);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        if (file is not null) await LoadFile(file.Path.LocalPath);
    }

    // Keep LoadFile focused on desktop viewer and its format-specific validation contract.
    private async Task LoadFile(string path)
    {
        try
        {
            ImageMetadata metadata = await ImageMetadata.ReadAsync(path);
            rows.Clear();
            if (metadata.Exif is { } exif)
            {
                foreach (ExifEntry entry in exif.Entries)
                {
                    string key = MetadataKeys.Format(entry.Key);
                    rows.Add(new("EXIF", entry.Key.Directory.ToString(), key, SampleSupport.RawText(entry.Value), ExifTags.Interpret(exif, entry.Key)));
                }
                if (exif.MakerNote is { } note)
                    foreach (MakerNoteEntry entry in note.Entries)
                    {
                        string key = $"Exif.{entry.Name ?? $"{note.Format}"}.{entry.Name ?? $"0x{entry.TagId:x4}"}";
                        rows.Add(new("EXIF / MakerNote", note.Format.ToString(), key, SampleSupport.RawText(entry.Value), entry.Fields.Count == 0 ? null : $"{entry.Fields.Count} interpreted field(s)"));
                    }
            }
            if (metadata.Iptc is { } iptc)
                foreach (IptcDataset dataset in iptc.Datasets)
                    rows.Add(new("IPTC", $"Record {dataset.Key.Record}", MetadataKeys.Format(dataset.Key) ?? $"Iptc.{dataset.Key.Record}.{dataset.Key.Dataset}", SampleSupport.IptcText(dataset), null));
            if (metadata.Xmp is { } xmp)
                foreach (var entry in xmp.Entries)
                    rows.Add(new("XMP", entry.Prefix, entry.Key, string.Join("; ", entry.Values), null));
            foreach (string comment in metadata.Comments) rows.Add(new("Comment", "Text", "Comment", comment, null));
            if (!metadata.IccProfile.IsEmpty) rows.Add(new("ICC", "Profile", "ICC profile", $"{metadata.IccProfile.Length} bytes", null));
            foreach (PreviewImage preview in metadata.Previews)
                rows.Add(new("Previews", preview.MimeType, preview.Source, $"{preview.Length} bytes at offset {preview.Offset}", $"{preview.Width} × {preview.Height}"));
            pathLabel.Text = path;
            status.Text = $"{metadata.Format} · {rows.Count} items · read-only" + (metadata.Errors.Count == 0 ? string.Empty : $" · {metadata.Errors.Count} unread family/families");
            BuildTree();
        }
        catch (Exception ex) when (ex is IOException or MetadataException or UnauthorizedAccessException)
        {
            status.Text = "Could not open file: " + ex.Message;
        }
    }

    // Keep BuildTree focused on desktop viewer and its format-specific validation contract.
    private void BuildTree()
    {
        string query = search.Text?.Trim() ?? string.Empty;
        tree.ItemsSource = rows.Where(r => query.Length == 0 || r.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Group.Contains(query, StringComparison.OrdinalIgnoreCase) || (r.Raw?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.Interpreted?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .GroupBy(r => r.Family, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(family =>
            {
                var root = new TreeViewItem { Header = family.Key, IsExpanded = true };
                foreach (var group in family.GroupBy(r => r.Group, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    var category = new TreeViewItem { Header = group.Key, IsExpanded = true };
                    foreach (Row row in group.OrderBy(r => r.Label, StringComparer.Ordinal))
                    {
                        var item = new TreeViewItem { Header = row.Label, Tag = row };
                        item.PointerPressed += (_, _) => Show(row);
                        category.Items.Add(item);
                    }
                    root.Items.Add(category);
                }
                return root;
            }).ToArray();
    }

    private void ShowSelection()
    {
        if (tree.SelectedItem is TreeViewItem { Tag: Row row }) Show(row);
    }

    private void Show(Row row) => details.Text = $"{row.Label}\n\nInterpreted\n{row.Interpreted ?? "(not available)"}\n\nRaw\n{row.Raw ?? "(no raw text)"}";

    private sealed record Row(string Family, string Group, string Label, string? Raw, string? Interpreted);
}
