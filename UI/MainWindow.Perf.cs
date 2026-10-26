using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GBooster.Core;
using GBooster.Core.Boost;

namespace GBooster.UI
{
    /// <summary>Página Desempenho: selo de estado + botão Otimizar, ponto de restauração, pausa das atualizações e a lista do que o Otimizar faz.</summary>
    public partial class MainWindow
    {
        private enum PerfMode { Main, Running, Result }

        private sealed class PerfData
        {
            public List<TweakState> States { get; set; } = new List<TweakState>();
            public RestoreInfo Restore { get; set; } = new RestoreInfo();
            public bool WuPaused { get; set; }
            public bool HasBackup { get; set; }
            /// <summary>Ajustes que o Otimizar tinha deixado aplicados e que o Windows desfez.</summary>
            public List<BoostAction> Undone { get; set; } = new List<BoostAction>();
        }

        private sealed class PerfSummary
        {
            public string Badge = "";
            public string BadgeBrush = "Brush.TextPrimary";
            public string DotBrush = "Brush.StateIdle";
            public string Reasons = "";
            public bool Optimized;
        }

        private PerfMode _perfMode = PerfMode.Main;
        private PerfData? _perf;
        private RestoreInfo? _restore;
        private bool _restoreBusy;
        private OptimizeSummary? _lastRun;
        private string _perfNote = "";
        private bool _perfNoteOk = true;
        private bool _perfAccOpen;
        private int _perfVer;

        private void InitBoost()
        {
            BoostCatalog.GameProvider = () => S.Games
                .Where(g => !string.IsNullOrWhiteSpace(g.ProcessName))
                .Select(g => new KeyValuePair<string, string>(g.ProcessName, g.Name))
                .ToList();
            TimerService.Sync();
        }

        // ------------------------------------------------------------------
        // Trava: com um dos jogos cadastrados aberto, não mexe no Windows.
        // ------------------------------------------------------------------
        private string? OpenGameName()
        {
            try
            {
                var names = new HashSet<string>(
                    S.Games.Where(g => !string.IsNullOrWhiteSpace(g.ProcessName)).Select(g => g.ProcessName),
                    StringComparer.OrdinalIgnoreCase);
                if (names.Count == 0) return null;
                Dictionary<string, uint> running = ProcessScanner.Snapshot(names);
                if (running.Count == 0) return null;
                foreach (GameConfig g in S.Games)
                {
                    if (!string.IsNullOrWhiteSpace(g.ProcessName) && running.ContainsKey(g.ProcessName)) return g.Name;
                }
                return "?";
            }
            catch
            {
                return null;
            }
        }

        private bool GameOpen() => OpenGameName() != null;

        /// <summary>Faixa amarela: diz qual jogo está aberto e o que fica bloqueado por causa dele.</summary>
        private static UIElement GameOpenBanner(string game, string what)
        {
            var col = new StackPanel();
            string title = game == "?" ? Loc.T("Um jogo da sua lista está aberto") : Loc.F("{0} está aberto", game);
            col.Children.Add(UiKit.Text(title, 13, "Brush.Warn", true));
            col.Children.Add(UiKit.Hint(what + " " + Loc.T("Feche o jogo e volte a esta página."), 3));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            System.Windows.Shapes.Path ic = UiKit.WarnIcon();
            ic.Margin = new Thickness(0, 0, 12, 0);
            ic.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(ic);
            row.Children.Add(col);
            Border b = UiKit.WarnCard(row);
            b.Margin = new Thickness(0, 0, 0, 12);
            return b;
        }

        // ------------------------------------------------------------------
        // Atualização da página
        // ------------------------------------------------------------------
        private async void RefreshPerf()
        {
            if (_perfMode != PerfMode.Main) return;
            int ver = ++_perfVer;
            ShowPerfMain();

            bool admin = AdminHelper.IsAdmin;
            PerfData data = await Task.Run(() =>
            {
                var d = new PerfData();
                foreach (BoostAction a in BoostCatalog.All) d.States.Add(a.GetState());
                d.WuPaused = BoostCatalog.WindowsUpdate.GetState() == TweakState.Applied;
                d.HasBackup = BoostCatalog.All.Any(a => a.HasBackup());
                d.Restore = admin ? RestorePointService.Query() : new RestoreInfo { State = RestoreState.Unknown };
                return d;
            });
            if (ver != _perfVer || _perfMode != PerfMode.Main) return;
            IReadOnlyList<BoostAction> all = BoostCatalog.All;
            for (int i = 0; i < all.Count && i < data.States.Count; i++)
            {
                if (S.BoostApplied.Contains(all[i].Id) && !IsSkipped(all[i])
                    && data.States[i] != TweakState.Applied && data.States[i] != TweakState.NotApplicable)
                {
                    data.Undone.Add(all[i]);
                }
            }
            _perf = data;
            _restore = data.Restore;
            ShowPerfMain();
        }

