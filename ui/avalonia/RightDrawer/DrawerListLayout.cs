using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    // Keep the filter/lifetime panel attached, and give the list's empty state
    // the remaining viewport height instead of a StackPanel's desired height.
    private ContentControl SetDrawerListBody(StackPanel panel, StackPanel list)
    {
        var layout = (Grid)_drawer.Child!;
        var original = layout.Children.OfType<ScrollViewer>().Single(scroll => ReferenceEquals(scroll.Content, panel));
        panel.Children.Remove(list);
        var inset = panel.Margin; panel.Margin = default;
        foreach (var child in panel.Children) child.Margin = inset;
        list.Margin = inset;
        original.Content = null; layout.Children.Remove(original);
        var body = new Grid { RowDefinitions = new("Auto,*") };
        body.Children.Add(panel);
        var listBody = new Grid(); Grid.SetRow(listBody, 1); body.Children.Add(listBody);
        listBody.Children.Add(new DrawerScrollViewer { Content = list });
        var empty = new ContentControl
        {
            Name = "DrawerEmptyState", IsVisible = false, Margin = new Thickness(20, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center
        };
        listBody.Children.Add(empty);
        Grid.SetRow(body, 1); layout.Children.Add(body); return empty;
    }
    private Control DrawerEmptyMessage(string icon, params string[] keys)
    {
        var content = new StackPanel { Spacing = 8 };
        var glyph = new WorkbookIcon(icon, 16) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.Bind(WorkbookIcon.ForegroundProperty, glyph.GetResourceObservable("Dialog-grey-600"));
        var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center, Child = glyph };
        badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable("Dialog-grey-100")); content.Children.Add(badge);
        foreach (var key in keys)
        {
            var message = Label(Text.Key(key)); message.FontSize = 12; message.LineHeight = 18; message.TextAlignment = TextAlignment.Center;
            message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable("Dialog-grey-600")); content.Children.Add(message);
        }
        return content;
    }
}
