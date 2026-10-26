using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using GBooster.Core;
using GBooster.Core.Boost;

namespace GBooster.UI
{
    /// <summary>Página Meu sistema: só mostra informações do PC (nada é alterado).</summary>
    public partial class MainWindow
    {
        private SystemSummary? _sysInfo;
        private int _sysVer;
        private string _sysNote = "";
        private bool _timerBusy;
        private string _timerNote = "";
        private List<TimerMeasure>? _timerHist;

        private async void RefreshSystem()
        {
            int ver = ++_sysVer;
            if (_sysInfo != null)
            {
                ShowSystem(_sysInfo);
            }
            else
            {
                UiKit.ProgressBar bar;
                PageSystem.Content = UiKit.Loading(Loc.T("LENDO O SISTEMA…"), Loc.T("Só leitura: nada é alterado no seu PC."), "", out bar, false);
            }
            SystemSummary info = await Task.Run(() => SystemInfoService.Read());
            if (ver != _sysVer) return;
            _sysInfo = info;
            ShowSystem(info);
        }

        private void ShowSystem(SystemSummary info)
        {
            double keepScroll = (PageSystem.Content as ScrollViewer)?.VerticalOffset ?? 0;
            var body = new StackPanel { Margin = new Thickness(28, 8, 28, 24) };

            var top = new Grid { Margin = new Thickness(2, 0, 0, 10) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(UiKit.Hint(_sysNote.Length > 0 ? _sysNote : Loc.T("Informações do seu PC. Só leitura: nada é alterado.")));
            Button copy = UiKit.IconBtn(Loc.T("Copiar resumo"), "M7.5,5.5 H11.7 A1.8,1.8 0 0 1 13.5,7.3 V11.7 A1.8,1.8 0 0 1 11.7,13.5 H7.3 A1.8,1.8 0 0 1 5.5,11.7 V7.5 A2,2 0 0 1 7.5,5.5 Z M10.5,3.5 V3.3 A1.8,1.8 0 0 0 8.7,1.5 H4.3 A1.8,1.8 0 0 0 2.5,3.3 V7.7 A1.8,1.8 0 0 0 4.3,9.5 H4.5", SystemCopy_Click);
            Button notepad = UiKit.IconBtn(Loc.T("Abrir no Bloco de Notas"), "M4,2 H9.5 L12,4.5 V14 H4 Z M9.5,2 V4.5 H12 M6,8 H10 M6,10.5 H10", SystemNotepad_Click);
            notepad.Margin = new Thickness(0, 0, 8, 0);
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            btns.Children.Add(notepad);
            btns.Children.Add(copy);
            Grid.SetColumn(btns, 1);
            top.Children.Add(btns);
            body.Children.Add(top);

            if (info.Error.Length > 0)
            {
                body.Children.Add(UiKit.WarnCard(UiKit.Text(Loc.T("Não consegui ler tudo: ") + info.Error, 12, "Brush.Warn")));
            }

            body.Children.Add(SystemSection(Loc.T("PROCESSADOR E MEMÓRIA"), info.Cpu, 0));
            body.Children.Add(SystemSection(Loc.T("PLACA DE VÍDEO"), info.Gpu, 12));

            // Discos
            var disks = new StackPanel();
            disks.Children.Add(UiKit.Label(Loc.T("DISCOS")));
            if (info.Disks.Count == 0)
            {
                disks.Children.Add(UiKit.Hint(Loc.T("Nenhum disco encontrado."), 8));
            }
            foreach (DiskInfo d in info.Disks)
            {
                bool full = d.Percent > 85;
                var row = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(UiKit.Text(d.Letter + ":" + (d.Kind.Length > 0 ? "  ·  " + d.Kind : ""), 13));
                TextBlock use = UiKit.Hint(SystemInfoService.FormatBytes(d.Used) + " / " + SystemInfoService.FormatBytes(d.Total) + "  ·  " + d.Percent + "%");
                Grid.SetColumn(use, 1);
                line.Children.Add(use);
                row.Children.Add(line);

                var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 8, 0, 0) };
                track.SetResourceReference(Border.BackgroundProperty, "Brush.Field");
                var fill = new Grid();
                fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(d.Percent, 0.0001), GridUnitType.Star) });
                fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - d.Percent, 0), GridUnitType.Star) });
                var f = new Border { CornerRadius = new CornerRadius(4) };
                f.SetResourceReference(Border.BackgroundProperty, full ? "Brush.Warn" : "Brush.Accent");
                fill.Children.Add(f);
                track.Child = fill;
                row.Children.Add(track);
                if (full) row.Children.Add(UiKit.Text(Loc.T("Pouco espaço livre. Use a página Limpeza."), 11, "Brush.Warn"));
                disks.Children.Add(row);
            }
            body.Children.Add(UiKit.Card(disks, 12));

            body.Children.Add(SystemSection("Windows", info.Windows, 12));
            body.Children.Add(BuildTimerCard());

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
            PageSystem.Content = sv;
            if (keepScroll > 0)
            {
                sv.Dispatcher.BeginInvoke(new Action(() => sv.ScrollToVerticalOffset(keepScroll)), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private static UIElement SystemSection(string title, System.Collections.Generic.List<InfoRow> rows, double top)
        {
            var panel = new StackPanel();
            panel.Children.Add(UiKit.Label(title));
            var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < rows.Count; i++)
            {
                InfoRow r = rows[i];
                list.Children.Add(UiKit.KeyValue(r.Label, r.Value, r.Tag.Length > 0 ? r.Tag : null, r.TagTip.Length > 0 ? r.TagTip : null, r.Good, i == rows.Count - 1));
            }
            panel.Children.Add(list);
            return UiKit.Card(panel, top);
        }

        private static string MsText(double ms) => ms.ToString("0.00", CultureInfo.CurrentCulture) + " ms";

        private const double TimerGoodMs = 1.5;

        /// <summary>Passa a última medição para o resumo, para ir junto no "Copiar" e no Bloco de Notas.</summary>
        private void SyncTimerRows()
        {
            if (_sysInfo == null) return;
            _sysInfo.Timer.Clear();
            TimerMeasure? m = (_timerHist ?? TimerMeter.History()).FirstOrDefault();
            if (m == null) return;
            _sysInfo.Timer.Add(new InfoRow { Label = Loc.T("Medido em"), Value = m.At.ToString("g", CultureInfo.CurrentCulture) });
            _sysInfo.Timer.Add(new InfoRow { Label = Loc.T("Timing configurado no Windows"), Value = MsText(m.ReportedMs) });
            _sysInfo.Timer.Add(new InfoRow { Label = Loc.T("Timing medido"), Value = MsText(m.AvgMs) });
        }

        /// <summary>Barra proporcional ao tempo medido (escala de 0 a 16 ms) com as marcas de "ótimo" e "padrão".</summary>
        private static UIElement TimerBar(double ms, string brush)
        {
            var box = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            double frac = Math.Max(0.03, Math.Min(1.0, ms / 16.0));
            var bar = new Grid { Height = 8 };
            var track = new Border { CornerRadius = new CornerRadius(4) };
            track.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSubtle");
            bar.Children.Add(track);
            var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
            fill.SetResourceReference(Border.BackgroundProperty, brush);
            bar.Children.Add(fill);
            bar.SizeChanged += (s, e) => fill.Width = Math.Max(4, bar.ActualWidth * frac);
            box.Children.Add(bar);

            var scale = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            scale.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scale.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            scale.Children.Add(UiKit.Hint(Loc.T("0 ms · ótimo")));
            TextBlock end = UiKit.Hint(Loc.T("15,6 ms · padrão do Windows"));
            Grid.SetColumn(end, 1);
            scale.Children.Add(end);
            box.Children.Add(scale);
            return box;
        }

        private UIElement BuildTimerCard()
        {
            if (_timerHist == null) _timerHist = TimerMeter.History();
            TimerMeasure? last = _timerHist.FirstOrDefault();

            var inner = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tx = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            tx.Children.Add(UiKit.Label(Loc.T("TIMING DO WINDOWS")));
            tx.Children.Add(UiKit.Hint(Loc.T("Timing é o intervalo entre cada “tique” do Windows. Ele decide com que precisão o jogo e os programas esperam o próximo quadro: quanto menor, melhor. Perto de 1 ms é ótimo e 15,6 ms é o padrão do Windows. A medida só observa e não altera nada."), 4));
            head.Children.Add(tx);
            Button measure = _timerBusy
                ? UiKit.BusyBtn(Loc.T("Medindo… (3 s)"))
                : UiKit.IconBtn(Loc.T("Medir agora"), "M8,2.5 V8 L11,10 M8,1.5 A6.5,6.5 0 1 1 7.99,1.5", TimerMeasure_Click);
            measure.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(measure, 1);
            head.Children.Add(measure);
            inner.Children.Add(head);

            if (_timerNote.Length > 0)
            {
                TextBlock n = UiKit.Text(_timerNote, 12, "Brush.Warn");
                n.Margin = new Thickness(0, 10, 0, 0);
                inner.Children.Add(n);
            }

            if (last != null)
            {
                bool good = last.AvgMs < TimerGoodMs;
                string verdict; string brush;
                if (good) { verdict = Loc.T("Ótimo: o timing está preciso."); brush = "Brush.StateActive"; }
                else if (last.Held) { verdict = Loc.T("O ajuste está aplicado, mas o timing não chegou a 0,5 ms.") + "\n" + Loc.T("Reinicie o PC e meça de novo."); brush = "Brush.Warn"; }
                else { verdict = Loc.T("Timing no padrão do Windows.") + "\n" + Loc.T("Aplique o Otimizar na página Desempenho e meça de novo."); brush = "Brush.Warn"; }

                // Resultado: número grande + veredito
                var result = new Grid { Margin = new Thickness(0, 16, 0, 0) };
                result.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                result.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var big = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                big.Children.Add(UiKit.DisplayText(MsText(last.AvgMs), 30, brush));
                big.Children.Add(UiKit.Hint(Loc.T("Timing medido"), 2));
                result.Children.Add(big);
                var vt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                vt.Children.Add(UiKit.Text(verdict, 13, brush));
                vt.Children.Add(UiKit.Hint(Loc.T("Medido em") + " " + last.At.ToString("g", CultureInfo.CurrentCulture) + " · " + Loc.T("Timing configurado no Windows") + ": " + MsText(last.ReportedMs), 4));
                Grid.SetColumn(vt, 1);
                result.Children.Add(vt);
                inner.Children.Add(result);
                inner.Children.Add(TimerBar(last.AvgMs, brush));

                if (_timerHist.Count > 1)
                {
                    inner.Children.Add(UiKit.Divider(14));
                    var hist = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
                    hist.Children.Add(UiKit.Label(Loc.T("MEDIDAS ANTERIORES")));
                    foreach (TimerMeasure old in _timerHist.Skip(1).Take(3))
                    {
                        var line = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        line.Children.Add(UiKit.Hint(old.At.ToString("g", CultureInfo.CurrentCulture)));
                        TextBlock ov = UiKit.Text(MsText(old.AvgMs), 12, old.AvgMs < TimerGoodMs ? "Brush.StateActive" : "Brush.TextPrimary");
                        Grid.SetColumn(ov, 1);
                        line.Children.Add(ov);
                        hist.Children.Add(line);
                    }
                    inner.Children.Add(hist);
                }
            }
            return UiKit.Card(inner, 12);
        }

        private async void TimerMeasure_Click(object sender, RoutedEventArgs e)
        {
            if (_timerBusy || _sysInfo == null) return;
            if (GameOpen())
            {
                _timerNote = Loc.T("Feche o jogo para medir.");
                ShowSystem(_sysInfo);
                return;
            }
            _timerNote = "";
            _timerBusy = true;
            ShowSystem(_sysInfo);
            try
            {
                await Task.Run(() => TimerMeter.Measure());
            }
            catch (Exception ex)
            {
                _timerNote = Loc.F("Não consegui medir: {0}", ex.Message);
            }
            _timerBusy = false;
            _timerHist = TimerMeter.History();
            if (_sysInfo != null) ShowSystem(_sysInfo);
        }

        private void SystemNotepad_Click(object sender, RoutedEventArgs e)
        {
            if (_sysInfo == null) return;
            SyncTimerRows();
            try
            {
                string path = System.IO.Path.Combine(Store.Folder, "meu-sistema.txt");
                System.IO.File.WriteAllText(path, _sysInfo.ToReport(), new System.Text.UTF8Encoding(true));
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
                _sysNote = "";
            }
            catch
            {
                _sysNote = Loc.T("Não consegui abrir o Bloco de Notas.");
                ShowSystem(_sysInfo);
            }
        }

        private void SystemCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_sysInfo == null) return;
            SyncTimerRows();
            try
            {
                System.Windows.Clipboard.SetText(_sysInfo.ToText());
                _sysNote = Loc.T("Resumo copiado.");
            }
            catch
            {
                _sysNote = Loc.T("Não consegui copiar.");
            }
            ShowSystem(_sysInfo);
        }
    }
}
