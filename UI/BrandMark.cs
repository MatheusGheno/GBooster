using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GBooster.UI
{
    /// <summary>Funções de suavização usadas nas animações da marca.</summary>
    internal static class Ease
    {
        public static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
        public static double OutCubic(double u) { u = Clamp(u); double k = 1 - u; return 1 - k * k * k; }
        public static double InCubic(double u) { u = Clamp(u); return u * u * u; }
        public static double InOutCubic(double u)
        {
            u = Clamp(u);
            return u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
        }
        /// <summary>Progresso 0..1 de um trecho que vai de a até b (em segundos).</summary>
        public static double Seg(double t, double a, double b) => Clamp((t - a) / (b - a));
    }

    /// <summary>Propriedade que avisa a barra lateral que ela está recolhida (os textos somem).</summary>
    public static class Nav
    {
        public static readonly DependencyProperty CollapsedProperty = DependencyProperty.RegisterAttached(
            "Collapsed", typeof(bool), typeof(Nav),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        /// <summary>Desenho do ícone do item de menu (texto de caminho SVG, convertido em Geometry pelo XAML).</summary>
        public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
            "Icon", typeof(Geometry), typeof(Nav), new FrameworkPropertyMetadata(null));

        public static Geometry? GetIcon(DependencyObject o) => (Geometry?)o.GetValue(IconProperty);
        public static void SetIcon(DependencyObject o, Geometry? v) => o.SetValue(IconProperty, v);

        public static bool GetCollapsed(DependencyObject o) => (bool)o.GetValue(CollapsedProperty);
        public static void SetCollapsed(DependencyObject o, bool v) => o.SetValue(CollapsedProperty, v);
    }

    /// <summary>
    /// A marca do GBooster desenhada em código (mesmas medidas da marca "Contorno" do design, caixa 64×64).
    /// Quem anima chama Set(...) a cada quadro; o desenho em si não tem timer.
    /// </summary>
    public sealed class BrandMark : Viewbox
    {
        public const double Circumference = 2 * Math.PI * 16.5;   // em pixels
        private const double StrokeW = 5.0;

        private readonly RotateTransform _rot = new RotateTransform(3, 32, 32);
        private readonly Path _arc;
        private readonly Line _leg;
        private readonly ScaleTransform _boltScale = new ScaleTransform(1, 1, 36, 33);
        private readonly Path _bolt;
        private readonly Canvas _canvas;
        private readonly DropShadowEffect _glow = new DropShadowEffect
        {
            Color = Color.FromRgb(255, 122, 20), ShadowDepth = 0, BlurRadius = 0, Opacity = 0
        };

        public RotateTransform Rotation => _rot;

        public BrandMark()
        {
            Stretch = Stretch.Uniform;
            SnapsToDevicePixels = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

            var grad = new LinearGradientBrush(Color.FromRgb(0xff, 0xb0, 0x61), Color.FromRgb(0xff, 0x6a, 0x00), new Point(0, 0), new Point(1, 1))
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(8, 6),
                EndPoint = new Point(56, 58)
            };
            grad.Freeze();

            _canvas = new Canvas { Width = 64, Height = 64 };

            var box = new Rectangle
            {
                Width = 60, Height = 60, RadiusX = 17, RadiusY = 17,
                Fill = new SolidColorBrush(Color.FromRgb(0x12, 0x17, 0x24)),
                Stroke = grad, StrokeThickness = 2.5
            };
            Canvas.SetLeft(box, 2);
            Canvas.SetTop(box, 2);
            _canvas.Children.Add(box);

            // Círculo r=16,5 que começa às 3 horas e vai no sentido horário (igual ao SVG).
            var fig = new PathFigure { StartPoint = new Point(48.5, 32), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(15.5, 32), new Size(16.5, 16.5), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new ArcSegment(new Point(48.5, 32), new Size(16.5, 16.5), 0, false, SweepDirection.Clockwise, true));
            _arc = new Path
            {
                Data = new PathGeometry(new[] { fig }),
                Stroke = grad, StrokeThickness = StrokeW,
                StrokeDashCap = PenLineCap.Round,
                RenderTransform = _rot
            };
            _canvas.Children.Add(_arc);

            _leg = new Line
            {
                X1 = 48.5, Y1 = 32, X2 = 34, Y2 = 32,
                Stroke = grad, StrokeThickness = StrokeW,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            _canvas.Children.Add(_leg);

            _bolt = new Path
            {
                Data = Geometry.Parse("M37.5,21 L28,34.5 L35,34.5 L31.5,45 L44,31 L37,31 Z"),
                Fill = Brushes.White,
                RenderTransform = _boltScale
            };
            _canvas.Children.Add(_bolt);

            Child = _canvas;
            Reset();
        }

        /// <summary>Estado final (repouso): G completo com o raio.</summary>
        public void Reset() => Set(3, 320, 1, 1, 0);

        /// <param name="rotation">giro do arco em graus</param>
        /// <param name="dashDeg">tamanho do arco em graus (110 = pedaço de loader, 320 = G)</param>
        /// <param name="leg">0..1: quanto da perna do G já apareceu</param>
        /// <param name="bolt">escala do raio (0 = escondido, 1 = normal)</param>
        /// <param name="glow">0..1: brilho laranja ao redor</param>
        public void Set(double rotation, double dashDeg, double leg, double bolt, double glow)
        {
            _rot.Angle = rotation;
            double unit = Circumference / StrokeW;                 // volta inteira, em "larguras de traço"
            _arc.StrokeDashArray = new DoubleCollection { Math.Max(0.01, dashDeg / 360.0 * unit), 40 };

            // a perna cresce do arco para dentro do G (de x = 48,5 até x = 34)
            _leg.X2 = 48.5 + (34 - 48.5) * Ease.Clamp(leg);
            _leg.Visibility = leg <= 0.001 ? Visibility.Hidden : Visibility.Visible;

            double b = Math.Max(0, bolt);
            _boltScale.ScaleX = b;
            _boltScale.ScaleY = b;
            _bolt.Visibility = b <= 0.001 ? Visibility.Hidden : Visibility.Visible;

            if (glow <= 0.001)
            {
                _canvas.Effect = null;
            }
            else
            {
                _glow.BlurRadius = 18 * glow;
                _glow.Opacity = 0.65 * glow;
                _canvas.Effect = _glow;
            }
        }
    }

    /// <summary>
    /// Animação discreta do topo: a cada 12 s o nome some para dentro da marca, o G dá uma volta,
    /// o raio pulsa e o nome volta. Só gasta processador enquanto a animação toca (~3,7 s).
    /// </summary>
    public sealed class HeaderBrandAnimator
    {
        private const double Cycle = 12.0;
        private const double WordShift = 34;

        private readonly BrandMark _mark;
        private readonly FrameworkElement? _wordClip;
        private readonly TextBlock? _word;
        private readonly TranslateTransform? _shift;
        private readonly double _full;
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Cycle) };
        private readonly Stopwatch _sw = new Stopwatch();
        private bool _playing;
        private bool _showWord = true;

        public HeaderBrandAnimator(BrandMark mark, FrameworkElement? wordClip, TextBlock? word, double fullWidth)
        {
            _mark = mark;
            _wordClip = wordClip;
            _word = word;
            _full = fullWidth;
            _shift = word?.RenderTransform as TranslateTransform;
            _timer.Tick += (s, e) => Play();
        }

        public static bool MotionAllowed => SystemParameters.ClientAreaAnimation;

        public bool ShowWord
        {
            get => _showWord;
            set
            {
                _showWord = value;
                if (!_playing) ApplyWord(1);
            }
        }

        public void Start()
        {
            if (!MotionAllowed) return;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            if (_playing)
            {
                System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
                _playing = false;
                _mark.Reset();
                ApplyWord(1);
            }
        }

        private void Play()
        {
            if (_playing) return;
            _playing = true;
            _sw.Restart();
            System.Windows.Media.CompositionTarget.Rendering += OnFrame;
        }

        private void ApplyWord(double visible)
        {
            if (_wordClip == null || _word == null) return;
            double v = _showWord ? visible : 0;
            _wordClip.Width = _full * v;
            _word.Opacity = v;
            if (_shift != null) _shift.X = -WordShift * (1 - v);
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            double t = _sw.Elapsed.TotalSeconds;
            double end = _showWord ? 3.7 : 2.3;

            // nome: sai (0–0,8 s) e volta (2,4–3,7 s)
            double word = 1;
            if (t < 2.4) word = 1 - Ease.InCubic(Ease.Seg(t, 0, 0.8));
            else word = Ease.OutCubic(Ease.Seg(t, 2.4, 3.7));
            ApplyWord(word);

            // G: uma volta completa (1,0–2,2 s)
            double rot = 3 + 360 * Ease.InOutCubic(Ease.Seg(t, 1.0, 2.2));
            // raio: pulsa (1,8–2,3 s)
            double pb = Ease.Seg(t, 1.8, 2.3);
            double bolt = 1 + 0.3 * Math.Sin(pb * Math.PI);
            _mark.Set(rot, 320, 1, bolt, 0);

            if (t >= end)
            {
                System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
                _playing = false;
                _mark.Reset();
                ApplyWord(1);
            }
        }
    }
}
