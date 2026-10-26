using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace GBooster.Core
{
    /// <summary>
    /// Idiomas do app (português e inglês). O texto no código e no XAML é escrito em português;
    /// em inglês, cada frase é trocada pela tradução desta tabela. Frase sem tradução aparece em português.
    /// </summary>
    public static partial class Loc
    {
        public static bool English { get; private set; }

        /// <summary>"auto" (segue o idioma do Windows), "pt" ou "en".</summary>
        public static void Init(string? setting)
        {
            string s = (setting ?? "auto").ToLowerInvariant();
            English = s == "en" || (s != "pt" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "pt");
        }

        public static string T(string pt)
        {
            if (!English || string.IsNullOrEmpty(pt)) return pt;
            if (En.TryGetValue(pt, out string? en)) return en;
            if (EnBoost.TryGetValue(pt, out string? en2)) return en2;
            return pt;
        }

        /// <summary>Traduz o modelo e preenche os {0}, {1}… com os valores.</summary>
        public static string F(string pt, params object[] args) => string.Format(T(pt), args);

        // ------------------------------------------------------------------
        // Troca os textos de uma janela já montada pelo XAML (e volta ao português se for o caso).
        // ------------------------------------------------------------------
        private sealed class Memo { public string Orig = ""; public string Applied = ""; }
        private static readonly ConditionalWeakTable<DependencyObject, Dictionary<string, Memo>> Seen =
            new ConditionalWeakTable<DependencyObject, Dictionary<string, Memo>>();

        public static void Apply(DependencyObject root)
        {
            Walk(root);
        }

        private static void Walk(DependencyObject d)
        {
            if (d is Window w) Tr(d, "Title", w.Title, v => w.Title = v);
            if (d is TextBlock tb) Tr(d, "Text", tb.Text, v => tb.Text = v);
            if (d is ContentControl cc && cc.Content is string c) Tr(d, "Content", c, v => cc.Content = v);
            if (d is FrameworkElement fe && fe.ToolTip is string tip) Tr(d, "ToolTip", tip, v => fe.ToolTip = v);
            foreach (object child in LogicalTreeHelper.GetChildren(d))
            {
                if (child is DependencyObject cd) Walk(cd);
            }
        }

        private static void Tr(DependencyObject d, string prop, string cur, Action<string> set)
        {
            if (string.IsNullOrEmpty(cur)) return;
            Dictionary<string, Memo> map = Seen.GetOrCreateValue(d);
            string orig = cur;
            if (map.TryGetValue(prop, out Memo? m) && m.Applied == cur) orig = m.Orig;
            string res = T(orig);
            map[prop] = new Memo { Orig = orig, Applied = res };
            if (res != cur) set(res);
        }

        // ------------------------------------------------------------------
        // Tabela português → inglês
        // ------------------------------------------------------------------
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            // Menu lateral
            ["AGUARDANDO"] = "WAITING",
            ["DETECTADO"] = "DETECTED",
            ["EM JOGO"] = "IN GAME",
            ["TESTE"] = "TEST",
            ["PAUSA GERAL"] = "ALL PAUSED",
            ["Programa do jogo"] = "Game executable",
            ["Aplicado agora"] = "Applied now",
            ["Tela agora"] = "Display now",
            ["Volta para isto ao sair do jogo"] = "Goes back to this when the game closes",
            ["Restaurar resolução agora"] = "Restore resolution now",
            ["Restaurar resolução e saturação agora"] = "Restore resolution and saturation now",
            ["Restaurar resolução e saturação agora (Ctrl+Alt+R)"] = "Restore resolution and saturation now (Ctrl+Alt+R)",
            ["aplicando em instantes…"] = "applying shortly…",
            ["JOGOS"] = "GAMES",
            ["Jogos"] = "Games",
            ["Histórico"] = "History",
            ["EM BREVE"] = "SOON",
            ["Limpeza"] = "Cleanup",
            ["Desempenho"] = "Performance",
            ["Geral"] = "General",
            ["Minimizar"] = "Minimize",
            ["Fechar"] = "Close",

            // Jogos
            ["JOGO"] = "GAME",
            ["Jogo"] = "Game",
            ["Detectar este jogo"] = "Detect this game",
            ["Desligue para o app ignorar só este jogo. A pausa de todos os jogos fica na bandeja."] =
                "Turn off to make the app ignore just this game. Pausing all games is in the tray menu.",
            ["Excluir este jogo"] = "Delete this game",
            ["Falta escolher o executável deste jogo"] = "This game's executable hasn't been chosen yet",
            ["Detecção deste jogo desligada"] = "Detection is off for this game",
            ["Processo (executável do jogo)"] = "Process (game executable)",
            ["Procurar o executável do jogo (.exe)"] = "Browse for the game executable (.exe)",
            ["Use a lupa para escolher o .exe do jogo."] = "Use the magnifier to pick the game's .exe.",
            ["Selecione o .exe para detectar este jogo."] = "Select the .exe to detect this game.",
            ["A detecção deste jogo está desligada: o app não vai mudar a tela quando ele abrir. Ligue “Detectar este jogo” para voltar a usar."] =
                "Detection is off for this game: the app won't change the display when it opens. Turn on “Detect this game” to use it again.",
            ["TELA NO JOGO"] = "DISPLAY IN GAME",
            ["Mostra esta tela por alguns segundos e depois volta ao normal sozinho"] = "Shows this resolution for a few seconds, then returns to normal by itself",
            ["Testar resolução"] = "Test resolution",
            ["Testando… {0} s"] = "Testing… {0} s",
            ["Resolução"] = "Resolution",
            ["Taxa de atualização"] = "Refresh rate",
            ["Manter atual"] = "Keep current",
            ["Manter atual ({0} Hz)"] = "Keep current ({0} Hz)",
            ["Em “Manter atual” o Hz não muda. Vale no monitor em que o jogo abrir."] =
                "With “Keep current”, the refresh rate doesn't change. It applies to the monitor where the game opens.",
            ["SATURAÇÃO NO JOGO"] = "SATURATION IN GAME",
            ["Mudar a saturação durante este jogo"] = "Change saturation during this game",
            ["O app só muda a saturação durante o jogo aberto."] = "The app only changes saturation while the game is open.",
            ["Saturação indisponível: precisa de uma placa NVIDIA. A resolução funciona normalmente."] =
                "Saturation unavailable: it needs an NVIDIA graphics card. Resolution works normally.",
            ["Fora do jogo volta para {0}% (o valor de antes do jogo)."] = "Outside the game it goes back to {0}% (the value from before the game).",
            ["Fora do jogo fica como você usa hoje ({0}%). O app só muda durante o jogo."] =
                "Outside the game it stays as you use it today ({0}%). The app only changes it during the game.",
            ["＋  Novo jogo…"] = "＋  New game…",
            ["Nome do jogo"] = "Game name",
            ["Escolha o executável do jogo (.exe)"] = "Choose the game executable (.exe)",
            ["Programas (*.exe)|*.exe|Todos os arquivos (*.*)|*.*"] = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            ["Mantenha pelo menos um jogo na lista."] = "Keep at least one game in the list.",
            ["Excluir “{0}”?\nVocê pode adicionar o jogo de novo depois em “Novo jogo…”."] =
                "Delete “{0}”?\nYou can add the game again later with “New game…”.",

            // Geral
            ["COMO O APP FUNCIONA"] = "HOW THE APP WORKS",
            ["Só confere quais programas estão abertos e troca resolução e saturação do monitor. Não lê nem altera o jogo e não injeta código."] =
                "It only checks which programs are running and changes your monitor's resolution and saturation. It doesn't read or modify the game and doesn't inject code.",
            ["Como ativar a escala “Tela inteira” (GPU)"] = "How to enable “Full screen” scaling (GPU)",
            ["SISTEMA"] = "SYSTEM",
            ["Iniciar com o Windows (direto na bandeja)"] = "Start with Windows (straight to the tray)",
            ["Fechar a janela mantém o app na bandeja"] = "Closing the window keeps the app in the tray",
            ["Avisar quando a resolução mudar ou voltar"] = "Notify when the resolution changes or is restored",
            ["Mostrar animação ao abrir o app"] = "Show animation when the app opens",
            ["Idioma"] = "Language",
            ["ATALHO PARA RESTAURAR"] = "RESTORE HOTKEY",
            ["Combine Ctrl, Alt, Shift ou Win com outra tecla."] = "Combine Ctrl, Alt, Shift or Win with another key.",
            ["Alterar"] = "Change",
            ["Cancelar"] = "Cancel",
            ["Clique, depois pressione a nova combinação de teclas"] = "Click, then press the new key combination",
            ["Pressione a combinação…"] = "Press the combination…",
            ["Mantido o atalho atual."] = "Keeping the current hotkey.",
            ["Esc cancela · Delete desativa o atalho."] = "Esc cancels · Delete turns the hotkey off.",
            ["Atalho desativado. Use o botão “Restaurar resolução agora” ou a bandeja."] = "Hotkey turned off. Use the “Restore resolution now” button or the tray.",
            ["Use Ctrl, Alt, Shift ou Win junto com a tecla (para não atrapalhar o jogo)."] =
                "Use Ctrl, Alt, Shift or Win together with the key (so it doesn't get in the game's way).",
            ["Essa combinação já é usada por outro programa. Escolha outra."] = "Another program already uses this combination. Pick a different one.",
            ["Atalho salvo."] = "Hotkey saved.",
            ["Desativado"] = "Off",
            ["ESPERAR ANTES DE APLICAR"] = "WAIT BEFORE APPLYING",
            ["Tempo depois que o jogo abre. Jogos com anti-cheat costumam pedir mais."] = "Delay after the game opens. Games with anti-cheat usually need more.",
            ["BACKUP"] = "BACKUP",
            ["Salve suas configurações num arquivo ou traga de outro PC."] = "Save your settings to a file or bring them from another PC.",
            ["Exportar"] = "Export",
            ["Importar"] = "Import",
            ["Exportar configurações"] = "Export settings",
            ["Importar configurações"] = "Import settings",
            ["GBooster-configuracoes.json"] = "GBooster-settings.json",
            ["Backup do GBooster (*.json)|*.json"] = "GBooster backup (*.json)|*.json",
            ["Configurações salvas em:\n{0}"] = "Settings saved to:\n{0}",
            ["Não foi possível salvar: {0}"] = "Couldn't save: {0}",
            ["Há uma tela alterada agora. Restaure a tela (ou feche o jogo) antes de importar."] =
                "The display is currently changed. Restore it (or close the game) before importing.",
            ["Arquivo inválido."] = "Invalid file.",
            ["Isto substitui TODAS as configurações atuais (jogos e preferências). Continuar?"] =
                "This replaces ALL current settings (games and preferences). Continue?",
            ["Esse arquivo não parece um backup do GBooster."] = "This file doesn't look like a GBooster backup.",
            ["Não consegui ler esse arquivo. Ele precisa ser um backup exportado pelo GBooster."] =
                "Couldn't read this file. It must be a backup exported by GBooster.",

            // Histórico
            ["ÚLTIMOS EVENTOS"] = "LATEST EVENTS",
            ["Copiar"] = "Copy",
            ["Limpar"] = "Clear",
            ["Ver histórico completo (últimos 500 eventos)"] = "View full history (last 500 events)",
            ["Não consegui abrir o histórico: {0}"] = "Couldn't open the history: {0}",

            // Janelas pequenas
            ["Antes de começar"] = "Before you start",
            ["Para a resolução esticada ocupar a tela toda (sem barras pretas), ative a escala “Tela inteira” nas configurações da sua placa de vídeo."] =
                "For the stretched resolution to fill the whole screen (no black bars), turn on “Full screen” scaling in your graphics card settings.",
            ["A configuração de saturação só funciona em placas NVIDIA."] = "The saturation setting only works on NVIDIA graphics cards.",

            // Bandeja
            ["Teste · {0}"] = "Test · {0}",
            ["Em jogo · {0}"] = "In game · {0}",
            ["Detecção geral pausada"] = "All detection paused",
            ["Aguardando jogo"] = "Waiting for a game",
            ["Abrir GBooster"] = "Open GBooster",
            ["Pausar detecção geral"] = "Pause all detection",
            ["Retomar detecção geral"] = "Resume all detection",
            ["Sair"] = "Exit",
            ["Uso do app: CPU {0}%  ·  RAM {1} MB"] = "App usage: CPU {0}%  ·  RAM {1} MB",
            ["Uso do app: indisponível"] = "App usage: unavailable",
            ["GBooster · em jogo {0}×{1}"] = "GBooster · in game {0}×{1}",
            ["GBooster · pausado"] = "GBooster · paused",
            ["GBooster · aguardando o jogo"] = "GBooster · waiting for a game",

            // Histórico e avisos gerados pelo motor
            ["{0} detectado"] = "{0} detected",
            ["jogo “{0}” · aguardando {1} s"] = "game “{0}” · waiting {1} s",
            ["{0} encerrado"] = "{0} closed",
            ["Não consegui ler o monitor {0}."] = "Couldn't read monitor {0}.",
            ["Erro ao aplicar"] = "Error applying",
            ["Hz não aceito"] = "Refresh rate not accepted",
            ["{0} Hz não funciona em {1} × {2}. Voltei para manter o atual."] = "{0} Hz doesn't work at {1} × {2}. Reverted to keeping the current refresh rate.",
            ["Não consegui aplicar a resolução"] = "Couldn't apply the resolution",
            ["Resolução aplicada"] = "Resolution applied",
            ["Saturação não aplicada"] = "Saturation not applied",
            ["Saturação aplicada"] = "Saturation applied",
            ["Saturação ignorada"] = "Saturation skipped",
            ["indisponível"] = "unavailable",
            ["A resolução volta para {0} × {1} quando o jogo fechar."] = "The resolution goes back to {0} × {1} when the game closes.",
            ["A resolução volta ao normal quando o jogo fechar."] = "The resolution goes back to normal when the game closes.",
            ["{0} × {1} para {2}"] = "{0} × {1} for {2}",
            ["Erro ao restaurar a resolução"] = "Error restoring the resolution",
            ["Resolução restaurada"] = "Resolution restored",
            ["Erro ao restaurar a saturação"] = "Error restoring saturation",
            ["Saturação restaurada"] = "Saturation restored",
            ["volta para {0}% (o valor de antes do jogo)"] = "back to {0}% (the value from before the game)",
            ["{0} foi encerrado"] = "{0} was closed",
            ["Resolução voltou para {0} × {1}."] = "Resolution restored to {0} × {1}.",
            ["Resolução original restaurada."] = "Original resolution restored.",
            ["Recuperação"] = "Recovery",
            ["o app tinha fechado com alterações ativas; desfazendo"] = "the app had closed with changes still applied; undoing them",
            ["Erro inesperado"] = "Unexpected error",
            ["Atalho indisponível"] = "Hotkey unavailable",
            ["outro programa já usa essa combinação"] = "another program already uses this combination",
            ["GBooster iniciado"] = "GBooster started",
            ["na bandeja"] = "in the tray",
            ["O monitor não aceita {0} × {1} a {2} Hz (código {3})."] = "The monitor doesn't accept {0} × {1} at {2} Hz (code {3}).",
            ["O Windows recusou a troca (código {0})."] = "Windows refused the change (code {0}).",
            ["Nenhum monitor NVIDIA encontrado."] = "No NVIDIA monitor found.",
            ["Saturação indisponível (precisa de placa NVIDIA): {0}"] = "Saturation unavailable (needs an NVIDIA card): {0}",
            ["Monitor NVIDIA não encontrado."] = "NVIDIA monitor not found.",
            ["Não consegui mudar a saturação: {0}"] = "Couldn't change saturation: {0}",
            ["Não consegui restaurar a saturação: {0}"] = "Couldn't restore saturation: {0}",
        };
    }
}