        private void RedrawPerfIfMain()
        {
            if (_perfMode == PerfMode.Main) ShowPerfMain();
        }

        private void SetPerfNote(string text, bool ok)
        {
            _perfNote = text;
            _perfNoteOk = ok;
            RedrawPerfIfMain();
        }

        private bool IsSkipped(BoostAction a) => S.BoostSkip.Contains(a.Id);

        /// <summary>Conta só os ajustes que valem neste PC e que estão marcados.</summary>
        private void CountSelected(out int total, out int applied, out int applicable)
        {
            total = 0; applied = 0; applicable = 0;
            if (_perf == null) return;
            IReadOnlyList<BoostAction> all = BoostCatalog.All;
            for (int i = 0; i < all.Count && i < _perf.States.Count; i++)
            {
                TweakState st = _perf.States[i];
                if (st == TweakState.NotApplicable) continue;
                applicable++;
                if (IsSkipped(all[i])) continue;
                total++;
                if (st == TweakState.Applied) applied++;
            }
        }

        private PerfSummary ComputePerfSummary(bool admin)
        {
            var s = new PerfSummary();
            if (_perf == null)
            {
                s.Badge = Loc.T("VERIFICANDO…");
                return s;
            }

            CountSelected(out int total, out int applied, out int applicable);
            if (applicable > 0 && total == 0)
            {
                s.Badge = Loc.T("NADA MARCADO");
                s.Reasons = Loc.T("Marque ao menos um ajuste na lista “O que otimizar”.");
                return s;
            }
            int pending = total - applied;
            bool restoreKnown = admin && _restore != null && _restore.State != RestoreState.Unknown;
            bool restoreOk = _restore != null && _restore.Valid;

            var reasons = new List<string>();
            if (pending == 1) reasons.Add(Loc.T("1 ajuste pendente"));
            else if (pending > 1) reasons.Add(Loc.F("{0} ajustes pendentes", pending));
            if (restoreKnown && !restoreOk)
            {
                reasons.Add(_restore!.State == RestoreState.Expired ? Loc.T("ponto de restauração vencido") : Loc.T("sem ponto de restauração"));
            }

            if (reasons.Count > 0)
            {
                s.Badge = Loc.T("PRECISA DE OTIMIZAÇÃO");
                s.BadgeBrush = "Brush.Warn";
                s.DotBrush = "Brush.Warn";
                s.Reasons = string.Join(" · ", reasons);
            }
            else if (!admin)
            {
                s.Badge = Loc.T("OTIMIZADO");
                s.BadgeBrush = "Brush.StateActive";
                s.DotBrush = "Brush.StateActive";
                s.Reasons = Loc.T("Todos os ajustes aplicados. Abra como administrador para conferir o ponto de restauração.");
                s.Optimized = true;
            }
            else if (!restoreKnown)
            {
                s.Badge = Loc.T("NÃO VERIFICADO");
                s.Reasons = Loc.T("Os ajustes estão em dia, mas o ponto de restauração não pôde ser conferido.");
                s.Optimized = true;
            }
            else
            {
                s.Badge = Loc.T("OTIMIZADO");
                s.BadgeBrush = "Brush.StateActive";
                s.DotBrush = "Brush.StateActive";
                s.Reasons = Loc.T("Todos os ajustes aplicados e ponto de restauração válido.");
                s.Optimized = true;
            }
            return s;
        }

