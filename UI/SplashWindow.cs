using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace GBooster.UI
{
    /// <summary>
    /// Tela de abertura: o loader gira enquanto o programa carrega; quando termina de carregar ele
    /// desacelera, ganha a perna e vira o G, o raio entra e o nome GBOOSTER aparece ao lado.
    /// </summary>
    public sealed class SplashWindow : Window
    {
        private const double SpinDegPerSec = 300;      // velocidade do giro enquanto carrega
        private const double DecelSeconds = 1.2;        // tempo para frear e virar G
        private const double DecelDegrees = 120;        // 3 * graus / tempo = velocidade do giro (sem "tranco")
        private const double MinSpinSeconds = 0.6;
        private const double WordShift = 34;

        private readonly BrandMark _mark = new BrandMark { Width = 96, Height = 96 };
        private readonly Grid _wordClip = new Grid { ClipToBounds = true, Width = 0, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _word;
        private readonly TranslateTransform _wordShift = new TranslateTransform(-WordShift, 0);
        private readonly double _wordFull;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private AnimationClock? _spin;
        private bool _finishing;
        private bool _decel;
        private bool _fading;
        private double _t0;
        private double _a0;
        private double _d;
        private double _fadeStart;
        private Action? _done;
        private bool _doneCalled;

        public SplashWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Width = 440;
            Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon = null;

            _word = new TextBlock
            {
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0),
                RenderTransform = _wordShift,
                Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _word.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Display");
            _word.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
            _word.Inlines.Add(new System.Windows.Documents.Run("G"));
            var boost = new System.Windows.Documents.Run("BOOSTER");
            boost.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.LogoAccent");
            _word.Inlines.Add(boost);
            _wordClip.Children.Add(_word);

            _word.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _wordFull = _word.DesiredSize.Width + 16;

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(_mark);
            row.Children.Add(_wordClip);

            var card = new Border
            {
                Margin = new Thickness(20),
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Child = row,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black }
            };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Window");
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.WindowBorder");
            Content = card;

            _mark.Set(0, 110, 0, 0, 0);

            Loaded += (s, e) => StartSpin();
        }

        private void StartSpin()
        {
            // Giro por animação "independente": continua suave mesmo se o programa estiver ocupado carregando.
            var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(360.0 / SpinDegPerSec)) { RepeatBehavior = RepeatBehavior.Forever };
            _spin = anim.CreateClock();
            _mark.Rotation.ApplyAnimationClock(RotateTransform.AngleProperty, _spin);
        }

        /// <summary>Avisa que o programa terminou de carregar. 'done' é chamado quando a janela principal pode aparecer.</summary>
        public void Finish(Action done)
        {
            if (_finishing) return;
            _finishing = true;
            _done = done;
            CompositionTarget.Rendering += OnFrame;
        }

        private double CurrentAngle()
        {
            double p = _spin?.CurrentProgress ?? 0;
            return p * 360.0;
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            double now = _sw.Elapsed.TotalSeconds;

            if (!_decel)
            {
                // espera o giro chegar no ponto certo para frear e terminar exatamente em "G"
                double a = CurrentAngle();
                double start = ((3 - DecelDegrees) % 360 + 360) % 360;
                double ahead = ((a - start) % 360 + 360) % 360;
                bool noAnim = _spin == null || !MotionOk();
                if ((now >= MinSpinSeconds && ahead < 9) || noAnim || now > MinSpinSeconds + 1.6)
                {
                    _decel = true;
                    _mark.Rotation.ApplyAnimationClock(RotateTransform.AngleProperty, null);
                    _a0 = a;
                    _d = ((3 - a) % 360 + 360) % 360;
                    if (_d < 40) _d += 360;
                    _t0 = now;
                }
                else
                {
                    return;
                }
            }

            double t = now - _t0;

            // 1) freia e vira G
            double e1 = Ease.OutCubic(Ease.Seg(t, 0, DecelSeconds));
            double rot = _a0 + _d * e1;
            double dash = Ease.Lerp(110, 320, e1);
            // 2) a perna do G cresce
            double leg = Ease.InOutCubic(Ease.Seg(t, 0.75, 1.1));
            // 3) o raio entra (estoura até 1,25 e assenta em 1)
            double bolt;
            if (t < 1.1) bolt = 0;
            else if (t < 1.3) bolt = 1.25 * Ease.OutCubic(Ease.Seg(t, 1.1, 1.3));
            else bolt = Ease.Lerp(1.25, 1.0, Ease.InOutCubic(Ease.Seg(t, 1.3, 1.55)));
            // 4) brilho
            double glow = t < 1.5 ? Ease.Seg(t, 1.15, 1.5) : 1 - Ease.InOutCubic(Ease.Seg(t, 1.5, 2.6));
            _mark.Set(rot, dash, leg, bolt, glow);

            // 5) o nome aparece ao lado e empurra a marca para a esquerda
            double w = Ease.OutCubic(Ease.Seg(t, 1.6, 2.8));
            _wordClip.Width = _wordFull * w;
            _word.Opacity = w;
            _wordShift.X = -WordShift * (1 - w);

            // 6) segura um instante e some
            if (t >= 3.4 && !_doneCalled)
            {
                _doneCalled = true;
                _fading = true;
                _fadeStart = now;
                _done?.Invoke();
            }
            if (_fading)
            {
                double f = Ease.Seg(now - _fadeStart, 0, 0.35);
                Opacity = 1 - f;
                if (f >= 1)
                {
                    CompositionTarget.Rendering -= OnFrame;
                    Close();
                }
            }
        }

        private static bool MotionOk() => SystemParameters.ClientAreaAnimation;
    }
}
