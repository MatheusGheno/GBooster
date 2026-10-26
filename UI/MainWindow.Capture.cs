using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GBooster.Core;

namespace GBooster.UI
{
    /// <summary>
    /// Print da janela feito pelo próprio app (botão da câmera ou F12). A janela do GBooster é "sem moldura",
    /// e o Windows nem sempre deixa a ferramenta de recorte pegá-la; assim o print sai sempre.
    /// Salva em Imagens\GBooster e copia para a área de transferência.
    /// </summary>
    public partial class MainWindow
    {
        private readonly DispatcherTimer _shotTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };

        private void InitCapture()
        {
            _shotTimer.Tick += (s, e) => { _shotTimer.Stop(); ShotToast.Visibility = Visibility.Collapsed; };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F12 && !e.IsRepeat)
                {
                    e.Handled = true;
                    CaptureWindow();
                }
            };
        }

        private void Shot_Click(object sender, RoutedEventArgs e) => CaptureWindow();

        private void ShowShotToast(string text)
        {
            ShotToastText.Text = text;
            ShotToast.Visibility = Visibility.Visible;
            _shotTimer.Stop();
            _shotTimer.Start();
        }

        private void CaptureWindow()
        {
            try
            {
                var root = (FrameworkElement)Content;
                // ActualWidth/Height já são o tamanho da janela SEM a margem de 12 px (sombra). Para o print sair
                // exato, tira a margem por um instante, mede a janela de verdade e devolve tudo ao normal.
                double w = root.ActualWidth, h = root.ActualHeight;
                if (w < 10 || h < 10) return;
                // O aviso de "print salvo" não entra na imagem.
                ShotToast.Visibility = Visibility.Collapsed;

                Thickness oldMargin = root.Margin;
                RenderTargetBitmap bmp;
                try
                {
                    root.Margin = new Thickness(0);
                    root.Width = w;
                    root.Height = h;
                    UpdateLayout();

                    DpiScale dpi = VisualTreeHelper.GetDpi(this);
                    bmp = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
                        96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    bmp.Render(root);
                }
                finally
                {
                    root.ClearValue(FrameworkElement.WidthProperty);
                    root.ClearValue(FrameworkElement.HeightProperty);
                    root.Margin = oldMargin;
                    UpdateLayout();
                }
                bmp.Freeze();

                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "GBooster");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "GBooster-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (FileStream fs = File.Create(file)) enc.Save(fs);

                bool copied = true;
                try { System.Windows.Clipboard.SetImage(bmp); }
                catch { copied = false; }

                ShowShotToast(copied
                    ? Loc.T("Print salvo em Imagens\\GBooster e copiado. Cole com Ctrl+V.")
                    : Loc.T("Print salvo em Imagens\\GBooster."));
            }
            catch (Exception ex)
            {
                ShowShotToast(Loc.F("Não consegui tirar o print: {0}", ex.Message));
            }
        }
    }
}