        // ------------------------------------------------------------------
        // Tela principal
        // ------------------------------------------------------------------
        private void ShowPerfMain()
        {
            double keep = (PagePerf.Content as ScrollViewer)?.VerticalOffset ?? 0;
            bool admin = AdminHelper.IsAdmin;
            string? openGame = OpenGameName();

            var body = new StackPanel { Margin = new Thickness(28, 8, 28, 24) };
            if (openGame != null) body.Children.Add(GameOpenBanner(openGame, Loc.T("Otimizar, desfazer e pausar atualizações ficam bloqueados enquanto o jogo roda, para não mexer no Windows durante a partida.")));
            if (!admin) body.Children.Add(BuildAdminBanner());
            if (_perfNote.Length > 0)
            {
                TextBlock note = UiKit.Text(_perfNote, 12, _perfNoteOk ? "Brush.StateActive" : "Brush.Bad");
                note.Margin = new Thickness(2, 0, 0, 10);
                body.Children.Add(note);
            }
            body.Children.Add(BuildStatusCard(admin, openGame));
            if (_perf != null && _perf.Undone.Count > 0) body.Children.Add(BuildUndoneCard(admin, openGame != null));
            body.Children.Add(BuildRestoreCard(admin));
            body.Children.Add(BuildUpdateCard(admin, openGame != null));
            body.Children.Add(BuildStepsCard());

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
            PagePerf.Content = sv;
            if (keep > 0)
            {
                sv.Dispatcher.BeginInvoke(new Action(() => sv.ScrollToVerticalOffset(keep)), DispatcherPriority.Loaded);
            }
        }

        private UIElement BuildAdminBanner()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            texts.Children.Add(UiKit.Label(Loc.T("MODO ADMINISTRADOR")));
            ((TextBlock)texts.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warn");
            texts.Children.Add(UiKit.Hint(Loc.T("Para otimizar o Windows e conferir o ponto de restauração, o app precisa abrir como administrador. A troca de resolução continua funcionando sem isso."), 4));
            grid.Children.Add(texts);
            Button b = UiKit.Primary(Loc.T("Reiniciar como administrador"), PerfAdmin_Click, 38, 20);
            Grid.SetColumn(b, 1);
            grid.Children.Add(b);
            Border card = UiKit.WarnCard(grid);
            card.Margin = new Thickness(0, 0, 0, 12);
            return card;
        }

        private UIElement BuildStatusCard(bool admin, string? openGame)
        {
            PerfSummary s = ComputePerfSummary(admin);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = UiKit.Dot(16, s.DotBrush);
            dot.Margin = new Thickness(0, 0, 16, 0);
            grid.Children.Add(dot);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(UiKit.DisplayText(s.Badge, 24, s.BadgeBrush));
            if (openGame != null)
            {
                TextBlock lockNote = UiKit.Hint(Loc.T("Feche o jogo para otimizar."), 4);
                lockNote.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warn");
                texts.Children.Add(lockNote);
            }
            else if (s.Reasons.Length > 0)
            {
                texts.Children.Add(UiKit.Hint(s.Reasons, 4));
            }
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            CountSelected(out int selTotal, out _, out _);
            bool canRun = admin && openGame == null && _perf != null && !_restoreBusy && selTotal > 0;
            if (_perf != null && _perf.HasBackup && _perfMode == PerfMode.Main)
            {
                Button undo = UiKit.Link(Loc.T("Desfazer otimizações"), UndoAll_Click);
                undo.Margin = new Thickness(0, 0, 18, 0);
                undo.IsEnabled = admin && openGame == null && !_restoreBusy;
                Grid.SetColumn(undo, 2);
                grid.Children.Add(undo);
            }

            Button run = UiKit.Primary(Loc.T("Otimizar"), Optimize_Click, 46, 32);
            run.FontSize = 15;
            run.IsEnabled = canRun;
            if (!admin) run.ToolTip = Loc.T("Abra o app como administrador para otimizar.");
            // Já otimizado: não há nada a aplicar. Se o Windows desfizer algo, o status muda e o botão volta.
            run.Visibility = s.Optimized ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetColumn(run, 3);
            grid.Children.Add(run);

            Border card = UiKit.Card(grid);
            card.Padding = new Thickness(20);
            return card;
        }

