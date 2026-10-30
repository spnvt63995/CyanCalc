using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;

namespace CyanCalc.Controls;

// Modal/modal-dialog.css, Button/button.css and Input/input.css at the pinned upstream.
// CSS widths exclude the two 1px borders; Avalonia Width includes them.
internal static class WorkbookDialog
{
    internal static readonly FontFamily Font = new("avares://CyanCalc.Avalonia/Resources/Fonts#Inter");

    internal static void Configure(Window window, LocalizedText title, LocalizedText closeLabel,
        Control body, Control? footer, double contentWidth = 280, bool import = false)
    {
        title.Bind(window, Window.TitleProperty);
        window.Width = contentWidth + 2;
        window.SizeToContent = SizeToContent.Height;
        window.CanResize = false;
        window.ShowInTaskbar = false;
        window.SystemDecorations = SystemDecorations.None;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        window.Background = Brushes.Transparent;
        window.FontFamily = Font;
        window.FontSize = 12;
        Attach(window);
        var header = new Grid { ColumnDefinitions = new("*,Auto"), Height = 44, Margin = new Thickness(12, 0) };
        var heading = title.Bind(new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, TextBlock.TextProperty);
        header.Children.Add(heading);
        var close = Button(closeLabel, window.Close, "ghost");
        close.Name = "DialogClose";
        close.Content = new WorkbookIcon("X");
        close.Width = close.Height = 28;
        close.Padding = new Thickness(0);
        Grid.SetColumn(close, 1); header.Children.Add(close);
        var content = new StackPanel();
        content.Children.Add(header); content.Children.Add(body);
        if (footer is not null) content.Children.Add(footer);
        var frame = new Border { Name = "DialogFrame", CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = content };
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Dialog-" + (import ? "common-white" : "surface-elevated")));
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-" + (import ? "grey-300" : "surface-elevated-border")));
        window.Content = frame;
        // The web modal remains centered as a tab or translated message changes its height.
        window.SizeChanged += (_, _) =>
        {
            if (!window.IsVisible || window.Owner is not Window owner) return;
            var ownerSize = owner.FrameSize ?? owner.ClientSize;
            var size = window.FrameSize ?? window.ClientSize;
            window.Position = owner.Position + new PixelPoint(
                (int)Math.Round((ownerSize.Width - size.Width) * owner.DesktopScaling / 2),
                (int)Math.Round((ownerSize.Height - size.Height) * owner.DesktopScaling / 2));
        };
    }

    internal static void Attach(Control root)
    {
        root.Resources["TabControlTopPlacementItemMargin"] = new Thickness(0);
        root.Resources["DialogOptionTemplate"] = new FuncControlTemplate<ComboBoxItem>((item, _) =>
        {
            var row = new Grid { ColumnDefinitions = new("16,8,*") };
            var check = new WorkbookIcon("Check"); check.Bind(Control.IsVisibleProperty, item.GetObservable(ComboBoxItem.IsSelectedProperty));
            row.Children.Add(check);
            var content = new ContentPresenter { VerticalContentAlignment = VerticalAlignment.Center };
            content.Bind(ContentPresenter.ContentProperty, item.GetObservable(ContentControl.ContentProperty));
            content.Bind(ContentPresenter.ContentTemplateProperty, item.GetObservable(ContentControl.ContentTemplateProperty));
            Grid.SetColumn(content, 2); row.Children.Add(content);
            var border = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 8), Child = row };
            border.Bind(Border.BackgroundProperty, item.GetObservable(ComboBoxItem.BackgroundProperty));
            return border;
        });
        root.Styles.Add(new StyleInclude(new Uri("avares://CyanCalc.Avalonia/"))
            { Source = new Uri("avares://CyanCalc.Avalonia/Workbook/WorkbookDialogStyles.axaml") });
        void Palette()
        {
            var palette = SheetPalette.For(root.ActualThemeVariant);
            foreach (var token in new[] { "common-black", "common-white", "grey-50", "grey-100", "grey-200", "grey-300", "grey-400", "grey-500", "grey-600", "grey-700", "grey-800", "grey-900",
                "primary-main", "primary-dark", "primary-light", "primary-contrast-text", "error-main", "error-dark", "error-contrast-text", "surface-elevated", "surface-elevated-border", "input-background", "input-border", "sheet-default-text-color", "text-selection-background" })
                root.Resources["Dialog-" + token] = palette.Token(token);
            Avalonia.Controls.Documents.TextElement.SetForeground(root, palette.Token("grey-900"));
            root.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        }
        Palette();
        root.ActualThemeVariantChanged += (_, _) => Palette();
    }

    internal static StackPanel Body() => new() { Spacing = 8, Margin = new Thickness(12) };
    internal static StackPanel Footer(params Control[] controls)
    {
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Margin = new Thickness(12, 8) };
        foreach (var control in controls) footer.Children.Add(control);
        return footer;
    }
    internal static Button Button(LocalizedText label, Action action, string variant = "secondary", string? icon = null, double iconSize = 14)
    {
        var button = new WorkbookButton(); StyleButton(button, label, variant, icon, iconSize);
        button.Click += (_, _) => action(); return button;
    }
    internal static void StyleButton(Button button, LocalizedText label, string variant = "secondary", string? icon = null, double iconSize = 14)
    {
        button.Classes.Add("dialog-button"); button.Classes.Add(variant); label.Bind(button, AutomationProperties.NameProperty);
        if (icon is null) label.Bind(button, ContentControl.ContentProperty);
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Border { Width = 16, Height = 16, Child = new WorkbookIcon(icon, iconSize) });
            var text = label.Block(); text.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(text); button.Content = row;
        }
    }
    internal static void Input(TextBox input, LocalizedText label)
    {
        input.Classes.Add("dialog-input"); label.Bind(input, AutomationProperties.NameProperty);
    }
    internal static TextBlock Paragraph(LocalizedText text) => text.Bind(new TextBlock
        { FontSize = 12, TextWrapping = TextWrapping.Wrap }, TextBlock.TextProperty);
}
