using System;
using System.Windows;
using System.Windows.Controls;
using GBooster.Core;

namespace GBooster.UI
{
    /// <summary>Página Ajuda (botão ? na barra de cima): explicações que antes ocupavam as telas.</summary>
    public partial class MainWindow
    {
        private RadioButton? _helpReturn;
        private readonly bool[] _helpOpen = new bool[5];

        private static readonly string[][][] HelpData =
        {
            new[]
            {
                new[] { "O que o GBooster faz", "Troca de tela, ajustes do Windows, limpeza e informações do PC." },
                new[] { "Troca de tela por jogo", "Quando um jogo cadastrado abre, o app troca a resolução do monitor (e, se você quiser, a taxa de atualização e a saturação) e devolve tudo ao fechar o jogo." },
                new[] { "Desempenho", "Aplica 12 ajustes do Windows de uma vez, com ponto de restauração e Desfazer. Só roda quando você clica em Otimizar." },
                new[] { "Limpeza", "Analisa e mostra o tamanho de temporários e caches, e só apaga o que você marcar." },
                new[] { "Meu sistema", "Mostra processador, memória, placa de vídeo, discos e Windows, e mede o timing do Windows. Só leitura: nada é alterado." },
            },
            new[]
            {
                new[] { "Como funciona e segurança", "O que o app faz e o que ele nunca faz." },
                new[] { "Não toca no jogo", "Só confere quais programas estão abertos. Não abre o processo do jogo, não lê nem escreve na memória e não injeta código." },
                new[] { "Sem internet, conta ou telemetria", "Nada sai do seu computador. Os dados ficam na pasta GBooster, dentro do AppData." },
                new[] { "Volta ao normal", "A tela volta ao normal ao fechar o jogo, ao usar Restaurar resolução agora (na janela ou na bandeja) ou ao fechar o app. Se o PC cair, o app desfaz na próxima abertura." },
                new[] { "Restaurar com o jogo aberto", "Se você clicar em Restaurar resolução agora com o jogo ainda aberto, o app para de trocar a tela nesse jogo até você fechá-lo. Ao abrir o jogo de novo, a troca volta ao normal." },
                new[] { "Ajustes do Windows são desfeitos por você", "Cada ajuste guarda o valor de antes. O botão Desfazer otimizações devolve tudo, menos os apps do Windows removidos." },
            },
            new[]
            {
                new[] { "O que precisa", "Requisitos e quando é preciso ser administrador." },
                new[] { "Windows 10 ou 11, 64 bits", "Não precisa instalar o .NET." },
                new[] { "Modo administrador", "Necessário para otimizar, pausar atualizações, criar ponto de restauração e alguns itens da Limpeza. A troca de tela funciona sem isso." },
                new[] { "Placa NVIDIA", "Só a saturação e o perfil da NVIDIA dependem dela. O resto funciona em qualquer placa." },
                new[] { "Escala “Tela inteira”", "Para a resolução esticada ocupar a tela toda, sem barras pretas, ative essa escala nas configurações da placa de vídeo." },
            },
            new[]
            {
                new[] { "Para quem é", "Quem joga competitivo e quer mais fluidez." },
                new[] { "Jogadores competitivos", "Quem usa resolução diferente da nativa e cansou de trocar nas configurações do Windows toda vez que abre e fecha o jogo." },
                new[] { "Quem quer o PC mais leve para jogar", "Os ajustes do Desempenho e a Limpeza reduzem o que roda em segundo plano e liberam espaço." },
            },
            new[]
            {
                new[] { "Cuidados e compatibilidade", "O que pode acontecer em alguns PCs." },
                new[] { "Anti-cheat", "Nenhuma desenvolvedora publica uma lista de programas permitidos. O app não faz o que as regras públicas proíbem, mas a decisão final é do anti-cheat. A saturação é a parte mais sensível: use por sua conta e risco." },
                new[] { "Notebook", "Tela sempre ligada, PCIe sem economia e timing de 0,5 ms gastam mais bateria e esquentam mais." },
                new[] { "Outras ferramentas", "ISLC, Wub, Timer Resolution e MSI util, se continuarem abertas ou instaladas, podem disputar os mesmos ajustes com o GBooster." },
                new[] { "App da NVIDIA", "O app da NVIDIA ou o GeForce Experience com “otimizar jogos” ligado pode sobrescrever o perfil dos jogos." },
                new[] { "Atualizações do Windows", "Atualizações grandes podem desfazer parte dos ajustes. O selo da página Desempenho mostra o estado real." },
                new[] { "Modo MSI", "Na placa de vídeo e no USB, em poucos PCs, causa chiado no áudio ou travadas. Dá para desfazer, mas pede reinício." },
                new[] { "HDD no lugar de SSD", "Abrir programas pode ficar um pouco mais lento sem o Superfetch." },
            },
        };

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            if (PageHelp.Visibility == Visibility.Visible)
            {
                HelpBack_Click(sender, e);
                return;
            }
            RadioButton[] tabs = { TabGames, TabLog, TabPerf, TabClean, TabGeneral, TabSystem };
            _helpReturn = tabs[0];
            foreach (RadioButton t in tabs)
            {
                if (t.IsChecked == true) _helpReturn = t;
            }
            foreach (RadioButton t in tabs) t.IsChecked = false;
            PageGames.Visibility = Visibility.Collapsed;
            PageGeneral.Visibility = Visibility.Collapsed;
            PageLog.Visibility = Visibility.Collapsed;
            PagePerf.Visibility = Visibility.Collapsed;
            PageClean.Visibility = Visibility.Collapsed;
            PageSystem.Visibility = Visibility.Collapsed;
            PageTitle.Text = Loc.T("Ajuda");
            BuildHelp();
            PageHelp.Visibility = Visibility.Visible;
            UpdateHelpBtn();
        }