        private UIElement BuildUndoneCard(bool admin, bool locked)
        {
            List<BoostAction> undone = _perf!.Undone;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            texts.Children.Add(UiKit.Label(Loc.T("O WINDOWS DESFEZ AJUSTES")));
            ((TextBlock)texts.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warn");
            string head = undone.Count == 1
                ? Loc.T("1 ajuste voltou ao padrão, provavelmente por uma atualização do Windows:")
                : Loc.F("{0} ajustes voltaram ao padrão, provavelmente por uma atualização do Windows:", undone.Count);
            texts.Children.Add(UiKit.Hint(head, 4));
            texts.Children.Add(UiKit.Text(string.Join(", ", undone.Select(a => Loc.T(a.Title))), 12, "Brush.TextPrimary"));
            texts.Children[2].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
            grid.Children.Add(texts);
            Button again = UiKit.Primary(Loc.T("Aplicar de novo"), Optimize_Click, 38, 20);
            again.IsEnabled = admin && !locked && !_restoreBusy;
            if (!admin) again.ToolTip = Loc.T("Abra o app como administrador para otimizar.");
            Grid.SetColumn(again, 1);
            grid.Children.Add(again);
            Border card = UiKit.WarnCard(grid);
            card.Margin = new Thickness(0, 12, 0, 0);
            return card;
        }

        private static string DaysText(int days)
        {
            if (days <= 0) return Loc.T("hoje");
            if (days == 1) return Loc.T("há 1 dia");
            return Loc.F("há {0} dias", days);
        }

        private UIElement BuildRestoreCard(bool admin)
        {
            string text, note, dot;
            RestoreInfo r = _restore ?? new RestoreInfo();
            bool valid = false;
            if (_restoreBusy)
            {
                dot = "Brush.StateIdle";
                text = Loc.T("Criando o ponto de restauração… (pode levar um minuto)");
                note = "";
            }
            else if (!admin)
            {
                dot = "Brush.StateIdle";
                text = Loc.T("Não consegui verificar");
                note = Loc.T("Abra o app como administrador para o GBooster conferir e criar o ponto.");
            }
            else
            {
                string when = r.Created.Year > 2000 ? r.Created.ToString("g", CultureInfo.CurrentCulture) + " · " : "";
                switch (r.State)
                {
                    case RestoreState.Recent:
                        dot = "Brush.StateActive"; valid = true;
                        text = Loc.F("Criado {0}", DaysText(r.Days));
                        note = when + Loc.T("Em dia. Se algo der errado, dá para voltar.");
                        break;
                    case RestoreState.Old:
                        dot = "Brush.Warn"; valid = true;
                        text = Loc.F("Criado {0}, faz tempo", DaysText(r.Days));
                        note = when + Loc.T("Recomendo criar um ponto novo antes de otimizar de novo.");
                        break;
                    case RestoreState.Expired:
                        dot = "Brush.Bad";
                        text = Loc.F("Vencida: criado {0}", DaysText(r.Days));
                        note = Loc.T("Passou de 60 dias. Crie um ponto novo antes de otimizar.");
                        break;
                    case RestoreState.None:
                        dot = "Brush.Bad";
                        text = Loc.T("Restauração não criada");
                        note = Loc.T("Sem ponto de restauração, não há como voltar o Windows para antes dos ajustes. Crie um antes de otimizar.");
                        break;
                    default:
                        dot = "Brush.StateIdle";
                        text = _perf == null ? Loc.T("Verificando…") : Loc.T("Não consegui verificar");
                        note = string.IsNullOrEmpty(r.Error) ? "" : r.Error;
                        break;
                }
            }

            var inner = new StackPanel();
            inner.Children.Add(UiKit.Label(Loc.T("PONTO DE RESTAURAÇÃO")));

            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var d = UiKit.Dot(10, dot);
            d.Margin = new Thickness(0, 0, 14, 0);
            row.Children.Add(d);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            texts.Children.Add(UiKit.Text(text, 13));
            if (note.Length > 0) texts.Children.Add(UiKit.Hint(note, 3));
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            Button create = UiKit.Btn(valid ? Loc.T("Criar novo ponto") : Loc.T("Criar ponto agora"), RestoreCreate_Click);
            create.IsEnabled = admin && !_restoreBusy && _perfMode == PerfMode.Main;
            Grid.SetColumn(create, 2);
            row.Children.Add(create);
            inner.Children.Add(row);
            return UiKit.Card(inner, 12);
        }

        private UIElement BuildUpdateCard(bool admin, bool locked)
        {
            bool paused = _perf != null && _perf.WuPaused;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            texts.Children.Add(UiKit.Text(Loc.T("Pausar atualizações do Windows"), 13));
            texts.Children.Add(UiKit.Hint(Loc.T("Impede o Windows de baixar, procurar e instalar atualizações, para não atrapalhar o jogo. Vale depois de reiniciar o PC. A página de atualizações do Windows passa a avisar que é gerenciada pela organização: é normal. Dá para ligar de volta aqui a qualquer momento. Lembre de atualizar de vez em quando."), 3));
            grid.Children.Add(texts);

            TextBlock state = UiKit.Hint(paused ? Loc.T("Pausadas") : Loc.T("Normal"));
            state.MinWidth = 64;
            state.TextAlignment = TextAlignment.Right;
            state.VerticalAlignment = VerticalAlignment.Center;
            state.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(state, 1);
            grid.Children.Add(state);

            var sw = new CheckBox { Style = UiKit.Res("SwitchToggle"), IsChecked = paused, VerticalAlignment = VerticalAlignment.Center };
            sw.IsEnabled = admin && !locked && _perf != null && _perfMode == PerfMode.Main;
            if (!admin) sw.ToolTip = Loc.T("Abra o app como administrador para mudar isto.");
            sw.Click += WuSwitch_Click;
            Grid.SetColumn(sw, 2);
            grid.Children.Add(sw);

            return UiKit.Card(grid, 12);
        }

        private UIElement BuildStepsCard()
        {
            CountSelected(out int total, out int applied, out int applicable);
            string sub = applicable == 0
                ? Loc.F("{0} ajustes seguros. Abra para ver e escolher.", BoostCatalog.All.Count)
                : Loc.F("{0} de {1} marcados · {2} já aplicados. Abra para ver e escolher.", total, applicable, applied);
            return UiKit.Accordion(Loc.T("O que otimizar"), sub, BuildStepsList(), _perfAccOpen, open => _perfAccOpen = open, 12);
        }

        private UIElement BuildStepsList()
        {
            var panel = new StackPanel();
            TextBlock tip = UiKit.Hint(Loc.T("Desmarcar um ajuste só o tira do próximo Otimizar: o que já foi aplicado continua. Para voltar atrás, use Desfazer otimizações."));
            tip.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(tip);

            IReadOnlyList<BoostAction> actions = BoostCatalog.All;
            for (int i = 0; i < actions.Count; i++)
            {
                BoostAction a = actions[i];
                TweakState st = _perf != null && i < _perf.States.Count ? _perf.States[i] : TweakState.NotApplied;
                OptimizeLine? line = _lastRun?.Lines.FirstOrDefault(l => l.Id == a.Id);
                bool failed = line != null && line.Status == "Failed";
                bool undone = _perf != null && _perf.Undone.Contains(a);
                bool na = st == TweakState.NotApplicable;

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cb = new CheckBox
                {
                    IsChecked = !na && !IsSkipped(a),
                    IsEnabled = !na && _perfMode == PerfMode.Main,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 12, 0),
                };
                BoostAction captured = a;
                cb.Click += (s2, e2) =>
                {
                    bool on = ((CheckBox)s2).IsChecked == true;
                    if (on) S.BoostSkip.RemoveAll(x => x == captured.Id);
                    else if (!S.BoostSkip.Contains(captured.Id)) S.BoostSkip.Add(captured.Id);
                    SaveSettingsNow();
                    RedrawPerfIfMain();
                };
                row.Children.Add(cb);

                var texts = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                texts.Children.Add(UiKit.Text(Loc.T(a.Title), 13));
                string desc = Loc.T(a.Description);
                if (na) desc = a.NotApplicableText + " " + desc;
                texts.Children.Add(UiKit.Hint(desc, 3));
                if (failed)
                {
                    TextBlock err = UiKit.Text(line!.Message, 11, "Brush.Warn");
                    err.Margin = new Thickness(0, 4, 0, 0);
                    texts.Children.Add(err);
                }
                Grid.SetColumn(texts, 1);
                row.Children.Add(texts);

                var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
                if (failed)
                {
                    status.Children.Add(UiKit.Text(Loc.T("Erro"), 12, "Brush.Warn"));
                }
                else if (undone)
                {
                    status.Children.Add(UiKit.Text(Loc.T("Desfeito pelo Windows"), 12, "Brush.Warn"));
                }
                else if (st == TweakState.Applied)
                {
                    status.Children.Add(UiKit.Text(Loc.T("Aplicado"), 12, "Brush.StateActive"));
                }
                else if (!na)
                {
                    status.Children.Add(UiKit.Text(st == TweakState.Partial ? Loc.T("Parcial") : Loc.T("Pendente"), 12, "Brush.TextTertiary"));
                }
                Grid.SetColumn(status, 2);
                row.Children.Add(status);

                var frame = new Border { Padding = new Thickness(0, 12, 0, 12), BorderThickness = new Thickness(0, 0, 0, i == actions.Count - 1 ? 0 : 1), Child = row };
                frame.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSubtle");
                panel.Children.Add(frame);
            }
            return panel;
        }

