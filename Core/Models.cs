using System;
using System.Collections.Generic;

namespace GBooster.Core
{
    /// <summary>Configuração de um jogo. É isso que a aba "Jogos" edita.</summary>
    public sealed class GameConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Novo jogo";
        /// <summary>Nome do executável, ex.: cs2.exe</summary>
        public string ProcessName { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int Width { get; set; } = 1280;
        public int Height { get; set; } = 960;
        /// <summary>Taxa de atualização durante o jogo. 0 = manter a atual (não mexe no Hz).</summary>
        public int Hz { get; set; } = 0;
        /// <summary>Só mexe na saturação durante este jogo se estiver marcado. Desligado por padrão.</summary>
        public bool SaturationEnabled { get; set; } = false;
        /// <summary>Saturação durante o jogo, de 0 a 100 (mesma escala do Painel NVIDIA).</summary>
        public int Saturation { get; set; } = 50;

        /// <summary>Jogo sem executável escolhido: aparece com alerta na lista.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasProblem => Id != "__new__" && string.IsNullOrWhiteSpace(ProcessName);

        public override string ToString() => Name;
    }

    /// <summary>Preferências do app inteiro (aba "Geral").</summary>
    public sealed class AppSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public List<GameConfig> Games { get; set; } = new List<GameConfig>();
        public string SelectedGameId { get; set; } = "";

        /// <summary>"dark", "light" ou "system".</summary>
        public bool StartWithWindows { get; set; } = false;
        public bool CloseToTray { get; set; } = true;
        public bool NotifyOnChange { get; set; } = true;
        public bool ConfirmOnTest { get; set; } = true;
        /// <summary>Segundos de espera depois que o jogo abre, antes de mexer na tela.</summary>
        public int WaitAfterOpenSeconds { get; set; } = 5;
        public bool PauseDetection { get; set; } = false;
        public bool ScalingNoticeSeen { get; set; } = false;
        /// <summary>Mostra a animação de abertura ao iniciar o app.</summary>
        public bool ShowSplash { get; set; } = true;
        /// <summary>"auto" (idioma do Windows), "pt" ou "en".</summary>
        public string Language { get; set; } = "auto";

        /// <summary>Copia tudo de outro conjunto de configurações para este (usado ao importar um backup).</summary>
        public void CopyFrom(AppSettings o)
        {
            Games = o.Games;
            SelectedGameId = o.SelectedGameId;
            StartWithWindows = o.StartWithWindows;
            CloseToTray = o.CloseToTray;
            NotifyOnChange = o.NotifyOnChange;
            ConfirmOnTest = o.ConfirmOnTest;
            WaitAfterOpenSeconds = o.WaitAfterOpenSeconds;
            PauseDetection = o.PauseDetection;
            ScalingNoticeSeen = o.ScalingNoticeSeen;
            ShowSplash = o.ShowSplash;
            Language = o.Language;
            HotkeyModifiers = o.HotkeyModifiers;
            HotkeyKey = o.HotkeyKey;
        }

        /// <summary>Modificadores do Win32: Alt=1, Ctrl=2, Shift=4, Win=8.</summary>
        public uint HotkeyModifiers { get; set; } = 0x0002 | 0x0001;
        /// <summary>Código de tecla virtual. 0x52 = R.</summary>
        public uint HotkeyKey { get; set; } = 0x52;

        public static AppSettings CreateDefault()
        {
            var s = new AppSettings();
            s.Games.Add(new GameConfig
            {
                Name = "Counter-Strike 2",
                ProcessName = "cs2.exe",
                Enabled = true,
                Width = 1280,
                Height = 960,
                Saturation = 50
            });
            s.Games.Add(new GameConfig
            {
                Name = "Delta Force",
                ProcessName = "DeltaForceClient-Win64-Shipping.exe",
                Enabled = false,
                Width = 1280,
                Height = 960,
                Saturation = 50
            });
            s.Games.Add(new GameConfig
            {
                Name = "Battlefield 6",
                ProcessName = "bf6.exe",
                Enabled = false,
                Width = 1280,
                Height = 960,
                Saturation = 50
            });
            s.SelectedGameId = s.Games[0].Id;
            return s;
        }
    }

    /// <summary>Como estava um monitor antes de o app mexer nele.</summary>
    public sealed class MonitorSnapshot
    {
        public string Device { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int Hz { get; set; }
        public int Bpp { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    /// <summary>Nível de vibrância (valor cru da NVIDIA) de um monitor antes de o app mexer.</summary>
    public sealed class VibranceSnapshot
    {
        public string Device { get; set; } = "";
        public int Level { get; set; }
        /// <summary>O mesmo valor em porcentagem (0 a 100), só para mostrar na tela.</summary>
        public int Percent { get; set; } = 50;
    }

    /// <summary>
    /// O que o app alterou e precisa desfazer. É gravado em disco ANTES de qualquer
    /// alteração, para o app conseguir restaurar tudo mesmo se cair ou o PC reiniciar.
    /// </summary>
    public sealed class AppliedState
    {
        public string GameId { get; set; } = "";
        public string GameName { get; set; } = "";
        public string ProcessName { get; set; } = "";
        /// <summary>True quando veio do botão "Testar agora" (o jogo não precisa estar aberto).</summary>
        public bool Manual { get; set; }
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public List<MonitorSnapshot> Monitors { get; set; } = new List<MonitorSnapshot>();
        public List<VibranceSnapshot> Vibrance { get; set; } = new List<VibranceSnapshot>();
        /// <summary>Texto para a interface: o que foi aplicado.</summary>
        public int AppliedWidth { get; set; }
        public int AppliedHeight { get; set; }
        public int AppliedHz { get; set; }
        /// <summary>-1 quando a saturação não foi alterada.</summary>
        public int AppliedSaturation { get; set; } = -1;
    }
}
