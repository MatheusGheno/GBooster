using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GBooster.Core;
using GBooster.UI;

namespace GBooster
{
    public partial class App : System.Windows.Application
    {
        private Mutex? _mutex;
        private Engine? _engine;
        private MainWindow? _window;
        private TrayService? _tray;
        private DispatcherTimer? _timer;
        private IntPtr _hwnd = IntPtr.Zero;
        private bool _exiting;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Só uma cópia do app por vez.
            _mutex = new Mutex(true, "GBooster.SingleInstance", out bool created);
            if (!created && e.Args.Contains("--elevated"))
            {
                // Veio do "Reiniciar como administrador": espera a cópia anterior terminar de fechar.
                try { created = _mutex.WaitOne(10000); }
                catch (AbandonedMutexException) { created = true; }
            }
            if (!created)
            {
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (s, a) =>
            {
                // Se algo inesperado acontecer, o mais importante é a tela voltar ao normal.
                try { _engine?.AddLog(Loc.T("Erro inesperado"), a.Exception.Message, false); } catch { }
                try { _engine?.RestoreNow(false); } catch { }
                a.Handled = true;
            };
            SessionEnding += (s, a) => { try { _engine?.RestoreNow(false); } catch { } };

            AppSettings settings = Store.LoadSettings();
            Loc.Init(settings.Language);

            // Tela de abertura: o loader gira enquanto o resto carrega.
            bool startInTray = e.Args.Contains("--tray");
            SplashWindow? splash = null;
            if (!startInTray && settings.ShowSplash && HeaderBrandAnimator.MotionAllowed)
            {
                try
                {
                    splash = new SplashWindow();
                    splash.Show();
                    Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                }
                catch { splash = null; }
            }

            _engine = new Engine(settings);
            _engine.RecoverFromCrash();

            _window = new MainWindow(_engine);
            _hwnd = new WindowInteropHelper(_window).EnsureHandle();

            _tray = new TrayService(
                _engine,
                () => _window.ShowFromTray(),
                ExitApp,
                () => _window.SaveSettingsNow(),
                () => _window.RefreshAll());
            _engine.Notify += (title, text) => _tray.Balloon(title, text);
            _engine.Changed += UpdateTooltip;

            _engine.AddLog(Loc.T("GBooster iniciado"), startInTray ? Loc.T("na bandeja") : "", false);

            // Verifica os processos a cada 2 segundos (leve: só lê a lista de processos).
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (s, a) =>
            {
                _engine.Tick();
                // Janela escondida na bandeja: não gasta nada atualizando uma tela que ninguém vê.
                if (_window.IsVisible && _window.WindowState != WindowState.Minimized) _window.RefreshStatus();
            };
            _timer.Start();

            UpdateTooltip();
            if (!startInTray)
            {
                if (splash != null)
                {
                    // termina o giro, vira G e, no fim, abre a janela principal
                    splash.Finish(() => _window?.ShowFromTray());
                }
                else
                {
                    _window.ShowFromTray();
                }
            }
            else
            {
                _window.TrimMemory();   // começou direto na bandeja
            }
        }

        private void UpdateTooltip()
        {
            if (_tray == null || _engine == null) return;
            if (_engine.Current != null)
            {
                _tray.SetTooltip(Loc.F("GBooster · em jogo {0}×{1}", _engine.Current.AppliedWidth, _engine.Current.AppliedHeight));
            }
            else if (_engine.Settings.PauseDetection)
            {
                _tray.SetTooltip(Loc.T("GBooster · pausado"));
            }
            else
            {
                _tray.SetTooltip(Loc.T("GBooster · aguardando o jogo"));
            }
        }

        public void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;

            try { _timer?.Stop(); } catch { }
            try { _engine?.Shutdown(); } catch { }          // devolve resolução e saturação
            try { _window?.SaveSettingsNow(); } catch { }
            try { _tray?.Dispose(); } catch { }
            if (_window != null)
            {
                _window.ExitRequested = true;
                _window.Close();
            }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }
}
