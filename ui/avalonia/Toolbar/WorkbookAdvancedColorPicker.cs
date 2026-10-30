using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyanCalc.Controls;

// AdvancedColorPicker.tsx / react-colorful: saturation/value, hue, HEX,
// preview, Cancel and Apply. Editing the controls only changes the draft.
internal sealed class WorkbookAdvancedColorPicker : StackPanel
{
    private readonly ColorPlane _saturation;
    private readonly ColorPlane _hue;
    private readonly Border _preview;
    private bool _syncing;
    private double _h, _s, _v;
    internal TextBox HexInput { get; }
    internal string SelectedHex { get; private set; } = "#272525";
    internal Control[] FocusStops { get; }

    internal WorkbookAdvancedColorPicker(Action<string> accept, Action cancel, WorkbookText? text = null)
    {
        text ??= new WorkbookText();
        Width = 240; Spacing = 0;
        _saturation = new ColorPlane(false, Change, text) { Height = 136 };
        _hue = new ColorPlane(true, Change, text) { Height = 8, Margin = new Thickness(8) };
        Children.Add(_saturation); Children.Add(_hue);
        Children.Add(WorkbookMenus.Divider());
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(8) };
        row.Children.Add(new TextBlock { Text = "Hex", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        HexInput = new TextBox { MaxLength = 7, Height = 28, MinHeight = 0, MinWidth = 0, FontSize = 12,
            Padding = new Thickness(2, 0, 10, 0), TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
        HexInput.Classes.Add("picker-hex");
        text.Key("native.hex_color").Describe(HexInput);
        var hexBox = new Grid { ColumnDefinitions = new("Auto,*") };
        hexBox.Children.Add(new TextBlock { Text = "#", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(HexInput, 1); hexBox.Children.Add(HexInput);
        var frame = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Child = hexBox };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Menu-grey-300"));
        void InputFocus()
        {
            var palette = SheetPalette.For(ActualThemeVariant);
            var background = ((ISolidColorBrush)palette.Token("surface-elevated")).Color;
            var accent = ((ISolidColorBrush)palette.Token("primary-main")).Color;
            frame.BoxShadow = HexInput.IsKeyboardFocusWithin
                ? BoxShadows.Parse($"0 0 0 1 {background}, 0 0 0 3 {accent}") : default;
        }
        HexInput.PropertyChanged += (_, e) => { if (e.Property == IsKeyboardFocusWithinProperty) InputFocus(); };
        ActualThemeVariantChanged += (_, _) => InputFocus();
        Grid.SetColumn(frame, 1); row.Children.Add(frame);
        _preview = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)), BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(_preview, 2); row.Children.Add(_preview); Children.Add(row);
        Children.Add(WorkbookMenus.Divider());
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(8) };
        var cancelButton = new WorkbookButton {  HorizontalAlignment = HorizontalAlignment.Left };
        var apply = new WorkbookButton {  HorizontalAlignment = HorizontalAlignment.Right };
        text.Key("color_picker.cancel").Bind(cancelButton, ContentControl.ContentProperty).Name = "CancelAdvancedColor";
        text.Key("color_picker.cancel").Describe(cancelButton);
        text.Key("color_picker.apply").Bind(apply, ContentControl.ContentProperty).Name = "ApplyColor";
        text.Key("color_picker.apply").Describe(apply);
        cancelButton.Classes.Add("picker-action"); apply.Classes.Add("picker-action"); apply.Classes.Add("primary");
        var applyContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        applyContent.Children.Add(new WorkbookIcon("Check")); applyContent.Children.Add(text.Key("color_picker.apply").Block());
        apply.Content = applyContent;
        cancelButton.Click += (_, _) => cancel();
        apply.Click += (_, _) => accept(SelectedHex);
        buttons.Children.Add(cancelButton); Grid.SetColumn(apply, 1); buttons.Children.Add(apply); Children.Add(buttons);
        FocusStops = [_saturation, _hue, HexInput, cancelButton, apply];
        HexInput.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || _syncing) return;
            // HexColorInput filters non-hex characters, accepts three or six
            // digits and retains the last valid color while the input is partial.
            var digits = new string((HexInput.Text ?? "").Where(Uri.IsHexDigit).Take(6).ToArray());
            _syncing = true; HexInput.Text = digits.ToUpperInvariant(); _syncing = false;
            if (digits.Length is 3 or 6) SetColor("#" + digits, false);
        };
        Reset(SelectedHex);
    }

    internal void Reset(string hex) => SetColor(hex, true);

    private void SetColor(string hex, bool updateInput)
    {
        var color = Color.Parse(hex);
        SelectedHex = hex;
        double r = color.R, g = color.G, b = color.B;
        var max = Math.Max(r, Math.Max(g, b)); var delta = max - Math.Min(r, Math.Min(g, b));
        var hh = delta == 0 ? 0 : max == r ? (g - b) / delta : max == g ? 2 + (b - r) / delta : 4 + (r - g) / delta;
        _h = 60 * (hh < 0 ? hh + 6 : hh);
        _s = max == 0 ? 0 : delta / max * 100; _v = max / 255 * 100;
        Sync(updateInput);
    }

    private void Change(bool hue, double x, double y)
    {
        if (hue) _h = x;
        else { _s = x; _v = y; }
        var color = FromHsv(_h, _s, _v);
        SelectedHex = $"#{color.R:x2}{color.G:x2}{color.B:x2}";
        Sync(true);
    }

    private void Sync(bool updateInput)
    {
        _saturation.Update(_h, _s, _v); _hue.Update(_h, _s, _v);
        _preview.Background = new SolidColorBrush(Color.Parse(SelectedHex));
        if (updateInput) { _syncing = true; HexInput.Text = SelectedHex.TrimStart('#').ToUpperInvariant(); _syncing = false; }
    }

    internal static Color FromHsv(double h, double s, double v)
    {
        // react-colorful utils/convert.ts: keep operation order and percentages,
        // since an equivalent HSV formula can round one RGB channel differently.
        h = h / 360 * 6; s /= 100; v /= 100;
        var hh = Math.Floor(h); var b = v * (1 - s);
        var c = v * (1 - (h - hh) * s); var d = v * (1 - (1 - h + hh) * s);
        var module = (int)hh % 6;
        byte Channel(double value) => (byte)Math.Clamp(Math.Floor(value * 255 + .5), 0, 255);
        return Color.FromRgb(Channel(new[] { v, c, b, b, d, v }[module]),
            Channel(new[] { d, v, v, c, b, b }[module]), Channel(new[] { b, b, d, v, v, c }[module]));
    }

    private sealed class ColorPlane : Control
    {
        private readonly bool _isHue;
        private readonly Action<bool, double, double> _change;
        private double _h, _s, _v;
        private IPointer? _pointer;
        internal ColorPlane(bool hue, Action<bool, double, double> change, WorkbookText text)
        {
            _isHue = hue; _change = change; Focusable = IsTabStop = true;
            text.Key(hue ? "native.hue" : "native.saturation_value").Describe(this);
        }
        internal void Update(double h, double s, double v) { _h = h; _s = s; _v = v; InvalidateVisual(); }
        public override void Render(DrawingContext context)
        {
            var rect = new Rect(Bounds.Size);
            if (_isHue)
            {
                var gradient = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
                for (var i = 0; i <= 6; i++) gradient.GradientStops.Add(new GradientStop(FromHsv(i * 60, 100, 100), i / 6d));
                context.DrawRectangle(gradient, null, rect, 5, 5);
            }
            else
            {
                using var clip = context.PushClip(new RoundedRect(rect, new CornerRadius(8, 8, 0, 0)));
                context.FillRectangle(new SolidColorBrush(FromHsv(_h, 100, 100)), rect);
                context.FillRectangle(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = new GradientStops { new(Colors.White, 0), new(Colors.Transparent, 1) }
                }, rect);
                context.FillRectangle(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = new GradientStops { new(Colors.Transparent, 0), new(Colors.Black, 1) }
                }, rect);
            }
            var center = new Point((_isHue ? _h / 360 : _s / 100) * Bounds.Width, (_isHue ? .5 : 1 - _v / 100) * Bounds.Height);
            context.DrawEllipse(new SolidColorBrush(FromHsv(_h, _isHue ? 100 : _s, _isHue ? 100 : _v)), new Pen(Brushes.White, 2), center, _isHue ? 8 : 7, _isHue ? 8 : 7);
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus(); _pointer = e.Pointer; _pointer.Capture(this); Pick(e.GetPosition(this)); e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e) { if (ReferenceEquals(_pointer, e.Pointer)) Pick(e.GetPosition(this)); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e) { if (ReferenceEquals(_pointer, e.Pointer)) { _pointer.Capture(null); _pointer = null; e.Handled = true; } }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { _pointer = null; base.OnPointerCaptureLost(e); }
        private void Pick(Point point) => _change(_isHue, Math.Clamp(point.X / Math.Max(1, Bounds.Width), 0, 1) * (_isHue ? 360 : 100), 100 - Math.Clamp(point.Y / Math.Max(1, Bounds.Height), 0, 1) * 100);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
            var x = _isHue ? _h : _s; var y = _v;
            if (e.Key == Key.Left) x -= _isHue ? 18 : 5;
            if (e.Key == Key.Right) x += _isHue ? 18 : 5;
            if (e.Key == Key.Up && !_isHue) y += 5;
            if (e.Key == Key.Down && !_isHue) y -= 5;
            _change(_isHue, Math.Clamp(x, 0, _isHue ? 360 : 100), Math.Clamp(y, 0, 100)); e.Handled = true;
        }
    }
}