        // ------------------------------------------------------------------
        // Ponto de restauração
        // ------------------------------------------------------------------
        private async void RestoreCreate_Click(object sender, RoutedEventArgs e)
        {
            await CreateRestoreAsync();
        }

        private async Task CreateRestoreAsync()
        {
            if (_restoreBusy) return;
            if (!AdminHelper.IsAdmin)
            {
                SetPerfNote(Loc.T("Precisa do modo administrador."), false);
                return;
            }
            _restoreBusy = true;
            RedrawPerfIfMain();
            RestoreResult res = await Task.Run(() => RestorePointService.Create());
            _restore = await Task.Run(() => RestorePointService.Query());
            _restoreBusy = false;

            if (res.Ok)
            {
                _engine.AddLog(Loc.T("Ponto de restauração criado"), "", true, "boost");
                SetPerfNote(Loc.T("Ponto de restauração criado."), true);
            }
            else if (res.TooSoon)
            {
                SetPerfNote(res.Message, true);
            }
            else
            {
                SetPerfNote(Loc.F("Não consegui criar o ponto de restauração: {0}", res.Message), false);
            }
            if (_perf != null) _perf.Restore = _restore;
            RedrawPerfIfMain();
        }

        /// <summary>Antes de otimizar: se não há ponto de restauração válido, oferece criar um. Devolve false se o usuário desistiu.</summary>
        private async Task<bool> EnsureRestoreAsync()
        {
            if (_restore == null || _restore.State == RestoreState.Unknown || _restore.Valid) return true;
            string msg = _restore.State == RestoreState.Expired
                ? Loc.T("O seu último ponto de restauração está vencido (mais de 60 dias).")
                : Loc.T("Você ainda não tem ponto de restauração.");
            msg += "\n\n" + Loc.T("Sim: criar um ponto agora e depois otimizar.\nNão: otimizar sem ponto.\nCancelar: não fazer nada.");
            MessageBoxResult res = System.Windows.MessageBox.Show(msg, "GBooster", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Cancel) return false;
            if (res == MessageBoxResult.No) return true;
            await CreateRestoreAsync();
            return _restore != null && _restore.Valid;
        }

