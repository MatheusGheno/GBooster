using System;
using System.Collections.Generic;
using System.Linq;
using NvAPIWrapper;
using NvDisplay = NvAPIWrapper.Display.Display;

namespace GBooster.Core
{
    /// <summary>
    /// Saturação (Digital Vibrance) pela API pública da NVIDIA, via a biblioteca
    /// NvAPIWrapper.Net (LGPL-3.0, https://github.com/falahati/NvAPIWrapper).
    /// É o mesmo ajuste do "Vibrância digital" do Painel de Controle NVIDIA.
    /// Sem placa NVIDIA, tudo aqui falha em silêncio e o app segue só com a resolução.
    /// </summary>
    public sealed class VibranceService
    {
        private bool _initialized;

        public bool Available { get; private set; }
        public string? Problem { get; private set; }

        public void Init()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;
            try
            {
                NVIDIA.Initialize();
                Available = NvDisplay.GetDisplays().Length > 0;
                if (!Available)
                {
                    Problem = Loc.T("Nenhum monitor NVIDIA encontrado.");
                }
            }
            catch (Exception ex)
            {
                Available = false;
                Problem = Loc.F("Saturação indisponível (precisa de placa NVIDIA): {0}", ex.Message);
            }
        }

        public void Shutdown()
        {
            if (!Available)
            {
                return;
            }
            try { NVIDIA.Unload(); } catch { /* ignorar */ }
        }

        /// <summary>Acha o monitor NVIDIA que corresponde ao nome do Windows (\\.\DISPLAYn).</summary>
        private NvDisplay? Find(string device)
        {
            var displays = NvDisplay.GetDisplays();
            if (displays.Length == 0)
            {
                return null;
            }

            foreach (var d in displays)
            {
                try
                {
                    string name = d.Name ?? "";
                    if (name.Equals(device, StringComparison.OrdinalIgnoreCase))
                    {
                        return d;
                    }
                }
                catch { /* próximo */ }
            }

            // Plano B: com um único monitor NVIDIA não há dúvida; com vários, usa o n-ésimo.
            if (displays.Length == 1)
            {
                return displays[0];
            }
            var all = DisplayService.GetAttachedDevices();
            int idx = all.FindIndex(x => x.Equals(device, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 && idx < displays.Length ? displays[idx] : displays[0];
        }

        /// <summary>Lê o nível atual (valor cru) para poder restaurar depois.</summary>
        public VibranceSnapshot? Read(string device)
        {
            if (!Available) return null;
            try
            {
                var d = Find(device);
                if (d == null) return null;
                var dvc = d.DigitalVibranceControl;
                int level = dvc.CurrentLevel;
                return new VibranceSnapshot
                {
                    Device = device,
                    Level = level,
                    Percent = LevelToPercent(level, dvc.MinimumLevel, dvc.DefaultLevel, dvc.MaximumLevel)
                };
            }
            catch { return null; }
        }

        /// <summary>percent: 0 a 100, onde 50 = padrão do driver.</summary>
        public string? SetPercent(string device, int percent)
        {
            if (!Available) return Problem;
            try
            {
                var d = Find(device);
                if (d == null) return Loc.T("Monitor NVIDIA não encontrado.");
                var dvc = d.DigitalVibranceControl;
                int min = dvc.MinimumLevel;
                int max = dvc.MaximumLevel;
                int def = dvc.DefaultLevel;
                dvc.CurrentLevel = PercentToLevel(percent, min, def, max);
                return null;
            }
            catch (Exception ex)
            {
                return Loc.F("Não consegui mudar a saturação: {0}", ex.Message);
            }
        }

        public string? SetLevel(string device, int level)
        {
            if (!Available) return Problem;
            try
            {
                var d = Find(device);
                if (d == null) return Loc.T("Monitor NVIDIA não encontrado.");
                d.DigitalVibranceControl.CurrentLevel = level;
                return null;
            }
            catch (Exception ex)
            {
                return Loc.F("Não consegui restaurar a saturação: {0}", ex.Message);
            }
        }

        /// <summary>0..50 interpola mínimo→padrão; 50..100 interpola padrão→máximo.</summary>
        public static int PercentToLevel(int percent, int min, int def, int max)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            double level = percent <= 50
                ? min + (def - min) * (percent / 50.0)
                : def + (max - def) * ((percent - 50) / 50.0);
            return (int)Math.Round(level);
        }

        /// <summary>Operação inversa, só para mostrar na tela.</summary>
        public static int LevelToPercent(int level, int min, int def, int max)
        {
            if (level >= def)
            {
                return max == def ? 50 : (int)Math.Round(50 + 50.0 * (level - def) / (max - def));
            }
            return def == min ? 50 : (int)Math.Round(50.0 * (level - min) / (def - min));
        }
    }
}
