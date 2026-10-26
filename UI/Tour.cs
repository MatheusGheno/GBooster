using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using GBooster.Core;

namespace GBooster.UI
{
    /// <summary>Tour guiado: escurece a janela, destaca uma parte por vez e explica. Abre na primeira vez e pelo botão "Repetir tour" da Ajuda.</summary>
    public partial class MainWindow
    {
        private sealed class TourStep
        {
            public string Title = "";
            public string Text = "";
            public Func<FrameworkElement?>? Target;
        }

        private List<TourStep> _tourSteps = new List<TourStep>();
        private int _tourIdx;
        private bool _tourActive;
        private bool _tourKeyHooked;

        private void BuildTourSteps()
        {
            _tourSteps = new List<TourStep>
            {
                new TourStep { Title = "Bem-vindo ao GBooster", Text = "Este tour rápido mostra onde fica cada coisa. Leva menos de um minuto e você pode pular quando quiser." },
                new TourStep { Title = "Estado do app", Text = "Aqui você vê se o GBooster está esperando um jogo ou se a tela já foi trocada, e o que volta quando o jogo fechar. O botão devolve a resolução na hora.", Target = () => StatusBar },
                new TourStep { Title = "Escolha o jogo", Text = "Cada jogo tem os seus próprios ajustes. Use “Novo jogo…” na lista para cadastrar outro.", Target = () => GameCombo },
                new TourStep { Title = "Diga qual é o programa do jogo", Text = "Clique no campo e escolha o .exe do jogo. É assim que o app sabe que o jogo abriu.", Target = () => ProcBox },
                new TourStep { Title = "Tela no jogo", Text = "Escolha a resolução e a taxa de atualização para quando o jogo abrir. Use Testar resolução para ver como fica: ela volta sozinha.", Target = () => ScreenCard },
                new TourStep { Title = "Saturação", Text = "Opcional e só em placas NVIDIA: muda a saturação durante o jogo e devolve depois.", Target = () => SatCard },
                new TourStep { Title = "Desempenho", Text = "Aplica ajustes do Windows para o PC render melhor nos jogos. Antes, crie o ponto de restauração. Você escolhe o que aplicar e pode desfazer tudo.", Target = () => TabPerf },
                new TourStep { Title = "Limpeza", Text = "Mostra o que pode ser apagado e o tamanho de cada item. Só apaga o que você marcar.", Target = () => TabClean },
                new TourStep { Title = "Histórico", Text = "Registro do que o app fez: trocas de tela, otimizações e limpezas. Dá para filtrar por Jogos ou Boost.", Target = () => TabLog },
                new TourStep { Title = "Meu sistema", Text = "Mostra as informações do seu PC. Dá para copiar ou abrir no Bloco de Notas, útil para pedir ajuda.", Target = () => TabSystem },
                new TourStep { Title = "Ajuda e tour", Text = "Aqui ficam as explicações do app. Para ver este tour de novo, abra a Ajuda e clique em Repetir tour.", Target = () => HelpBtn },
            };
        }

        public void StartTour()
        {
            if (_tourActive) return;
            if (PageHelp.Visibility == Visibility.Visible) HelpBack_Click(this, new RoutedEventArgs());
            TabGames.IsChecked = true;
            BuildTourSteps();
            _tourIdx = 0;
            _tourActive = true;
            if (!_tourKeyHooked)
            {
                _tourKeyHooked = true;
                PreviewKeyDown += (s, e) =>
                {
                    if (_tourActive && e.Key == Key.Escape) { EndTour(); e.Handled = true; }
                };
            }
            TourLayer.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(RenderTour), DispatcherPriority.Loaded);
        }

        private void EndTour()
        {
            _tourActive = false;
            TourLayer.Children.Clear();
            TourLayer.Visibility = Visibility.Collapsed;
            if (!S.TourSeen)
            {
                S.TourSeen = true;
                SaveSettingsNow();
            }
        }

        private void TourGo(int delta)
        {
            int next = _tourIdx + delta;
            if (next < 0) return;
            if (next >= _tourSteps.Count) { EndTour(); return; }
            _tourIdx = next;
            RenderTour();
        }

