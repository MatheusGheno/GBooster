using System;
using System.Collections.Generic;
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
    /// <summary>Página Limpeza: analisar, escolher o que apagar, limpar.</summary>
    public partial class MainWindow
    {
        private enum CleanMode { Start, Analyzing, Found, Cleaning, Done }

        private CleanMode _cleanMode = CleanMode.Start;
        private List<CleanItem> _cleanItems = new List<CleanItem>();
        private CleanSummary? _cleanSum;
        private string _cleanNote = "";

        private void RefreshClean()
        {
            // Não mexe no meio de uma análise/limpeza; nos outros casos, redesenha (idioma pode ter mudado).
            if (_cleanMode == CleanMode.Analyzing || _cleanMode == CleanMode.Cleaning) return;
            ShowCleanCurrent();
        }

        private void ShowCleanCurrent()
        {
            switch (_cleanMode)
            {
                case CleanMode.Found: ShowCleanFound(); break;
                case CleanMode.Done: ShowCleanDone(); break;
                default: _cleanMode = CleanMode.Start; ShowCleanStart(); break;
            }
        }

        private static string SizeText(CleanItem it)
        {
            if (it.Bytes < 0) return Loc.T("O Windows calcula");
            if (it.Empty || it.Bytes == 0) return Loc.T("Nada a limpar");
            return SystemInfoService.FormatBytes(it.Bytes);
        }

        // ---------------- Início ----------------
        private void ShowCleanStart()
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(UiKit.Mark(false));
            TextBlock t = UiKit.DisplayText(Loc.T("LIMPEZA"), 22, "Brush.TextPrimary");
            t.TextAlignment = TextAlignment.Center;
            t.Margin = new Thickness(0, 26, 0, 0);
            stack.Children.Add(t);
            TextBlock s = UiKit.Hint(Loc.T("Primeiro o GBooster analisa o que dá para apagar e mostra o tamanho. Nada é apagado até você escolher e clicar em Limpar."), 18);
            s.TextAlignment = TextAlignment.Center;
            s.MaxWidth = 460;
            s.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(s);
            if (_cleanNote.Length > 0)
            {
                TextBlock n = UiKit.Text(_cleanNote, 12, "Brush.Warn");
                n.TextAlignment = TextAlignment.Center;
                n.Margin = new Thickness(0, 14, 0, 0);
                n.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(n);
            }
            Button go = UiKit.Primary(Loc.T("Analisar"), CleanAnalyze_Click, 46, 40);
            go.HorizontalAlignment = HorizontalAlignment.Center;
            go.Margin = new Thickness(0, 28, 0, 0);
            string? gameNow = OpenGameName();
            go.IsEnabled = gameNow == null;
            stack.Children.Add(go);
            if (gameNow != null)
            {
                UIElement ban = GameOpenBanner(gameNow, Loc.T("A limpeza fica bloqueada enquanto o jogo roda, para não apagar arquivos que ele esteja usando."));
                ((FrameworkElement)ban).Margin = new Thickness(0, 24, 0, 0);
                ((FrameworkElement)ban).MaxWidth = 480;
                stack.Children.Add(ban);
            }
            var host = new Grid { Margin = new Thickness(28, 0, 28, 24) };
            host.Children.Add(stack);
            PageClean.Content = host;
        }

        // ---------------- Analisando ----------------
        private async void CleanAnalyze_Click(object sender, RoutedEventArgs e)
        {
            if (_cleanMode == CleanMode.Analyzing) return;
            if (GameOpen())
            {
                _cleanNote = Loc.T("Feche o jogo para limpar.");
                ShowCleanStart();
                return;
            }
            _cleanNote = "";
            _cleanMode = CleanMode.Analyzing;
            UiKit.ProgressBar bar;
            PageClean.Content = UiKit.Loading(Loc.T("ANALISANDO…"), Loc.T("Procurando arquivos que podem ser apagados. Nada é apagado agora."), Loc.T("Pode levar até 1 minuto"), out bar);
            bar.Update(0, Loc.T("Preparando…"));

            List<CleanItem> items = CleanupService.Catalog();
            var progress = new Progress<CleanProgress>(p => bar.Update(p.Percent, p.Step));
            await Task.Run(() => CleanupService.Analyze(items, progress, CancellationToken.None));

            _cleanItems = items;
            _cleanMode = CleanMode.Found;
            ShowCleanFound();
        }

        // ---------------- Resultado da análise ----------------
        private long SelectedBytes() => _cleanItems.Where(i => i.Selected && i.Bytes > 0).Sum(i => i.Bytes);

        private void ShowCleanFound()
        {
            bool admin = AdminHelper.IsAdmin;
            string? openGame = OpenGameName();
            var body = new StackPanel { Margin = new Thickness(28, 8, 28, 24) };

            if (_cleanNote.Length > 0)
            {
                TextBlock note = UiKit.Text(_cleanNote, 12, "Brush.Warn");
                note.Margin = new Thickness(2, 0, 0, 10);
                body.Children.Add(note);
            }
            if (openGame != null)
            {
                body.Children.Add(GameOpenBanner(openGame, Loc.T("A limpeza fica bloqueada enquanto o jogo roda, para não apagar arquivos que ele esteja usando.")));
            }

            var list = new StackPanel();
            TextBlock total = UiKit.DisplayText("", 20, "Brush.TextPrimary");
            Button clean = UiKit.Primary(Loc.T("Limpar selecionados"), CleanRun_Click, 42, 26);

            Action refreshTotal = () =>
            {
                long b = SelectedBytes();
                int n = _cleanItems.Count(i => i.Selected);
                total.Text = n == 0 ? Loc.T("Nada selecionado") : (b > 0 ? SystemInfoService.FormatBytes(b) : Loc.T("Selecionado"));
                clean.IsEnabled = n > 0 && openGame == null;
            };

            for (int i = 0; i < _cleanItems.Count; i++)
            {
                CleanItem it = _cleanItems[i];
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cb = new CheckBox { IsChecked = it.Selected, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
                bool blocked = it.Empty || (it.NeedsAdmin && !admin);
                if (blocked)
                {
                    cb.IsEnabled = false;
                    cb.IsChecked = false;
                    it.Selected = false;
                }
                CleanItem captured = it;
                cb.Click += (s, e) =>
                {
                    captured.Selected = ((CheckBox)s).IsChecked == true;
                    refreshTotal();
                };
                row.Children.Add(cb);

                var texts = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                texts.Children.Add(UiKit.Text(Loc.T(it.Title), 13));
                string desc = Loc.T(it.Description);
                texts.Children.Add(UiKit.Hint(desc, 3));
                if (it.NeedsAdmin && !admin)
                {
                    Button adm = UiKit.Link(Loc.T("Precisa do modo administrador. Reiniciar como administrador"), PerfAdmin_Click);
                    adm.HorizontalAlignment = HorizontalAlignment.Left;
                    adm.Margin = new Thickness(0, 4, 0, 0);
                    texts.Children.Add(adm);
                }
                Grid.SetColumn(texts, 1);
                row.Children.Add(texts);

                TextBlock size = UiKit.Text(SizeText(it), 12, blocked ? "Brush.TextTertiary" : "Brush.TextPrimary", !blocked && it.Bytes > 0);
                size.VerticalAlignment = VerticalAlignment.Top;
                size.Margin = new Thickness(0, 2, 0, 0);
                Grid.SetColumn(size, 2);
                row.Children.Add(size);

                var frame = new Border { Padding = new Thickness(0, 12, 0, 12), BorderThickness = new Thickness(0, 0, 0, i == _cleanItems.Count - 1 ? 0 : 1), Child = row };
                frame.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSubtle");
                list.Children.Add(frame);
            }

            // Cabeçalho: total + ações
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ht = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ht.Children.Add(UiKit.Label(Loc.T("PODE SER LIMPO")));
            total.Margin = new Thickness(0, 4, 0, 0);
            ht.Children.Add(total);
            head.Children.Add(ht);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button again = UiKit.Link(Loc.T("Analisar de novo"), CleanAnalyze_Click);
            again.Margin = new Thickness(0, 0, 18, 0);
            actions.Children.Add(again);
            actions.Children.Add(clean);
            Grid.SetColumn(actions, 1);
            head.Children.Add(actions);

            body.Children.Add(UiKit.Card(head));
            body.Children.Add(UiKit.Card(list, 12));
            refreshTotal();
            PageClean.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        }

        // ---------------- Limpando ----------------
        private async void CleanRun_Click(object sender, RoutedEventArgs e)
        {
            if (_cleanMode == CleanMode.Cleaning) return;
            if (GameOpen())
            {
                _cleanNote = Loc.T("Feche o jogo para limpar.");
                ShowCleanFound();
                return;
            }
            var chosen = _cleanItems.Where(i => i.Selected).ToList();
            if (chosen.Count == 0) return;
            if (chosen.Any(i => i.Id == "restore"))
            {
                MessageBoxResult ask = System.Windows.MessageBox.Show(
                    Loc.T("Isso apaga os pontos de restauração antigos, menos o mais recente. Depois não dá para voltar o Windows a essas datas.\n\nContinuar?"),
                    "GBooster", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ask != MessageBoxResult.Yes) return;
            }

            _cleanNote = "";
            _cleanMode = CleanMode.Cleaning;
            UiKit.ProgressBar bar;
            PageClean.Content = UiKit.Loading(Loc.T("LIMPANDO…"), Loc.T("Não feche o app nem inicie um jogo. Você pode usar o PC normalmente."), Loc.T("Pode levar alguns minutos"), out bar);
            bar.Update(0, Loc.T("Preparando…"));

            var progress = new Progress<CleanProgress>(p => bar.Update(p.Percent, p.Step));
            List<CleanItem> items = _cleanItems;
            CleanSummary sum = await Task.Run(() => CleanupService.Clean(items, progress, GameOpen, CancellationToken.None));

            _cleanSum = sum;
            _cleanMode = CleanMode.Done;
            long freed = sum.Freed;
            _engine.AddLog(sum.Interrupted ? Loc.T("Limpeza interrompida") : Loc.T("Limpeza concluída"),
                freed > 0 ? Loc.F("{0} liberados", SystemInfoService.FormatBytes(freed)) : "", !sum.Interrupted, "boost");
            ShowCleanResult(sum);
        }

        private void ShowCleanResult(CleanSummary sum)
        {
            var buttons = new List<Button>();
            buttons.Add(UiKit.Plain(Loc.T("Ver detalhes"), (s, e) => ShowCleanDone()));
            string title, subtitle, brush;
            if (sum.Interrupted)
            {
                title = Loc.T("LIMPEZA INTERROMPIDA");
                subtitle = Loc.T("Um jogo foi aberto e a limpeza parou para não atrapalhar.") + " "
                    + Loc.F("{0} liberados até aqui.", SystemInfoService.FormatBytes(sum.Freed));
                brush = "Brush.Warn";
                buttons.Add(UiKit.Primary(Loc.T("Analisar de novo"), (s, e) => { _cleanMode = CleanMode.Start; ShowCleanStart(); }));
            }
            else
            {
                title = Loc.T("LIMPEZA CONCLUÍDA");
                subtitle = Loc.F("{0} liberados.", SystemInfoService.FormatBytes(sum.Freed));
                if (sum.Skipped > 0) subtitle += " " + Loc.F("{0} arquivos estavam em uso e foram pulados.", sum.Skipped);
                brush = "Brush.StateActive";
                buttons.Add(UiKit.Primary(Loc.T("OK"), (s, e) => { _cleanMode = CleanMode.Start; ShowCleanStart(); }));
            }
            PageClean.Content = UiKit.Result(title, subtitle, sum.Interrupted ? sum.Percent : 100,
                sum.Interrupted ? Loc.F("Parou em: {0}", sum.StoppedAt) : Loc.T("Concluído"), brush, buttons.ToArray());
        }

        // ---------------- Limpeza concluída (detalhes) ----------------
        private void ShowCleanDone()
        {
            _cleanMode = CleanMode.Done;
            var body = new StackPanel { Margin = new Thickness(28, 8, 28, 24) };

            var list = new StackPanel();
            for (int i = 0; i < _cleanItems.Count; i++)
            {
                CleanItem it = _cleanItems[i];
                bool did = it.Selected;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                FrameworkElement mark = did ? (FrameworkElement)UiKit.CheckIcon() : UiKit.Dot(8, "Brush.StateIdle");
                mark.VerticalAlignment = VerticalAlignment.Top;
                mark.HorizontalAlignment = HorizontalAlignment.Left;
                mark.Margin = new Thickness(0, 3, 0, 0);
                row.Children.Add(mark);

                var texts = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                texts.Children.Add(UiKit.Text(Loc.T(it.Title), 13, did ? "Brush.TextPrimary" : "Brush.TextTertiary"));
                texts.Children.Add(UiKit.Hint(Loc.T(it.Description), 3));
                Grid.SetColumn(texts, 1);
                row.Children.Add(texts);

                string right = did
                    ? (it.Freed > 0 ? Loc.F("Limpo · {0}", SystemInfoService.FormatBytes(it.Freed)) : Loc.T("Limpo"))
                    : Loc.T("Não selecionado");
                TextBlock r = UiKit.Text(right, 12, did ? "Brush.StateActive" : "Brush.TextTertiary");
                r.VerticalAlignment = VerticalAlignment.Top;
                r.Margin = new Thickness(0, 2, 0, 0);
                Grid.SetColumn(r, 2);
                row.Children.Add(r);

                var frame = new Border { Padding = new Thickness(0, 12, 0, 12), BorderThickness = new Thickness(0, 0, 0, i == _cleanItems.Count - 1 ? 0 : 1), Child = row };
                frame.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSubtle");
                list.Children.Add(frame);
            }

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ht = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ht.Children.Add(UiKit.Label(Loc.T("LIMPEZA CONCLUÍDA")));
            TextBlock total = UiKit.DisplayText(Loc.F("{0} liberados", SystemInfoService.FormatBytes(_cleanSum?.Freed ?? 0)), 20, "Brush.StateActive");
            total.Margin = new Thickness(0, 4, 0, 0);
            ht.Children.Add(total);
            head.Children.Add(ht);
            Button ok = UiKit.Primary(Loc.T("OK"), (s, e) => { _cleanMode = CleanMode.Start; ShowCleanStart(); }, 42, 30);
            Grid.SetColumn(ok, 1);
            head.Children.Add(ok);

            body.Children.Add(UiKit.Card(head));
            body.Children.Add(UiKit.Card(list, 12));
            PageClean.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        }
    }
}