        // ------------------------------------------------------------------
        // Pausar atualizações do Windows
        // ------------------------------------------------------------------
        private async void WuSwitch_Click(object sender, RoutedEventArgs e)
        {
            var sw = (CheckBox)sender;
            bool want = sw.IsChecked == true;
            if (!AdminHelper.IsAdmin)
            {
                SetPerfNote(Loc.T("Precisa do modo administrador."), false);
                return;
            }
            if (GameOpen())
            {
                SetPerfNote(Loc.T("Feche o jogo antes de mudar isto."), false);
                return;
            }
            if (want)
            {
                MessageBoxResult ask = System.Windows.MessageBox.Show(
                    Loc.T("O Windows deixa de baixar e instalar atualizações sozinho, inclusive as de segurança, até você ligar de volta aqui.\n\nPausar as atualizações?"),
                    "GBooster", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ask != MessageBoxResult.Yes)
                {
                    sw.IsChecked = false;
                    return;
                }
            }

            sw.IsEnabled = false;
            TweakResult r = await Task.Run(() => want ? BoostCatalog.WindowsUpdate.Apply() : BoostCatalog.WindowsUpdate.Undo());
            if (r.Ok)
            {
                _engine.AddLog(want ? Loc.T("Atualizações do Windows pausadas") : Loc.T("Atualizações do Windows liberadas"), "", want, "boost");
                string msg = want ? Loc.T("Atualizações do Windows pausadas.") : Loc.T("Atualizações do Windows voltaram ao normal.");
                if (r.NeedsReboot && !r.NoChange) msg += " " + Loc.T("Reinicie o PC para valer.");
                _perfNote = msg;
                _perfNoteOk = true;
            }
            else
            {
                _perfNote = Loc.F("Não consegui mudar as atualizações: {0}", r.Message);
                _perfNoteOk = false;
            }
            RefreshPerf();
        }

        // ------------------------------------------------------------------
        // Otimizar e desfazer
        // ------------------------------------------------------------------
        private async void Optimize_Click(object sender, RoutedEventArgs e)
        {
            await RunOptimizeAsync(false);
        }

