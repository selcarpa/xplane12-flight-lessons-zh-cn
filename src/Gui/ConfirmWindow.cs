using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace XpLessonZhCn.Gui;

/// <summary>确认 / 提示对话框。带可选复选框（用于「未验证版本」的二次确认）。</summary>
internal sealed class ConfirmWindow : Window
{
    private readonly CheckBox? _checkbox;
    private bool _confirmed;

    private ConfirmWindow(
        string title,
        string message,
        string? checkText,
        bool infoOnly,
        string okText = "继续",
        string cancelText = "取消")
    {
        Title = title;
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = UiFonts.Main;

        var root = new StackPanel { Margin = new Thickness(20), Spacing = 14 };

        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
        });

        if (!string.IsNullOrEmpty(checkText))
        {
            _checkbox = new CheckBox { Content = checkText, Margin = new Thickness(0, 4, 0, 0) };
            root.Children.Add(_checkbox);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        if (infoOnly)
        {
            var know = new Button { Content = "知道了", MinWidth = 96 };
            know.Click += (_, _) =>
            {
                _confirmed = true;
                Close(true);
            };
            buttons.Children.Add(know);
        }
        else
        {
            var cancel = new Button { Content = cancelText, MinWidth = 88 };
            cancel.Click += (_, _) => Close(false);

            var ok = new Button { Content = okText, MinWidth = 88 };
            ok.Click += (_, _) =>
            {
                if (_checkbox is not null && _checkbox.IsChecked != true)
                {
                    _checkbox.Foreground = Brushes.OrangeRed;
                    return; // 必须勾选才能继续（二次确认）
                }
                _confirmed = true;
                Close(true);
            };

            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
        }

        root.Children.Add(buttons);
        Content = root;
    }

    public static async Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        string? checkText = null,
        string okText = "继续",
        string cancelText = "取消")
    {
        var window = new ConfirmWindow(title, message, checkText, infoOnly: false, okText, cancelText);
        bool result = await window.ShowDialog<bool>(owner);
        return result && window._confirmed;
    }

    /// <summary>纯信息提示（只有一个「知道了」按钮）。</summary>
    public static async Task ShowInfoAsync(Window owner, string title, string message)
    {
        var window = new ConfirmWindow(title, message, null, infoOnly: true);
        await window.ShowDialog<bool>(owner);
    }
}
