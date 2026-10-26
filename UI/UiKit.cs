using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace GBooster.UI
{
    /// <summary>
    /// Pecinhas de tela montadas em código (cartões, botões, sanfona, tela de progresso e de resultado),
    /// com as cores e estilos do tema. As páginas Desempenho, Limpeza e Meu sistema usam isto.
    /// </summary>
    internal static class UiKit
    {
        public static Style Res(string key) => (Style)System.Windows.Application.Current.FindResource(key);

        public static TextBlock Text(string text, double size = 13, string brush = "Brush.TextPrimary", bool semibold = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            if (semibold) t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        /// <summary>Texto auxiliar pequeno (cinza).</summary>
        public static TextBlock Hint(string text, double top = 0)
        {
            TextBlock t = Text(text, 12, "Brush.TextTertiary");
            t.LineHeight = 18;
            t.Margin = new Thickness(0, top, 0, 0);
            return t;
        }

        /// <summary>Título de seção em caixa alta.</summary>
        public static TextBlock Label(string text)
        {
            var t = new TextBlock { Text = text, Style = Res("Label") };
            return t;
        }

        public static TextBlock DisplayText(string text, double size, string brush)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Display");
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        public static Border Card(UIElement child, double top = 0)
        {
            return new Border { Style = Res("Card"), Child = child, Margin = new Thickness(0, top, 0, 0) };
        }

        public static Button Btn(string text, RoutedEventHandler click, string style = "BtnSm")
        {
            var b = new Button { Content = text, Style = Res(style) };
            b.Click += click;
            return b;
        }

        /// <summary>Botão de destaque (laranja).</summary>
        public static Button Primary(string text, RoutedEventHandler click, double height = 40, double padding = 24)
        {
            var b = new Button { Content = text, Style = Res("BtnPrimary"), Height = height, Padding = new Thickness(padding, 0, padding, 0), FontSize = 14 };
            b.Click += click;
            return b;
        }

        /// <summary>Botão comum (cinza), do tamanho dos campos.</summary>
        public static Button Plain(string text, RoutedEventHandler click, double height = 40, double padding = 22)
        {
            var b = new Button { Content = text, Style = Res("Btn"), Height = height, Padding = new Thickness(padding, 0, padding, 0), FontSize = 13 };
            b.Click += click;
            return b;
        }

        /// <summary>Botão com ícone à esquerda e texto; o ícone acompanha a cor do texto.</summary>
        public static Button IconBtn(string text, string data, RoutedEventHandler click, string style = "BtnSm")
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                Width = 16, Height = 16, Stretch = Stretch.None,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
            };
            icon.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1),
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Style = Res(style), Content = row };
            b.Click += click;
            return b;
        }

        public static Button Link(string text, RoutedEventHandler click)
        {
            var b = new Button { Style = Res("BtnLink"), Content = text, VerticalAlignment = VerticalAlignment.Center };
            b.Click += click;
            return b;
        }

        public static Ellipse Dot(double size, string brush)
        {
            var e = new Ellipse { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            e.SetResourceReference(Shape.FillProperty, brush);
            return e;
        }

        public static Border Divider(double top = 0)
        {
            var b = new Border { Height = 1, Margin = new Thickness(0, top, 0, 0), IsHitTestVisible = false };
            b.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSubtle");
            return b;
        }

        public static System.Windows.Shapes.Path Icon(string data, string brush, double thickness = 1.7)
        {
            var p = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 16, Height = 16,
                Stretch = Stretch.None,
                VerticalAlignment = VerticalAlignment.Center,
            };
            p.SetResourceReference(Shape.StrokeProperty, brush);
            return p;
        }

        public static System.Windows.Shapes.Path CheckIcon() => Icon("M3,8.5 L6.2,11.7 L13,4.8", "Brush.StateActive", 1.8);
        public static System.Windows.Shapes.Path WarnIcon() => Icon("M8,2.2 L14.6,13.5 H1.4 Z M8,6.5 V9.7 M8,11.7 H8.01", "Brush.Warn", 1.6);
        public static System.Windows.Shapes.Path CrossIcon() => Icon("M4,4 L12,12 M12,4 L4,12", "Brush.Bad", 1.8);


        /// <summary>Arco que gira sem parar; a cor acompanha o texto do botão em que está.</summary>
        public static System.Windows.Shapes.Path Spinner()
        {
            var rot = new RotateTransform(0, 8, 8);
            var p = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M8,2 A6,6 0 1 1 2.3,10"),
                Width = 16, Height = 16, Stretch = Stretch.None,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
                RenderTransform = rot,
            };
            p.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1),
            });
            rot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.9))) { RepeatBehavior = RepeatBehavior.Forever });
            return p;
        }

        /// <summary>Botão ocupado: ícone girando + texto. Não tem ação enquanto a tarefa roda.</summary>
        public static Button BusyBtn(string text, string style = "BtnSm")
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Spinner());
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return new Button { Style = Res(style), Content = row, Cursor = Cursors.Wait, Focusable = false };
        }

        /// <summary>Faixa amarela no topo da página: explica por que os botões estão desligados.</summary>
        public static Border Banner(string text, double bottom = 12)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            System.Windows.Shapes.Path ic = WarnIcon();
            ic.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(ic);
            row.Children.Add(Text(text, 13, "Brush.Warn"));
            Border b = WarnCard(row);
            b.Margin = new Thickness(0, 0, 0, bottom);
            return b;
        }

        /// <summary>Cartão no estilo "alerta" (amarelo).</summary>
        public static Border WarnCard(UIElement child, double top = 0)
        {
            Border b = Card(child, top);
            b.SetResourceReference(Border.BackgroundProperty, "Brush.WarnBg");
            b.SetResourceReference(Border.BorderBrushProperty, "Brush.WarnBorder");
            return b;
        }

        // ------------------------------------------------------------------
        // Sanfona (clica no título e abre/fecha)
        // ------------------------------------------------------------------
        public static Border Accordion(string title, string subtitle, UIElement content, bool open, Action<bool>? onToggle, double top = 0)
        {
            var chev = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M4,6 L8,10 L12,6"),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 16, Height = 16,
                Stretch = Stretch.None,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(open ? 180 : 0),
            };
            chev.SetResourceReference(Shape.StrokeProperty, "Brush.TextTertiary");

            var head = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(18, 14, 18, 14) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel();
            texts.Children.Add(Label(title));
            texts.Children.Add(Hint(subtitle, 3));
            head.Children.Add(texts);
            Grid.SetColumn(chev, 1);
            head.Children.Add(chev);

            var body = new Border
            {
                Padding = new Thickness(18, 0, 18, 8),
                Child = content,
                Visibility = open ? Visibility.Visible : Visibility.Collapsed,
            };

            var root = new StackPanel();
            root.Children.Add(head);
            root.Children.Add(body);

            head.MouseLeftButtonUp += (s, e) =>
            {
                bool nowOpen = body.Visibility != Visibility.Visible;
                body.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;
                ((RotateTransform)chev.RenderTransform).Angle = nowOpen ? 180 : 0;
                onToggle?.Invoke(nowOpen);
            };

            Border card = Card(root, top);
            card.Padding = new Thickness(0);
            return card;
        }

        // ------------------------------------------------------------------
        // Marca animada e telas de progresso / resultado
        // ------------------------------------------------------------------
        /// <summary>A marca do G. Girando = tela de carregamento (sem o raio); parada = G completo.</summary>
        public static BrandMark Mark(bool spinning, double size = 96)
        {
            var mark = new BrandMark { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
            if (!spinning)
            {
                mark.Reset();
                return mark;
            }

            mark.Set(0, 110, 0, 0, 0);
            if (!HeaderBrandAnimator.MotionAllowed) return mark;

            var sw = new Stopwatch();
            EventHandler? frame = null;
            mark.Loaded += (s, e) =>
            {
                if (frame != null) return;
                sw.Restart();
                frame = (a, b) => mark.Set(sw.Elapsed.TotalSeconds * 300.0 % 360.0, 110, 0, 0, 0);
                CompositionTarget.Rendering += frame;
            };
            mark.Unloaded += (s, e) =>
            {
                if (frame != null) CompositionTarget.Rendering -= frame;
                frame = null;
            };
            return mark;
        }

        /// <summary>Barra de progresso (0–100) com o texto do passo e a porcentagem.</summary>
        public sealed class ProgressBar
        {
            public StackPanel Root { get; }
            private readonly TextBlock _step;
            private readonly TextBlock _pct;
            private readonly ColumnDefinition _done;
            private readonly ColumnDefinition _rest;

            public ProgressBar(string step, int percent, string? fillBrush, string eta)
            {
                Root = new StackPanel { Width = 420, HorizontalAlignment = HorizontalAlignment.Center };
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _step = Hint(step);
                _step.TextTrimming = TextTrimming.CharacterEllipsis;
                _step.TextWrapping = TextWrapping.NoWrap;
                _pct = Text("0%", 12, "Brush.TextPrimary");
                _pct.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
                Grid.SetColumn(_pct, 1);
                line.Children.Add(_step);
                line.Children.Add(_pct);
                Root.Children.Add(line);

                var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true };
                track.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSubtle");
                var g = new Grid();
                _done = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
                _rest = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
                g.ColumnDefinitions.Add(_done);
                g.ColumnDefinitions.Add(_rest);
                var fill = new Border { CornerRadius = new CornerRadius(4) };
                if (fillBrush == null)
                {
                    fill.Background = new LinearGradientBrush(Color.FromRgb(0xff, 0xb0, 0x61), Color.FromRgb(0xff, 0x6a, 0x00), 0);
                }
                else
                {
                    fill.SetResourceReference(Border.BackgroundProperty, fillBrush);
                }
                g.Children.Add(fill);
                track.Child = g;
                Root.Children.Add(track);

                if (eta.Length > 0)
                {
                    TextBlock e = Hint(eta, 12);
                    e.TextAlignment = TextAlignment.Center;
                    Root.Children.Add(e);
                }
                Update(percent, step);
            }

            public void Update(int percent, string step)
            {
                int p = Math.Max(0, Math.Min(100, percent));
                _done.Width = new GridLength(Math.Max(p, 0.0001), GridUnitType.Star);
                _rest.Width = new GridLength(100 - p, GridUnitType.Star);
                _pct.Text = p + "%";
                _step.Text = step;
            }
        }

        /// <summary>Tela de carregamento: marca girando, título, frase, barra e tempo estimado. Tudo centralizado.</summary>
        public static Grid Loading(string title, string subtitle, string eta, out ProgressBar bar, bool showBar = true)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(Mark(true));
            TextBlock t = DisplayText(title, 20, "Brush.TextPrimary");
            t.TextAlignment = TextAlignment.Center;
            t.Margin = new Thickness(0, 26, 0, 0);
            stack.Children.Add(t);
            TextBlock s = Hint(subtitle, 26);
            s.TextAlignment = TextAlignment.Center;
            s.MaxWidth = 440;
            s.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(s);
            bar = new ProgressBar("", 0, null, eta);
            bar.Root.Margin = new Thickness(0, 26, 0, 0);
            bar.Root.Visibility = showBar ? Visibility.Visible : Visibility.Collapsed;
            stack.Children.Add(bar.Root);
            var host = new Grid { Margin = new Thickness(28, 0, 28, 24) };
            host.Children.Add(stack);
            return host;
        }

        /// <summary>Tela de resultado (sucesso ou interrompida): marca parada, título colorido, frase, barra cheia e botões.</summary>
        public static Grid Result(string title, string subtitle, int percent, string step, string colorBrush, params Button[] buttons)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(Mark(false));
            TextBlock t = DisplayText(title, 22, colorBrush);
            t.TextAlignment = TextAlignment.Center;
            t.Margin = new Thickness(0, 26, 0, 0);
            stack.Children.Add(t);
            TextBlock s = Hint(subtitle, 26);
            s.TextAlignment = TextAlignment.Center;
            s.MaxWidth = 460;
            s.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(s);
            var bar = new ProgressBar(step, percent, colorBrush, "");
            bar.Root.Margin = new Thickness(0, 26, 0, 0);
            stack.Children.Add(bar.Root);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 32, 0, 0) };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i > 0) buttons[i].Margin = new Thickness(10, 0, 0, 0);
                row.Children.Add(buttons[i]);
            }
            stack.Children.Add(row);
            var host = new Grid { Margin = new Thickness(28, 0, 28, 24) };
            host.Children.Add(stack);
            return host;
        }

        /// <summary>Linha "rótulo ... valor" das páginas de informação.</summary>
        public static Grid KeyValue(string key, string value, string? tag = null, string? tagTip = null, bool good = false, bool last = false)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock k = Hint(key);
            k.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(k);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            TextBlock v = Text(value, 13, good ? "Brush.StateActive" : "Brush.TextPrimary", good);
            v.TextAlignment = TextAlignment.Right;
            v.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(v);
            if (!string.IsNullOrEmpty(tag))
            {
                var chip = new Border
                {
                    Margin = new Thickness(10, 0, 0, 0),
                    Padding = new Thickness(6, 1, 6, 1),
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = Text(tag!, 10, "Brush.Warn", true),
                };
                chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Warn");
                if (!string.IsNullOrEmpty(tagTip)) chip.ToolTip = tagTip;
                right.Children.Add(chip);
            }
            Grid.SetColumn(right, 1);
            row.Children.Add(right);

            var frame = new Border { Padding = new Thickness(0, 9, 0, 9), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1), Child = row };
            frame.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSubtle");
            var holder = new Grid();
            holder.Children.Add(frame);
            return holder;
        }
    }
}