        /// <summary>O botão ? fica aceso enquanto a Ajuda está aberta.</summary>
        private void UpdateHelpBtn()
        {
            bool on = PageHelp.Visibility == Visibility.Visible;
            if (on)
            {
                HelpBtn.SetResourceReference(Control.BackgroundProperty, "Brush.NavActive");
                HelpBtn.SetResourceReference(Control.ForegroundProperty, "Brush.TextAccent");
            }
            else
            {
                HelpBtn.ClearValue(Control.BackgroundProperty);
                HelpBtn.ClearValue(Control.ForegroundProperty);
            }
        }

        private void HelpTour_Click(object sender, RoutedEventArgs e)
        {
            HelpBack_Click(sender, e);
            Dispatcher.BeginInvoke(new Action(StartTour), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void HelpBack_Click(object sender, RoutedEventArgs e)
        {
            RadioButton back = _helpReturn ?? TabGames;
            back.IsChecked = true;   // dispara Tab_Checked, que esconde a ajuda e mostra a página
        }

        private void BuildHelp()
        {
            var body = new StackPanel { Margin = new Thickness(28, 8, 28, 24) };
            Button back = UiKit.IconBtn(Loc.T("Voltar"), "M13,8 H3 M7,4 L3,8 L7,12", HelpBack_Click);
            Button tour = UiKit.IconBtn(Loc.T("Repetir tour"), "M4.5,3 L12.5,8 L4.5,13 Z", HelpTour_Click);
            tour.Margin = new Thickness(8, 0, 0, 0);
            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var leftRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            leftRow.Children.Add(back);
            leftRow.Children.Add(tour);
            topRow.Children.Add(leftRow);
            Button tips = UiKit.IconBtn(Loc.T("Dicas antes de usar"), "M8,1.8 A6.2,6.2 0 1 1 7.99,1.8 M8,7 V11.2 M8,4.8 H8.01", HowTo_Click);
            Grid.SetColumn(tips, 1);
            topRow.Children.Add(tips);
            body.Children.Add(topRow);

            for (int i = 0; i < HelpData.Length; i++)
            {
                string[][] sec = HelpData[i];
                var list = new StackPanel();
                for (int j = 1; j < sec.Length; j++)
                {
                    var row = new StackPanel { Margin = new Thickness(0, j == 1 ? 0 : 12, 0, 0) };
                    row.Children.Add(UiKit.Text(Loc.T(sec[j][0]), 13, "Brush.TextPrimary", true));
                    row.Children.Add(UiKit.Hint(Loc.T(sec[j][1]), 3));
                    list.Children.Add(row);
                }
                int idx = i;
                var host = new Border { Padding = new Thickness(0, 4, 0, 10), Child = list };
                body.Children.Add(UiKit.Accordion(Loc.T(sec[0][0]), Loc.T(sec[0][1]), host, _helpOpen[idx], open => _helpOpen[idx] = open, 12));
            }
            PageHelp.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        }
    }
}