        private void RenderTour()
        {
            if (!_tourActive) return;
            TourLayer.Children.Clear();
            UpdateLayout();
            double w = TourLayer.ActualWidth, h = TourLayer.ActualHeight;
            if (w <= 0 || h <= 0) return;

            TourStep step = _tourSteps[_tourIdx];
            FrameworkElement? target = step.Target?.Invoke();
            Rect hole = Rect.Empty;
            if (target != null && target.IsVisible && target.ActualWidth > 0 && target.ActualHeight > 0)
            {
                Rect r = target.TransformToVisual(TourLayer).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                hole = new Rect(r.X - 6, r.Y - 6, r.Width + 12, r.Height + 12);
                hole.Intersect(new Rect(0, 0, w, h));
            }
            bool hasHole = !hole.IsEmpty;

            // Fundo escuro com um "buraco" no alvo
            Geometry full = new RectangleGeometry(new Rect(0, 0, w, h), 13, 13);
            Geometry shape = hasHole
                ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole, 10, 10))
                : full;
            TourLayer.Children.Add(new Path { Data = shape, Fill = new SolidColorBrush(Color.FromArgb(185, 0, 0, 0)) });
            if (hasHole)
            {
                // Bloqueia cliques no alvo durante o tour e desenha o contorno laranja.
                var frame = new Rectangle
                {
                    Width = hole.Width, Height = hole.Height, RadiusX = 10, RadiusY = 10,
                    Fill = Brushes.Transparent, StrokeThickness = 2,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(hole.X, hole.Y, 0, 0),
                };
                frame.SetResourceReference(Shape.StrokeProperty, "Brush.Accent");
                TourLayer.Children.Add(frame);
            }

            // Cartão com o texto
            const double cardW = 340;
            var inner = new StackPanel();
            inner.Children.Add(UiKit.Label(Loc.F("{0} DE {1}", _tourIdx + 1, _tourSteps.Count)));
            TextBlock title = UiKit.DisplayText(Loc.T(step.Title), 20, "Brush.TextPrimary");
            title.Margin = new Thickness(0, 6, 0, 0);
            inner.Children.Add(title);
            inner.Children.Add(UiKit.Hint(Loc.T(step.Text), 8));

            var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool last = _tourIdx == _tourSteps.Count - 1;
            if (!last)
            {
                Button skip = UiKit.Link(Loc.T("Pular tour"), (s, e) => EndTour());
                skip.VerticalAlignment = VerticalAlignment.Center;
                skip.HorizontalAlignment = HorizontalAlignment.Left;
                buttons.Children.Add(skip);
            }
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            if (_tourIdx > 0)
            {
                Button back = UiKit.Plain(Loc.T("Voltar"), (s, e) => TourGo(-1), 36, 16);
                back.Margin = new Thickness(0, 0, 8, 0);
                right.Children.Add(back);
            }
            right.Children.Add(UiKit.Primary(last ? Loc.T("Concluir") : Loc.T("Próximo"), (s, e) => TourGo(1), 36, 20));
            Grid.SetColumn(right, 1);
            buttons.Children.Add(right);
            inner.Children.Add(buttons);

            Border card = UiKit.Card(inner);
            card.Width = cardW;
            card.Padding = new Thickness(20);
            card.HorizontalAlignment = HorizontalAlignment.Left;
            card.VerticalAlignment = VerticalAlignment.Top;
            card.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black };
            card.Measure(new Size(cardW, h));
            double cardH = card.DesiredSize.Height;

            double x, y;
            if (!hasHole)
            {
                x = (w - cardW) / 2; y = (h - cardH) / 2;
            }
            else if (hole.X < 220)
            {
                // itens do menu lateral: cartão ao lado
                x = hole.Right + 16; y = hole.Y + hole.Height / 2 - cardH / 2;
            }
            else if (hole.Bottom + 16 + cardH <= h - 12)
            {
                x = hole.X + hole.Width / 2 - cardW / 2; y = hole.Bottom + 16;
            }
            else if (hole.Y - cardH >= 16)
            {
                x = hole.X + hole.Width / 2 - cardW / 2; y = Math.Max(12, hole.Y - 16 - cardH);
            }
            else if (hole.X - 16 - cardW >= 12)
            {
                x = hole.X - 16 - cardW; y = hole.Y + hole.Height / 2 - cardH / 2;
            }
            else
            {
                x = hole.Right + 16; y = hole.Y + hole.Height / 2 - cardH / 2;
            }
            x = Math.Max(12, Math.Min(x, w - cardW - 12));
            y = Math.Max(12, Math.Min(y, h - cardH - 12));
            card.Margin = new Thickness(x, y, 0, 0);
            TourLayer.Children.Add(card);
        }
    }
}
