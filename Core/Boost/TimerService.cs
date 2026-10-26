using System;
using System.Runtime.InteropServices;

namespace GBooster.Core.Boost
{
    /// <summary>
    /// Mantém o relógio do Windows em 0,5 ms enquanto o ajuste "Timing do Windows mais preciso" estiver aplicado.
    /// O pedido vale só enquanto o GBooster está aberto (na bandeja) e é devolvido ao fechar.
    /// </summary>
    public static class TimerService
    {
        [DllImport("ntdll.dll")]
        private static extern int NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

        [DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.Bool)] bool setResolution, out uint currentResolution);

        private const uint WantedUnits = 5000;   // 0,5 ms em unidades de 100 ns
        private static uint _held;

        public static bool IsHolding => _held != 0;

        /// <summary>Relógio atual do Windows em milissegundos (0 se não deu para ler).</summary>
        public static double CurrentMs()
        {
            try
            {
                if (NtQueryTimerResolution(out _, out _, out uint cur) == 0 && cur != 0) return cur / 10000.0;
            }
            catch
            {
                // ntdll sem essa função: segue sem o dado
            }
            return 0;
        }

        public static void Start()
        {
            if (_held != 0) return;
            try
            {
                uint finest = WantedUnits;
                if (NtQueryTimerResolution(out _, out uint max, out _) == 0 && max != 0) finest = Math.Max(WantedUnits, max);
                if (NtSetTimerResolution(finest, true, out _) == 0) _held = finest;
            }
            catch
            {
                // não é essencial
            }
        }

        public static void Stop()
        {
            if (_held == 0) return;
            try { NtSetTimerResolution(_held, false, out _); }
            catch { /* já devolvido */ }
            _held = 0;
        }

        /// <summary>Liga ou desliga conforme o ajuste esteja aplicado.</summary>
        public static void Sync()
        {
            if (BoostCatalog.Timer.HasBackup()) Start(); else Stop();
        }
    }
}