        private async void UndoAll_Click(object sender, RoutedEventArgs e)
        {
            if (!AdminHelper.IsAdmin)
            {
                SetPerfNote(Loc.T("Precisa do modo administrador."), false);
                return;
            }
            MessageBoxResult ask = System.Windows.MessageBox.Show(
                Loc.T("Desfazer as otimizações e devolver os valores de antes?\n\nOs apps do Windows que foram removidos não voltam por aqui; para reinstalar, use a Microsoft Store."),
                "GBooster", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;
            await RunOptimizeAsync(true);
        }

        private bool AllApplied()
        {
            CountSelected(out int total, out int applied, out _);
            return total > 0 && total == applied;
        }

        private async Task RunOptimizeAsync(bool undo)
        {
            if (_perfMode == PerfMode.Running) return;
            if (!AdminHelper.IsAdmin)
            {
                SetPerfNote(Loc.T("Precisa do modo administrador."), false);
                return;
            }
            if (GameOpen())
            {
                SetPerfNote(Loc.T("Feche o jogo antes de otimizar."), false);
                return;
            }
            _perfMode = PerfMode.Main;
            if (!undo)
            {
                if (!await EnsureRestoreAsync()) return;
                if (GameOpen())
                {
                    SetPerfNote(Loc.T("Feche o jogo antes de otimizar."), false);
                    return;
                }
            }

            bool force = !undo && AllApplied();
            _perfMode = PerfMode.Running;
            _perfNote = "";
            _lastRun = null;

            UiKit.ProgressBar bar;
            Grid view = UiKit.Loading(
                undo ? Loc.T("DESFAZENDO…") : Loc.T("OTIMIZANDO…"),
                Loc.T("Não feche o app nem inicie um jogo. Você pode usar o PC normalmente."),
                undo ? Loc.T("Pode levar até 1 minuto") : Loc.T("Pode levar até 2 minutos"),
                out bar);
            bar.Update(0, Loc.T("Preparando…"));
            PagePerf.Content = view;

            var progress = new Progress<OptimizeProgress>(p => bar.Update(p.Percent, p.Step));
            OptimizeSummary sum = await Task.Run(() => OptimizeRunner.Run(undo, force, progress, GameOpen, CancellationToken.None, new HashSet<string>(S.BoostSkip)));

            _lastRun = sum;
            if (undo)
            {
                S.BoostApplied.Clear();
            }
            else
            {
                foreach (OptimizeLine l in sum.Lines)
                {
                    if ((l.Status == "Applied" || l.Status == "AlreadyDone") && !S.BoostApplied.Contains(l.Id)) S.BoostApplied.Add(l.Id);
                    if (l.Status == "Failed") S.BoostApplied.RemoveAll(x => x == l.Id);
                }
            }
            SaveSettingsNow();
            _perfMode = PerfMode.Result;
            if (sum.Interrupted)
            {
                _engine.AddLog(undo ? Loc.T("Desfazer interrompido") : Loc.T("Otimização interrompida"), sum.StoppedAt, false, "boost");
            }
            else
            {
                _engine.AddLog(undo ? Loc.T("Otimizações desfeitas") : Loc.T("Otimização concluída"), Loc.F("{0} de {1} ajustes", sum.Done, sum.Total), !undo, "boost");
            }
            ShowPerfResult(sum, undo);
        }

        private void ShowPerfResult(OptimizeSummary sum, bool undo)
        {
            int changed = sum.Lines.Count(l => l.Status == "Applied");
            string title, subtitle, brush;
            Button primary;
            var buttons = new List<Button>();
            buttons.Add(UiKit.Plain(Loc.T("Ver detalhes"), (s, e) => { _perfAccOpen = true; BackToPerfMain(); }));

            if (sum.Interrupted)
            {
                string game = OpenGameName() ?? "";
                string who = game.Length > 0 && game != "?" ? Loc.F("O jogo {0} foi aberto", game) : Loc.T("Um jogo foi aberto");
                title = undo ? Loc.T("DESFAZER INTERROMPIDO") : Loc.T("OTIMIZAÇÃO INTERROMPIDA");
                subtitle = who + " " + (undo ? Loc.T("e o desfazer parou para não atrapalhar.") : Loc.T("e a otimização parou para não atrapalhar."))
                    + " " + Loc.F("{0} de {1} ajustes já estavam aplicados.", sum.Done, sum.Total)
                    + " " + (undo ? Loc.T("Feche o jogo e clique em Continuar desfazendo para terminar.") : Loc.T("Feche o jogo e clique em Continuar otimização para terminar."));
                brush = "Brush.Warn";
                primary = UiKit.Primary(undo ? Loc.T("Continuar desfazendo") : Loc.T("Continuar otimização"), async (s, e) =>
                {
                    _perfMode = PerfMode.Main;
                    await RunOptimizeAsync(undo);
                });
                buttons.Add(primary);
                PagePerf.Content = UiKit.Result(title, subtitle, sum.Percent, Loc.F("Parou em: {0}", sum.StoppedAt), brush, buttons.ToArray());
                return;
            }

            bool warn = sum.Failed > 0;
            if (undo)
            {
                title = warn ? Loc.T("DESFEITO COM AVISOS") : Loc.T("OTIMIZAÇÕES DESFEITAS");
                subtitle = Loc.F("{0} ajustes voltaram ao que eram antes.", changed);
                subtitle += " " + Loc.T("Os apps do Windows removidos não voltam por aqui.");
            }
            else
            {
                title = warn ? Loc.T("OTIMIZAÇÃO CONCLUÍDA COM AVISOS") : Loc.T("COMPUTADOR OTIMIZADO");
                int already = sum.Lines.Count(l => l.Status == "AlreadyDone");
                subtitle = already > 0
                    ? Loc.F("{0} ajustes aplicados agora e {1} que já estavam ativos.", changed, already)
                    : Loc.F("{0} ajustes aplicados agora.", changed);
            }
            if (warn) subtitle += " " + Loc.F("{0} com erro: abra os detalhes.", sum.Failed);
            if (sum.NeedsReboot) subtitle += " " + Loc.T("Reinicie o PC para tudo valer.");
            brush = warn ? "Brush.Warn" : "Brush.StateActive";

            if (sum.NeedsReboot)
            {
                primary = UiKit.Primary(Loc.T("Reiniciar agora"), Reboot_Click);
            }
            else
            {
                primary = UiKit.Primary(Loc.T("OK"), (s, e) => BackToPerfMain());
            }
            buttons.Add(primary);
            Grid resultView = UiKit.Result(title, subtitle, 100, Loc.T("Concluído"), brush, buttons.ToArray());
            // Resumo curto logo abaixo do texto: o que mudou agora e o que já estava ativo.
            var stack = (StackPanel)resultView.Children[0];
            int at = 3;
            string nowList = TitleList(sum, "Applied");
            if (nowList.Length > 0)
            {
                TextBlock t = UiKit.Hint((undo ? Loc.T("Desfeito agora: ") : Loc.T("Aplicado agora: ")) + nowList, 14);
                t.TextAlignment = TextAlignment.Center; t.MaxWidth = 460; t.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Insert(at++, t);
            }
            string keptList = undo ? "" : TitleList(sum, "AlreadyDone");
            if (keptList.Length > 0)
            {
                TextBlock t = UiKit.Hint(Loc.T("Já estava ativo: ") + keptList, 8);
                t.TextAlignment = TextAlignment.Center; t.MaxWidth = 460; t.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Insert(at, t);
            }
            PagePerf.Content = resultView;
        }

        private static string TitleList(OptimizeSummary sum, string status)
        {
            var names = sum.Lines.Where(l => l.Status == status).Select(l => Loc.T(l.Title)).ToList();
            if (names.Count == 0) return "";
            if (names.Count > 5) return string.Join(", ", names.Take(5)) + Loc.F(" e mais {0}", names.Count - 5);
            return string.Join(", ", names);
        }

        private void BackToPerfMain()
        {
            _perfMode = PerfMode.Main;
            RefreshPerf();
        }

        private void Reboot_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult ask = System.Windows.MessageBox.Show(
                Loc.T("O PC vai reiniciar em 15 segundos. Salve o que estiver aberto.\n\nReiniciar agora?"),
                "GBooster", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;
            ShellResult r = Shell.Sys("shutdown.exe", 15000, "/r", "/t", "15", "/c", "GBooster");
            if (!r.Ok)
            {
                System.Windows.MessageBox.Show(Loc.F("Não consegui reiniciar: {0}", r.Brief), "GBooster", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ------------------------------------------------------------------
        // Modo administrador
        // ------------------------------------------------------------------
        private void PerfAdmin_Click(object sender, RoutedEventArgs e)
        {
            string? err = AdminHelper.RelaunchAsAdmin();
            if (err != null)
            {
                SetPerfNote(err, false);
                return;
            }
            ((App)System.Windows.Application.Current).ExitApp();
        }
    }
}
