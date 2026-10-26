using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace GBooster.Core.Boost
{
    /// <summary>Resultado de uma medição do relógio do Windows.</summary>
    public sealed class TimerMeasure
    {
        public DateTime At { get; set; } = DateTime.Now;
        /// <summary>Timing configurado no Windows (ms) no começo da medição.</summary>
        public double ReportedMs { get; set; }
        /// <summary>Média de um Sleep(1) no momento da medição (ms).</summary>
        public double AvgMs { get; set; }
        public double MaxMs { get; set; }
        /// <summary>true quando o GBooster estava segurando o relógio (ajuste aplicado).</summary>
        public bool Held { get; set; }
    }

    /// <summary>
    /// Mede, sem alterar nada no Windows, quanto tempo um Sleep(1) realmente dura. Com o relógio em 0,5 ms dá cerca de 1 ms;
    /// no padrão do Windows dá cerca de 15,6 ms. Guarda as últimas medições em timer-medidas.json.
    /// </summary>
    public static class TimerMeter
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
        private static string PathFile => System.IO.Path.Combine(Store.Folder, "timer-medidas.json");

        /// <summary>Sleep(1) repetido por <paramref name="millis"/> ms; devolve média e máximo em ms.</summary>
        private static void Sample(int millis, out double avg, out double max)
        {
            double sum = 0; max = 0; int n = 0;
            long start = Stopwatch.GetTimestamp();
            double freq = Stopwatch.Frequency;
            while ((Stopwatch.GetTimestamp() - start) * 1000.0 / freq < millis)
            {
                long t0 = Stopwatch.GetTimestamp();
                Thread.Sleep(1);
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / freq;
                if (n++ < 3) continue;   // as primeiras vezes esquentam e não valem
                sum += ms;
                if (ms > max) max = ms;
            }
            int counted = Math.Max(1, n - 3);
            avg = sum / counted;
        }

        public static TimerMeasure Measure()
        {
            var m = new TimerMeasure { At = DateTime.Now, ReportedMs = TimerService.CurrentMs(), Held = TimerService.IsHolding };
            Sample(3000, out double avg, out double max);
            m.AvgMs = avg; m.MaxMs = max;
            Save(m);
            return m;
        }

        public static List<TimerMeasure> History()
        {
            try
            {
                if (File.Exists(PathFile))
                {
                    var l = JsonSerializer.Deserialize<List<TimerMeasure>>(File.ReadAllText(PathFile));
                    if (l != null) return l;
                }
            }
            catch
            {
                // arquivo corrompido: começa vazio
            }
            return new List<TimerMeasure>();
        }

        private static void Save(TimerMeasure m)
        {
            try
            {
                List<TimerMeasure> l = History();
                l.Insert(0, m);
                if (l.Count > 10) l.RemoveRange(10, l.Count - 10);
                File.WriteAllText(PathFile, JsonSerializer.Serialize(l, Json));
            }
            catch
            {
                // sem histórico: a medição continua valendo na tela
            }
        }
    }
}
