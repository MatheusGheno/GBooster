using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace GBooster.Core.Boost
{
    /// <summary>Lista dos ajustes do Otimizar, na ordem em que são feitos. Para criar um novo, é só acrescentar aqui.</summary>
    public static class BoostCatalog
    {
        /// <summary>Quem sabe a lista de jogos (nome, executável) devolve aqui; a NVIDIA grava um perfil para cada um.</summary>
        public static Func<List<KeyValuePair<string, string>>>? GameProvider { get; set; }

        private static readonly TimerAction TimerInstance = new TimerAction();
        private static readonly WindowsUpdateAction WuInstance = new WindowsUpdateAction();
        private static List<BoostAction>? _all;

        public static BoostAction Timer => TimerInstance;

        /// <summary>Botão à parte "Pausar atualizações do Windows" (não faz parte do Otimizar).</summary>
        public static BoostAction WindowsUpdate => WuInstance;

        public static IReadOnlyList<BoostAction> All
        {
            get
            {
                if (_all == null)
                {
                    _all = new List<BoostAction>
                    {
                        new RecordingOffAction(),
                        new PowerPlanAction(),
                        new HibernateAction(),
                        TimerInstance,
                        new GamePriorityAction(),
                        new NtfsAction(),
                        new MemoryAction(),
                        new SnappyAction(),
                        new TelemetryAction(),
                        new AppsAction(),
                        new NvidiaAction(),
                        new InputAction(),
                    };
                }
                return _all;
            }
        }

        /// <summary>Estados de todos os ajustes (rápido; não altera nada).</summary>
        public static OptimizeStatus Status()
        {
            var st = new OptimizeStatus();
            foreach (BoostAction a in All)
            {
                TweakState s = a.GetState();
                if (s == TweakState.NotApplicable) { st.Skipped++; continue; }
                st.Total++;
                if (s == TweakState.Applied) st.Applied++;
            }
            return st;
        }
    }

    public sealed class OptimizeStatus
    {
        /// <summary>Ajustes que valem neste PC.</summary>
        public int Total { get; set; }
        public int Applied { get; set; }
        /// <summary>Ajustes que não se aplicam a este PC.</summary>
        public int Skipped { get; set; }
        public int Pending => Total - Applied;
        public bool AllApplied => Total > 0 && Applied == Total;
    }

    public sealed class OptimizeProgress
    {
        public int Index { get; set; }
        public int Count { get; set; }
        public int Percent { get; set; }
        public string Step { get; set; } = "";
    }

    public sealed class OptimizeLine
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        /// <summary>Applied = aplicado/desfeito agora; AlreadyDone = já estava; Skipped = não se aplica; Failed = erro; NotRun = não chegou a rodar.</summary>
        public string Status { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public sealed class OptimizeSummary
    {
        public List<OptimizeLine> Lines { get; } = new List<OptimizeLine>();
        public bool Interrupted { get; set; }
        public string StoppedAt { get; set; } = "";
        public bool NeedsReboot { get; set; }
        public int Done => Lines.Count(l => l.Status == "Applied" || l.Status == "AlreadyDone");
        public int Failed => Lines.Count(l => l.Status == "Failed");
        public int Total => Lines.Count(l => l.Status != "Skipped");
        public int Percent { get; set; }
    }

    /// <summary>Roda o Otimizar (ou o Desfazer) passo a passo, avisando o progresso e parando se um jogo abrir.</summary>
    public static class OptimizeRunner
    {
        public static OptimizeSummary Run(bool undo, bool force, IProgress<OptimizeProgress> progress, Func<bool> gameOpen, CancellationToken cancel, ISet<string>? skip = null)
        {
            var sum = new OptimizeSummary();
            // Ao otimizar, só entram os ajustes marcados; ao desfazer, entram todos.
            List<BoostAction> actions = BoostCatalog.All.Where(a => undo || skip == null || !skip.Contains(a.Id)).ToList();
            int totalWeight = actions.Sum(a => a.Weight);
            int doneWeight = 0;
            bool reboot = false;

            for (int i = 0; i < actions.Count; i++)
            {
                BoostAction a = actions[i];

                if (cancel.IsCancellationRequested || gameOpen())
                {
                    sum.Interrupted = true;
                    sum.StoppedAt = Loc.T(a.Title);
                    for (int j = i; j < actions.Count; j++)
                    {
                        sum.Lines.Add(new OptimizeLine { Id = actions[j].Id, Title = actions[j].Title, Status = "NotRun" });
                    }
                    break;
                }

                progress.Report(new OptimizeProgress
                {
                    Index = i + 1,
                    Count = actions.Count,
                    Percent = (int)Math.Round(doneWeight * 100.0 / totalWeight),
                    Step = Loc.T(a.Title),
                });

                TweakResult r;
                try
                {
                    r = undo ? a.Undo() : a.Apply(force);
                }
                catch (Exception ex)
                {
                    r = new TweakResult { Ok = false, Message = ex.Message };
                }

                var line = new OptimizeLine { Id = a.Id, Title = a.Title, Message = r.Message };
                if (!r.Ok) line.Status = "Failed";
                else if (r.Skipped) line.Status = "Skipped";
                else if (r.NoChange) line.Status = "AlreadyDone";
                else line.Status = "Applied";
                if (r.Ok && !r.NoChange && r.NeedsReboot) reboot = true;
                sum.Lines.Add(line);

                doneWeight += a.Weight;
            }

            sum.NeedsReboot = reboot;
            sum.Percent = sum.Interrupted ? (int)Math.Round(doneWeight * 100.0 / totalWeight) : 100;
            progress.Report(new OptimizeProgress { Index = actions.Count, Count = actions.Count, Percent = sum.Percent, Step = Loc.T("Concluído") });
            TimerService.Sync();
            return sum;
        }
    }
}
