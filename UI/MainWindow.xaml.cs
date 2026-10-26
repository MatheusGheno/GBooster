using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GBooster.Core;

namespace GBooster.UI
{
    public partial class MainWindow : Window
    {
        private readonly Engine _engine;
        private readonly DispatcherTimer _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private int _previewLeft;
        private const int PreviewSeconds = 6;
        private readonly DispatcherTimer _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        // Começa verdadeiro: o XAML dispara eventos durante o InitializeComponent e eles devem ser ignorados.
        private bool _loading = true;

        public bool ExitRequested { get; set; }

        /// <summary>Quem registra o atalho no Windows (o App). Devolve false se a combinação já está em uso.</summary>

        private AppSettings S => _engine.Settings;
        private static readonly GameConfig NewGameItem = new GameConfig { Id = "__new__", Name = "＋  Novo jogo…" };
        private const string NewGameKey = "＋  Novo jogo…";
        private GameConfig? Selected => GameCombo.SelectedItem is GameConfig g && !ReferenceEquals(g, NewGameItem) ? g : null;

        public MainWindow(Engine engine)
        {
            InitializeComponent();
            _engine = engine;
            Loc.Apply(this);

            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                Store.SaveSettings(S);
                // Jogo aberto: aplica na hora o que foi mudado.
                GameConfig? sel = Selected;
                if (sel != null && _engine.IsActive && _engine.Current != null && !_engine.Current.Manual && _engine.Current.GameId == sel.Id)
                {
                    string? err = _engine.Reapply(sel);
                    if (err != null) System.Windows.MessageBox.Show(err, "GBooster");
                }
            };
            _previewTimer.Tick += (s, e) => PreviewTick();
            _engine.Changed += RefreshAll;
            InitCapture();

            InitBoost();
            LoadGeneral();
            ReloadGames(S.SelectedGameId);
            _loading = false;
            RefreshAll();

            // Marca animada do topo do menu lateral
            HeaderWord.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _wordFull = HeaderWord.DesiredSize.Width + 10;
            HeaderWordClip.Width = _wordFull;
            _brandAnim = new HeaderBrandAnimator(HeaderBrand, HeaderWordClip, HeaderWord, _wordFull);
            IsVisibleChanged += (s2, e2) =>
            {
                if (IsVisible) _brandAnim.Start(); else _brandAnim.Stop();
            };
        }

