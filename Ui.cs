namespace ToolManager;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class Ui
{
    public static ImageSource AppIcon { get; } = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/ToolManager.ico"));

    public static System.Drawing.Icon TrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ToolManager.ico")).Stream;
        using var icon = new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
        return (System.Drawing.Icon)icon.Clone();
    }

    public static Button Button(string text, Action click)
    {
        var b = new Button
        {
            Content = text,
            Margin = new Thickness(3),
            Padding = new Thickness(10, 5, 10, 5),
            MinWidth = 65,
        };
        b.Click += (_, _) => click();
        return b;
    }

    public static TextBlock Text(string text, double size = 14) => new()
    {
        Text = text,
        FontSize = size,
        Margin = new Thickness(4),
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBox Input(string value, bool multiline = false) => new()
    {
        Text = value,
        Margin = new Thickness(3),
        MinWidth = 200,
        Padding = new Thickness(5),
        AcceptsReturn = multiline,
        AcceptsTab = multiline,
        Height = multiline ? 125 : double.NaN,
        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
        FontFamily = multiline ? new FontFamily("Consolas") : SystemFonts.MessageFontFamily,
    };

    public static void Field(Panel panel, string label, UIElement control)
    {
        System.Windows.Automation.AutomationProperties.SetName(control, label);
        panel.Children.Add(Text(label));
        panel.Children.Add(control);
    }

    public static void ConsoleTextBox(TextBox box)
    {
        box.Background = Brushes.Black;
        box.Foreground = Brushes.Gainsboro;
        box.CaretBrush = Brushes.White;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Black);
        border.SetValue(Border.BorderBrushProperty, Brushes.DimGray);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        content.SetValue(ScrollViewer.BackgroundProperty, Brushes.Black);
        border.AppendChild(content);
        box.Template = new ControlTemplate(typeof(TextBox)) { VisualTree = border };
    }

    public static void Error(Exception e) => MessageBox.Show(e.Message, "Tool Manager", MessageBoxButton.OK, MessageBoxImage.Error);
}
