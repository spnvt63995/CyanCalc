using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CyanGraph.Data;

namespace CyanGraph;

sealed class ConnectionWindow : Window
{
    readonly QueryWorkspace workspace;
    readonly TextBox address = new(), database = new(), username = new(), password = new() { PasswordChar = '●' };
    readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Foreground = CyanGraphView.Muted, MaxLines = 4 };
    readonly Button submit = CyanGraphView.Primary("Connect");
    bool pending, dismissed;
    public string Address => address.Text?.Trim() ?? "";
    public string Database => database.Text?.Trim() ?? "";
    public string Username => username.Text?.Trim() ?? "";

    public ConnectionWindow(QueryWorkspace workspace, string uri, string db, string user)
    {
        this.workspace = workspace;
        Title = "Connect to Neo4j"; Width = 490; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        address.Text = uri; database.Text = db; username.Text = user;
        var form = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        form.Children.Add(new TextBlock { Text = "Connection settings", FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = CyanGraphView.Ink });
        form.Children.Add(Field("Connection URI", address));
        form.Children.Add(Field("Database", database));
        form.Children.Add(Field("Username", username));
        form.Children.Add(Field("Password", password));
        form.Children.Add(feedback);
        var cancel = new Button { Content = "Cancel" };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(cancel); actions.Children.Add(submit); form.Children.Add(actions);
        Content = form;
        submit.Click += async (_, _) => await Connect();
        cancel.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await Connect(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            dismissed = true; password.Text = "";
            if (pending) _ = workspace.DisconnectAsync();
        };
        Opened += (_, _) => address.Focus();
    }

    static Control Field(string label, TextBox box)
    {
        AutomationProperties.SetName(box, label);
        var group = new StackPanel { Spacing = 4 };
        group.Children.Add(CyanGraphView.Caption(label)); group.Children.Add(box);
        return group;
    }

    async Task Connect()
    {
        if (pending) return;
        pending = true; submit.IsEnabled = false;
        address.IsEnabled = database.IsEnabled = username.IsEnabled = password.IsEnabled = false;
        feedback.Text = "Connecting…";
        await workspace.ConnectAsync(Address, Database, Username, password.Text ?? "");
        pending = false;
        if (dismissed) return;
        if (workspace.Connected != null) { Close(); return; }
        submit.IsEnabled = true;
        address.IsEnabled = database.IsEnabled = username.IsEnabled = password.IsEnabled = true;
        feedback.Foreground = Brush.Parse("#A2382B");
        feedback.Text = workspace.Status;
    }
}
