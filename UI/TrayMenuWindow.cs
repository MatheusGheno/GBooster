using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace GBooster.UI
{
    /// <summary>Estado do pontinho de um item do menu.</summary>
    public enum MenuDot { None, Active, Idle, Off }

    /// <summary>
    /// Menu da bandeja desenhado pelo próprio app (mesmo visual do resto: cantos arredondados, cores do tema).
    /// Substitui o menu padrão do Windows, que não aceita estilo.
    /// </summary>
    public sealed class TrayMenuWindow : Window
    {
        private readonly StackPanel _panel = new StackPanel();
        private bool _closing;

        public TrayMenuWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "GBooster";

            var box = new Border
            {
                Width = 290,
                Margin = new Thickness(12),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.45, Color = Colors.Black },
                Child = _panel
            };
            box.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            box.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderDefault");
            Content = box;

            Deactivated += (s, e) => CloseMenu();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) CloseMenu(); };
        }

        public void CloseMenu()
        {
            if (_closing) return;
            _closing = true;
            try { Close(); } catch { }
        }

        // ---------------------------------------------------------------- construção
        public void AddHeader(string status, bool active)
        {
            var logo = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold };
            logo.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Display");
            logo.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            logo.Inlines.Add(new System.Windows.Documents.Run("G"));
            var boost = new System.Windows.Documents.Run("BOOSTER");
            boost.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.LogoAccent");
            logo.Inlines.Add(boost);

            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            line.Children.Add(MakeDot(active ? MenuDot.Active : MenuDot.Idle));
            var st = new TextBlock { Text = status, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            st.SetResourceReference(TextBlock.ForegroundProperty, active ? "Brush.StateActive" : "Brush.TextSecondary");
            line.Children.Add(st);

            var head = new StackPanel { Margin = new Thickness(10, 8, 10, 6) };
            head.Children.Add(logo);
            head.Children.Add(line);
            _panel.Children.Add(head);
        }

        public void AddSeparator()
        {
            var sep = new Border { Height = 1, Margin = new Thickness(4, 6, 4, 6) };
            sep.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSubtle");
            _panel.Children.Add(sep);
        }

        public void AddTitle(string text)
        {
            var t = new TextBlock { Text = text.ToUpperInvariant(), Margin = new Thickness(10, 4, 10, 2) };
            t.SetResourceReference(FrameworkElement.StyleProperty, "Label");
            _panel.Children.Add(t);
        }

        public void AddInfo(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(10, 6, 10, 6) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
            _panel.Children.Add(t);
        }

        public void AddItem(string text, Action onClick, string? key = null, MenuDot dot = MenuDot.None, bool enabled = true)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (dot != MenuDot.None)
            {
                FrameworkElement d = MakeDot(dot);
                d.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(d, 0);
                grid.Children.Add(d);
            }
            var label = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            if (!string.IsNullOrEmpty(key))
            {
                var k = new TextBlock { Text = key, FontSize = 11, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                k.SetResourceReference(FrameworkElement.StyleProperty, "Mono");
                k.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
                Grid.SetColumn(k, 2);
                grid.Children.Add(k);
            }

            var btn = new Button { Content = grid, IsEnabled = enabled };
            btn.SetResourceReference(FrameworkElement.StyleProperty, "TrayItem");
            btn.Click += (s, e) =>
            {
                CloseMenu();
                System.Windows.Application.Current.Dispatcher.BeginInvoke(onClick);
            };
            _panel.Children.Add(btn);
        }

        private static FrameworkElement MakeDot(MenuDot kind)
        {
            var e = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
            switch (kind)
            {
                case MenuDot.Active:
                    e.SetResourceReference(Shape.FillProperty, "Brush.StateActive");
                    break;
                case MenuDot.Off:
                    e.Fill = Brushes.Transparent;
                    e.StrokeThickness = 1.6;
                    e.SetResourceReference(Shape.StrokeProperty, "Brush.StateIdle");
                    break;
                default:
                    e.SetResourceReference(Shape.FillProperty, "Brush.StateIdle");
                    break;
            }
            return e;
        }

        // ---------------------------------------------------------------- posição
        /// <summary>Mostra o menu perto do cursor (pixels da tela), sempre dentro da área útil do monitor.</summary>
        public void ShowAt(System.Drawing.Point cursorPx)
        {
            Opacity = 0;
            Show();
            UpdateLayout();

            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            System.Drawing.Rectangle wa = System.Windows.Forms.Screen.FromPoint(cursorPx).WorkingArea;
            double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
            double cx = cursorPx.X / sx, cy = cursorPx.Y / sy;
            double l = wa.Left / sx, t = wa.Top / sy, r = wa.Right / sx, b = wa.Bottom / sy;

            double left = cx - ActualWidth + 12;
            double top = cy - ActualHeight + 12;
            if (top < t) top = Math.Min(cy - 12, b - ActualHeight);
            left = Math.Max(l, Math.Min(left, r - ActualWidth));
            top = Math.Max(t, Math.Min(top, b - ActualHeight));
            Left = left;
            Top = top;

            Opacity = 1;
            Activate();
            Focus();
        }
    }
}
