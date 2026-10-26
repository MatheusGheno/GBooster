using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using GBooster.Core;

namespace GBooster.UI
{
    /// <summary>Ícone da bandeja e menu. Usa o NotifyIcon do Windows (nativo, leve).</summary>
    public sealed class TrayService : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly Engine _engine;
        private readonly Action _open;
        private readonly Action _exit;
        private readonly Action _saveSettings;
        private readonly Action _refreshUi;
        private TimeSpan _lastCpu = TimeSpan.Zero;
        private DateTime _lastWall = DateTime.UtcNow;
        private double _lastPercent;

        public TrayService(Engine engine, Action open, Action exit, Action saveSettings, Action refreshUi)
        {
            _engine = engine;
            _open = open;
            _exit = exit;
            _saveSettings = saveSettings;
            _refreshUi = refreshUi;

            _icon = new NotifyIcon
            {
                Icon = MakeIcon(),
                Text = "GBooster",
                Visible = true
            };
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    _open();
                }
            };
            _icon.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    ShowMenu(Cursor.Position);
                }
            };
        }

        private TrayMenuWindow? _menu;

        /// <summary>Abre o menu da bandeja (janela própria, no visual do app).</summary>
        private void ShowMenu(Point where)
        {
            try { _menu?.CloseMenu(); } catch { }

            var m = new TrayMenuWindow();
            _menu = m;

            AppliedState? cur = _engine.Current;
            string status = cur != null
                ? (cur.Manual ? Loc.F("Teste · {0}", cur.GameName) : Loc.F("Em jogo · {0}", cur.ProcessName))
                : _engine.Settings.PauseDetection ? Loc.T("Detecção geral pausada") : Loc.T("Aguardando jogo");
            m.AddHeader(status, cur != null);
            m.AddSeparator();

            m.AddItem(Loc.T("Abrir GBooster"), _open);
            m.AddItem(_engine.Settings.PauseDetection ? Loc.T("Retomar detecção geral") : Loc.T("Pausar detecção geral"), () =>
            {
                _engine.Settings.PauseDetection = !_engine.Settings.PauseDetection;
                _saveSettings();
                _refreshUi();
            });

            m.AddSeparator();
            m.AddItem(Loc.T("Restaurar resolução agora"), () => _engine.RestoreByUser(), null, MenuDot.None, _engine.IsActive);

            m.AddSeparator();
            m.AddInfo(UsageText());
            m.AddItem(Loc.T("Sair"), _exit);

            m.ShowAt(where);
        }

        /// <summary>Quanto o próprio GBooster está usando (CPU média desde a última vez + memória).</summary>
        private string UsageText()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    DateTime now = DateTime.UtcNow;
                    TimeSpan cpu = p.TotalProcessorTime;
                    double wall = (now - _lastWall).TotalSeconds;
                    if (wall >= 1.0)
                    {
                        _lastPercent = (cpu - _lastCpu).TotalSeconds / wall / Environment.ProcessorCount * 100.0;
                        _lastCpu = cpu;
                        _lastWall = now;
                    }
                    double mb = p.WorkingSet64 / 1048576.0;
                    return Loc.F("Uso do app: CPU {0}%  ·  RAM {1} MB", _lastPercent.ToString("0.0"), mb.ToString("0"));
                }
            }
            catch
            {
                return Loc.T("Uso do app: indisponível");
            }
        }

        public void SetTooltip(string text)
        {
            // NotifyIcon.Text aceita no máximo 63 caracteres.
            _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        public void Balloon(string title, string text)
        {
            _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.None);
        }

        private static Icon MakeIcon()
        {
            // Usa o ícone do app (Assets/app.ico, embutido no programa).
            try
            {
                var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
                if (info != null)
                {
                    using (info.Stream)
                    {
                        return new Icon(info.Stream, SystemInformation.SmallIconSize);
                    }
                }
            }
            catch { /* cai no ícone desenhado abaixo */ }
            return MakeFallbackIcon();
        }

        private static Icon MakeFallbackIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    int r = 8;
                    path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                    path.AddArc(31 - r * 2, 0, r * 2, r * 2, 270, 90);
                    path.AddArc(31 - r * 2, 31 - r * 2, r * 2, r * 2, 0, 90);
                    path.AddArc(0, 31 - r * 2, r * 2, r * 2, 90, 90);
                    path.CloseFigure();
                    using (var b = new SolidBrush(Color.FromArgb(255, 122, 20)))
                    {
                        g.FillPath(b, path);
                    }
                }
                using (var f = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var tb = new SolidBrush(Color.FromArgb(11, 10, 9)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("G", f, tb, new RectangleF(0, 1, 32, 32), sf);
                }
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