        private HeaderBrandAnimator _brandAnim = null!;
        private double _wordFull;
        // ------------------------------------------------------------------
        // Janela
        // ------------------------------------------------------------------
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>Janela escondida na bandeja: devolve ao Windows a memória que não está em uso.</summary>
        public void TrimMemory()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                    NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
                }
                catch { /* só uma otimização */ }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!ExitRequested)
            {
                e.Cancel = true;
                if (S.CloseToTray)
                {
                    Hide();
                    TrimMemory();
                }
                else
                {
                    ((App)System.Windows.Application.Current).ExitApp();
                }
            }
            base.OnClosing(e);
        }

        public void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            RefreshAll();

            // Aviso de primeiro uso: aparece uma vez só.
            if (!S.ScalingNoticeSeen)
            {
                S.ScalingNoticeSeen = true;
                SaveSettingsNow();
                new NoticeWindow { Owner = this }.ShowDialog();
            }
            if (!S.TourSeen && !_tourActive)
            {
                Dispatcher.BeginInvoke(new Action(StartTour), DispatcherPriority.Background);
            }
        }

        public void SaveSettingsNow()
        {
            _saveTimer.Stop();
            Store.SaveSettings(S);
        }

        private void QueueSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        // ------------------------------------------------------------------
        // Abas
        // ------------------------------------------------------------------
        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (PageGames == null || PageGeneral == null || PageLog == null || PagePerf == null || PageClean == null || PageSystem == null || PageHelp == null)
            {
                return;
            }
            PageHelp.Visibility = Visibility.Collapsed;
            UpdateHelpBtn();
            PageGames.Visibility = TabGames.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageGeneral.Visibility = TabGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageLog.Visibility = TabLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PagePerf.Visibility = TabPerf.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageClean.Visibility = TabClean.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageSystem.Visibility = TabSystem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageTitle.Text = Loc.T(TabGeneral.IsChecked == true ? "Geral"
                : TabLog.IsChecked == true ? "Histórico"
                : TabPerf.IsChecked == true ? "Desempenho"
                : TabClean.IsChecked == true ? "Limpeza"
                : TabSystem.IsChecked == true ? "Meu sistema" : "Jogos");
            if (TabLog.IsChecked == true)
            {
                RefreshLog();
            }
            if (TabPerf.IsChecked == true)
            {
                RefreshPerf();
            }
            if (TabClean.IsChecked == true)
            {
                RefreshClean();
            }
            if (TabSystem.IsChecked == true)
            {
                RefreshSystem();
            }
        }

        // ------------------------------------------------------------------
        // Status e atualização geral
        // ------------------------------------------------------------------
        public void RefreshAll()
        {
            RefreshStatus();
            // Se o app voltou o Hz para "manter atual" (o Windows recusou), atualiza a lista.
            if (Selected != null && HzCombo.SelectedItem is HzItem hi && hi.Hz != Selected.Hz) FillHz(Selected);
            RefreshLog();
            RefreshTestButton();
            UpdateSatOutside();
            RestoreBtn.ToolTip = Loc.T("Restaurar resolução e saturação agora");
        }

        public void RefreshStatus()
        {
            MonitorSnapshot? cur = DisplayService.GetCurrent(DisplayService.GetPrimaryDevice());
            string curText = cur != null ? cur.Width + "×" + cur.Height + " · " + cur.Hz + " Hz" : "—";
            AppliedState? st = _engine.Current;

            if (st != null)
            {
                StatusBar.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
                StatusBar.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSubtle");
                StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.StateActive");
                StatusTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.StateActive");
                StatusTitle.Text = Loc.T(st.Manual ? "TESTE" : "EM JOGO");
                StatusProc.Text = st.Manual ? st.GameName : st.ProcessName;
                StatusProc.ToolTip = StatusProc.Text;
                StatusProcRow.Visibility = Visibility.Visible;
                StatusLine1Icon.Data = Geometry.Parse(BoltIcon);
                StatusLine1Icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.Accent");
                StatusLine1Row.ToolTip = Loc.T("Aplicado agora");
                StatusLine1.Text = st.AppliedWidth + "×" + st.AppliedHeight
                                   + (st.AppliedHz > 0 ? " · " + st.AppliedHz + "Hz" : "")
                                   + (st.AppliedSaturation >= 0 ? " · " + st.AppliedSaturation + "%" : "");
                MonitorSnapshot? orig = st.Monitors.FirstOrDefault();
                VibranceSnapshot? vorig = st.Vibrance.FirstOrDefault();
                StatusLine2.Text = orig != null
                    ? orig.Width + "×" + orig.Height + " · " + orig.Hz + "Hz" + (vorig != null ? " · " + vorig.Percent + "%" : "")
                    : "";
                StatusLine2Row.Visibility = Visibility.Visible;
                RestoreBtn.Style = (Style)FindResource("BtnSmAccent");
                RestoreBtn.IsEnabled = true;
            }
            else
            {
                StatusBar.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
                StatusBar.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSubtle");
                StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.StateIdle");
                StatusTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
                StatusLine2Row.Visibility = Visibility.Collapsed;
                StatusProcRow.Visibility = Visibility.Collapsed;
                StatusLine1Icon.Data = Geometry.Parse(MonitorIcon);
                StatusLine1Icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.TextTertiary");
                StatusLine1Row.ToolTip = Loc.T("Tela agora");
                RestoreBtn.Style = (Style)FindResource("BtnSm");
                RestoreBtn.IsEnabled = false;

                if (_engine.IsWaitingForDelay)
                {
                    StatusTitle.Text = Loc.T("DETECTADO");
                    StatusLine1.Text = Loc.T("aplicando em instantes…");
                }
                else if (_engine.IgnoredGameName != null)
                {
                    StatusBar.SetResourceReference(Border.BackgroundProperty, "Brush.WarnBg");
                    StatusBar.SetResourceReference(Border.BorderBrushProperty, "Brush.WarnBorder");
                    StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Warn");
                    StatusTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warn");
                    StatusTitle.Text = Loc.T("PAUSADO");
                    StatusProc.Text = _engine.IgnoredGameName;
                    StatusProc.ToolTip = StatusProc.Text;
                    StatusProcRow.Visibility = Visibility.Visible;
                    StatusLine1.Text = Loc.T("restaurado à mão");
                    StatusLine1Row.ToolTip = Loc.T("Você restaurou a tela com o jogo ainda aberto.");
                    StatusLine2.Text = Loc.T("volta ao fechar o jogo");
                    StatusLine2Row.ToolTip = Loc.T("A troca de tela só volta quando o jogo fechar. Ao abrir de novo, tudo volta ao normal.");
                    StatusLine2Row.Visibility = Visibility.Visible;
                }
                else if (S.PauseDetection)
                {
                    StatusTitle.Text = Loc.T("PAUSA GERAL");
                    StatusLine1.Text = curText.Replace(" Hz","Hz");
                }
                else
                {
                    StatusTitle.Text = Loc.T("AGUARDANDO");
                    StatusLine1.Text = curText.Replace(" Hz","Hz");
                }
            }
        }

        private const string BoltIcon = "M9,1.8 L3.8,9 H8 L7,14.2 L12.2,7 H8 Z";
        private const string MonitorIcon = "M2,3.5 H14 V11 H2 Z M6,13.8 H10 M8,11 V13.8";

        private void UpdateSatOutside()
        {
            if (!_engine.Vibrance.Available)
            {
                SatOutside.Text = "";
                return;
            }
            int? pct = _engine.Current?.Vibrance.FirstOrDefault()?.Percent
                       ?? _engine.Vibrance.Read(DisplayService.GetPrimaryDevice())?.Percent;
            SatOutside.Text = pct == null
                ? ""
                : _engine.IsActive
                    ? Loc.F("Fora do jogo volta para {0}% (o valor de antes do jogo).", pct)
                    : Loc.F("Fora do jogo fica como você usa hoje ({0}%). O app só muda durante o jogo.", pct);
        }

        private void RefreshTestButton()
        {
            bool live = _engine.IsActive && _engine.Current != null && !_engine.Current.Manual;
            // Com o jogo aberto, as mudanças valem na hora; o teste só serve para visualizar antes.
            TestBtn.IsEnabled = Selected != null && !live;
            if (_previewLeft <= 0) TestText.Text = Loc.T("Testar resolução");
        }

        private string _logFilter = "all";

        private void RefreshLog()
        {
            var rows = _engine.Log.Where(l => _logFilter == "all" || l.Kind == _logFilter).Take(40).ToList();
            LogList.ItemsSource = rows;
            LogEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (Button b in new[] { LogFAll, LogFGame, LogFBoost })
            {
                b.Style = (Style)FindResource((string)b.Tag == _logFilter ? "BtnSmAccent" : "BtnSm");
            }
        }

        private void LogFilter_Click(object sender, RoutedEventArgs e)
        {
            _logFilter = (string)((Button)sender).Tag;
            RefreshLog();
        }

        private void Restore_Click(object sender, RoutedEventArgs e) => _engine.RestoreByUser();

        // ------------------------------------------------------------------
        // Jogos
        // ------------------------------------------------------------------
        private void ReloadGames(string? selectId)
        {
            bool before = _loading;
            _loading = true;
            NewGameItem.Name = Loc.T(NewGameKey);
            GameCombo.ItemsSource = null;
            GameCombo.ItemsSource = S.Games.Concat(new[] { NewGameItem }).ToList();
            GameConfig? pick = S.Games.FirstOrDefault(g => g.Id == selectId) ?? S.Games.FirstOrDefault();
            GameCombo.SelectedItem = pick;
            _loading = before;
            LoadSelected();
        }

        private void LoadSelected()
        {
            bool before = _loading;
            _loading = true;

            GameConfig? g = Selected;
            bool has = g != null;
            ProcBox.IsEnabled = has;
            DetectSwitch.IsEnabled = has;
            DeleteBtn.IsEnabled = has;
            ResCombo.IsEnabled = has;
            HzCombo.IsEnabled = has;
            TestBtn.IsEnabled = has;

            GamePrefixRun.Text = Loc.T("AJUSTES PARA");
            GameNameRun.Text = g != null ? g.Name.ToUpper(System.Globalization.CultureInfo.CurrentCulture) : "";
            if (g != null)
            {
                S.SelectedGameId = g.Id;
                ProcBox.Text = g.ProcessName;
                UpdateProcUi(g);
                UpdatePauseUi(g);

                List<DisplayMode> modes = DisplayService.ListModes(DisplayService.GetPrimaryDevice());
                DisplayMode? match = modes.FirstOrDefault(m => m.Width == g.Width && m.Height == g.Height);
                if (match == null)
                {
                    match = new DisplayMode { Width = g.Width, Height = g.Height };
                    modes.Insert(0, match);
                }
                ResCombo.ItemsSource = modes;
                ResCombo.SelectedItem = match;
                FillHz(g);

                SatEnable.IsChecked = g.SaturationEnabled;
                SatSlider.Value = g.Saturation;
                SatValue.Text = g.Saturation + "%";
                PositionSatBubble();
            }

            bool nv = _engine.Vibrance.Available;
            SatEnable.IsEnabled = has && nv;
            SatSlider.IsEnabled = has && nv && g != null && g.SaturationEnabled;
            SatSlider.Opacity = SatSlider.IsEnabled ? 1.0 : 0.45;
            UpdateSatOutside();
            SatUnavailable.Visibility = nv ? Visibility.Collapsed : Visibility.Visible;
            SatUnavailable.Text = Loc.T("Saturação indisponível: precisa de uma placa NVIDIA. A resolução funciona normalmente.");

            _loading = before;
        }

        private void GameCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (ReferenceEquals(GameCombo.SelectedItem, NewGameItem))
            {
                // Volta para o jogo anterior e abre o "Novo jogo".
                bool before = _loading;
                _loading = true;
                GameCombo.SelectedItem = S.Games.FirstOrDefault(x => x.Id == S.SelectedGameId) ?? S.Games.FirstOrDefault();
                _loading = before;
                Dispatcher.BeginInvoke(new Action(AddGame));
                return;
            }
            LoadSelected();
            QueueSave();
        }

        private void Proc_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading || Selected == null) return;
            Selected.ProcessName = ProcBox.Text.Trim();
            UpdateProcUi(Selected);
            QueueSave();
        }

        /// <summary>Clicar no campo do processo também abre o seletor de arquivos.</summary>
        private void ProcBox_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            BrowseForExe();
        }

        /// <summary>Mostra o aviso laranja quando falta o executável e atualiza o alerta na lista de jogos.</summary>
        private void UpdateProcUi(GameConfig g)
        {
            ProcHint.Text = g.HasProblem
                ? Loc.T("Selecione o .exe para detectar este jogo.")
                : Loc.T("Clique no campo para escolher o .exe do jogo.");
            if (g.HasProblem) ProcHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warn");
            else ProcHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
            bool before = _loading;
            _loading = true;
            // Os ícones da lista leem HasProblem/Enabled uma vez só; trocar a seleção força a leitura de novo.
            GameCombo.Items.Refresh();
            GameCombo.SelectedItem = null;
            GameCombo.SelectedItem = g;
            _loading = before;
        }

        private void Detect_Click(object sender, RoutedEventArgs e)
        {
            if (_loading || Selected == null) return;
            Selected.Enabled = DetectSwitch.IsChecked == true;
            UpdatePauseUi(Selected);
            UpdateProcUi(Selected);      // atualiza o ícone de pausa na lista de jogos
            QueueSave();
        }

        /// <summary>Chave "Detectar este jogo": desligada, o jogo é ignorado e os cartões ficam apagados.</summary>
        private void UpdatePauseUi(GameConfig g)
        {
            DetectSwitch.IsChecked = g.Enabled;
            PausedNote.Visibility = g.Enabled ? Visibility.Collapsed : Visibility.Visible;
            ScreenCard.Opacity = g.Enabled ? 1.0 : 0.5;
            SatCard.Opacity = g.Enabled ? 1.0 : 0.5;
        }

        private void Res_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || Selected == null) return;
            if (ResCombo.SelectedItem is DisplayMode m)
            {
                Selected.Width = m.Width;
                Selected.Height = m.Height;
                FillHz(Selected);
                QueueSave();
            }
        }

        /// <summary>Opção da lista de taxas de atualização. Hz = 0 significa "manter a atual".</summary>
        private sealed class HzItem
        {
            public int Hz { get; set; }
            public string Label { get; set; } = "";
            public override string ToString() => Label;
        }

        /// <summary>Preenche a lista de Hz com o que o monitor aceita na resolução escolhida.</summary>
        private void FillHz(GameConfig g)
        {
            bool before = _loading;
            _loading = true;
            string device = DisplayService.GetPrimaryDevice();
            MonitorSnapshot? cur = DisplayService.GetCurrent(device);
            var items = new List<HzItem>
            {
                new HzItem { Hz = 0, Label = cur != null ? Loc.F("Manter atual ({0} Hz)", cur.Hz) : Loc.T("Manter atual") }
            };
            List<int> rates = DisplayService.ListRefreshRates(device, g.Width, g.Height);
            if (g.Hz > 0 && !rates.Contains(g.Hz))
            {
                // Essa resolução não aceita o Hz salvo: volta para "manter atual".
                g.Hz = 0;
            }
            foreach (int r in rates)
            {
                items.Add(new HzItem { Hz = r, Label = r + " Hz" });
            }
            HzCombo.ItemsSource = items;
            HzCombo.SelectedItem = items.First(x => x.Hz == g.Hz);
            _loading = before;
        }

        private void Hz_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || Selected == null) return;
            if (HzCombo.SelectedItem is HzItem h)
            {
                Selected.Hz = h.Hz;
                QueueSave();
            }
        }

        private void SatBubbleHost_SizeChanged(object sender, SizeChangedEventArgs e) => PositionSatBubble();

        /// <summary>O valor da saturação acompanha a bolinha do slider.</summary>
        private void PositionSatBubble()
        {
            if (SatBubbleHost == null || SatSlider == null) return;
            double w = SatBubbleHost.ActualWidth;
            if (w <= 0) return;
            SatValue.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tw = SatValue.DesiredSize.Width;
            double ratio = SatSlider.Maximum > SatSlider.Minimum ? (SatSlider.Value - SatSlider.Minimum) / (SatSlider.Maximum - SatSlider.Minimum) : 0;
            double x = 9 + (w - 18) * ratio - tw / 2;
            Canvas.SetLeft(SatValue, Math.Max(0, Math.Min(w - tw, x)));
        }

        private void Sat_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || Selected == null) return;
            int v = (int)Math.Round(SatSlider.Value);
            Selected.Saturation = v;
            SatValue.Text = v + "%";
            PositionSatBubble();
            QueueSave();
        }

        private void AddGame()
        {
            var dlg = new PromptWindow("Nome do jogo", "") { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var cur = Selected;
            var g = new GameConfig
            {
                Name = dlg.Value,
                ProcessName = "",
                Width = cur?.Width ?? 1280,
                Height = cur?.Height ?? 960,
                Saturation = 50,
                Enabled = true
            };
            S.Games.Add(g);
            ReloadGames(g.Id);
            QueueSave();
            BrowseForExe(); // já pede o executável do jogo novo
            ProcBox.Focus();
        }

        private void SatEnable_Click(object sender, RoutedEventArgs e)
        {
            if (_loading || Selected == null) return;
            Selected.SaturationEnabled = SatEnable.IsChecked == true;
            SatSlider.IsEnabled = Selected.SaturationEnabled && _engine.Vibrance.Available;
            SatSlider.Opacity = SatSlider.IsEnabled ? 1.0 : 0.45;
            QueueSave();
        }

        private void Browse_Click(object sender, RoutedEventArgs e) => BrowseForExe();

        /// <summary>Abre o seletor de arquivos e usa só o NOME do .exe escolhido (é o que o app compara).</summary>
        private void BrowseForExe()
        {
            if (Selected == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Escolha o executável do jogo (.exe)"),
                Filter = Loc.T("Programas (*.exe)|*.exe|Todos os arquivos (*.*)|*.*"),
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                ProcBox.Text = Path.GetFileName(dlg.FileName);   // dispara Proc_Changed e salva
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var g = Selected;
            if (g == null) return;
            if (S.Games.Count <= 1)
            {
                System.Windows.MessageBox.Show(Loc.T("Mantenha pelo menos um jogo na lista."), "GBooster");
                return;
            }
            if (System.Windows.MessageBox.Show(Loc.F("Excluir “{0}”?\nVocê pode adicionar o jogo de novo depois em “Novo jogo…”.", g.Name), "GBooster", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;
            }
            S.Games.Remove(g);
            ReloadGames(null);
            QueueSave();
        }

        /// <summary>"Testar": mostra a configuração por alguns segundos e volta sozinho. Clicar de novo cancela.</summary>
        private void Test_Click(object sender, RoutedEventArgs e)
        {
            if (_previewLeft > 0)
            {
                EndPreview(true);
                return;
            }

            GameConfig? g = Selected;
            if (g == null) return;

            string? error = _engine.Apply(g, 0, true);
            if (error != null)
            {
                System.Windows.MessageBox.Show(error, "GBooster");
                return;
            }
            _previewLeft = PreviewSeconds;
            TestText.Text = Loc.F("Testando… {0} s", _previewLeft);
            _previewTimer.Start();
        }

        private void PreviewTick()
        {
            // Se algo já restaurou (atalho, bandeja, jogo abriu de verdade), só encerra o contador.
            if (!_engine.IsActive || _engine.Current == null || !_engine.Current.Manual)
            {
                EndPreview(false);
                return;
            }
            _previewLeft--;
            if (_previewLeft <= 0)
            {
                EndPreview(true);
                return;
            }
            TestText.Text = Loc.F("Testando… {0} s", _previewLeft);
        }

        private void EndPreview(bool restore)
        {
            _previewTimer.Stop();
            _previewLeft = 0;
            if (restore && _engine.IsActive && _engine.Current != null && _engine.Current.Manual)
            {
                _engine.RestoreNow(false);
            }
            TestText.Text = Loc.T("Testar resolução");
        }

        private void HowTo_Click(object sender, RoutedEventArgs e)
        {
            new NoticeWindow { Owner = this }.ShowDialog();
        }

        // ------------------------------------------------------------------
        // Geral
        // ------------------------------------------------------------------
        private void LoadGeneral()
        {
            StartCheck.IsChecked = S.StartWithWindows;
            TrayCheck.IsChecked = S.CloseToTray;
            NotifyCheck.IsChecked = S.NotifyOnChange;
            SplashCheck.IsChecked = S.ShowSplash;
            LangCombo.SelectedIndex = Loc.English ? 1 : 0;
            LoadWaitCombo();
        }

        private void General_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool start = StartCheck.IsChecked == true;
            if (start != S.StartWithWindows)
            {
                S.StartWithWindows = start;
                AutoStart.Set(start);
            }
            S.CloseToTray = TrayCheck.IsChecked == true;
            S.NotifyOnChange = NotifyCheck.IsChecked == true;
            S.ShowSplash = SplashCheck.IsChecked == true;
            QueueSave();
        }

        private void Lang_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || LangCombo.SelectedIndex < 0) return;
            string? keep = Selected?.Id;
            S.Language = LangCombo.SelectedIndex == 1 ? "en" : "pt";
            Loc.Init(S.Language);
            Loc.Apply(this);
            _loading = true;
            LoadGeneral();
            _loading = false;
            ReloadGames(keep ?? S.SelectedGameId);
            Tab_Checked(this, new RoutedEventArgs());
            RefreshAll();
            QueueSave();
        }

        private static readonly int[] WaitOptions = { 3, 5, 10, 15, 20, 30 };

        private void LoadWaitCombo()
        {
            bool before = _loading;
            _loading = true;
            var opts = WaitOptions.ToList();
            if (!opts.Contains(S.WaitAfterOpenSeconds)) { opts.Add(S.WaitAfterOpenSeconds); opts.Sort(); }
            WaitCombo.ItemsSource = opts.Select(n => n + " s").ToList();
            WaitCombo.SelectedIndex = opts.IndexOf(S.WaitAfterOpenSeconds);
            _loading = before;
        }

        private void Wait_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || WaitCombo.SelectedItem is not string t) return;
            if (int.TryParse(t.Replace(" s", ""), out int n))
            {
                S.WaitAfterOpenSeconds = n;
                QueueSave();
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T("Exportar configurações"),
                FileName = Loc.T("GBooster-configuracoes.json"),
                Filter = Loc.T("Backup do GBooster (*.json)|*.json")
            };
            if (dlg.ShowDialog(this) != true) return;
            Store.SaveSettings(S);
            string? err = Store.Export(S, dlg.FileName);
            System.Windows.MessageBox.Show(err == null ? Loc.F("Configurações salvas em:\n{0}", dlg.FileName) : Loc.F("Não foi possível salvar: {0}", err), "GBooster");
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_engine.IsActive)
            {
                System.Windows.MessageBox.Show(Loc.T("Há uma tela alterada agora. Restaure a tela (ou feche o jogo) antes de importar."), "GBooster");
                return;
            }
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Importar configurações"),
                Filter = Loc.T("Backup do GBooster (*.json)|*.json")
            };
            if (dlg.ShowDialog(this) != true) return;
            AppSettings? imp = Store.Import(dlg.FileName, out string? err);
            if (imp == null)
            {
                System.Windows.MessageBox.Show(err ?? Loc.T("Arquivo inválido."), "GBooster");
                return;
            }
            if (System.Windows.MessageBox.Show(Loc.T("Isto substitui TODAS as configurações atuais (jogos e preferências). Continuar?"),
                    "GBooster", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            bool wasStart = S.StartWithWindows;
            S.CopyFrom(imp);
            if (S.StartWithWindows != wasStart) AutoStart.Set(S.StartWithWindows);
            Store.SaveSettings(S);
            LoadGeneral();
            ReloadGames(S.SelectedGameId);
            RefreshAll();
        }

        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // Log
        // ------------------------------------------------------------------
        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            string text = string.Join(Environment.NewLine,
                _engine.Log.Select(l => l.TimeText + "  " + l.Text + (string.IsNullOrEmpty(l.Detail) ? "" : "  (" + l.Detail + ")")));
            try { System.Windows.Clipboard.SetText(text); } catch { /* área de transferência ocupada */ }
        }

        private void FullLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = System.IO.Path.Combine(Store.Folder, "historico.txt");
                string text = string.Join(Environment.NewLine,
                    _engine.Log.Select(l => l.Time.ToString("dd/MM/yyyy HH:mm:ss") + "  " + l.Text + (string.IsNullOrEmpty(l.Detail) ? "" : "  (" + l.Detail + ")")));
                File.WriteAllText(path, text);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Loc.F("Não consegui abrir o histórico: {0}", ex.Message), "GBooster");
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e) => _engine.ClearLog();
    }
}
