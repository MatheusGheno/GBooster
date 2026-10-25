using System;
using System.Collections.Generic;
using System.Linq;

namespace GBooster.Core
{
    /// <summary>
    /// O "cérebro": a cada 2 segundos olha a lista de processos. Quando um jogo cadastrado
    /// abre, espera alguns segundos e troca resolução/saturação; quando fecha, devolve tudo.
    /// Tudo roda na thread da interface (as chamadas são rápidas), então não há concorrência.
    /// </summary>
    public sealed class Engine
    {
        public const int RestoreAfterCloseTicks = 1; // 1 verificação sem o jogo (~2 s) antes de restaurar

        public AppSettings Settings { get; }
        public VibranceService Vibrance { get; } = new VibranceService();
        public AppliedState? Current { get; private set; }
        public List<LogEntry> Log { get; }

        /// <summary>Algo mudou (estado aplicado, pausa, jogo detectado...). A interface se atualiza.</summary>
        public event Action? Changed;
        /// <summary>Aviso para mostrar na bandeja: título, texto.</summary>
        public event Action<string, string>? Notify;

        /// <summary>Corta textos longos (nomes de programas) e termina com "…".</summary>
        private static string Short(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";

        private GameConfig? _pending;
        private DateTime _pendingSince;
        private int _missing;

        public Engine(AppSettings settings)
        {
            Settings = settings;
            Log = Store.LoadLog();
            Vibrance.Init();
        }

        // ------------------------------------------------------------------
        // Estado para a interface
        // ------------------------------------------------------------------
        public bool IsActive => Current != null;
        public bool IsWaitingForDelay => Current == null && _pending != null;

        public GameConfig? Game(string id) => Settings.Games.FirstOrDefault(g => g.Id == id);

        // ------------------------------------------------------------------
        // Ciclo principal (chamado a cada 2 s)
        // ------------------------------------------------------------------
        private readonly HashSet<string> _wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Nomes de programa que o app precisa ver: os jogos cadastrados e o que está aplicado agora.</summary>
        private HashSet<string> WantedNames()
        {
            _wanted.Clear();
            foreach (GameConfig g in Settings.Games)
            {
                string n = g.ProcessName?.Trim() ?? "";
                if (n.Length > 0) _wanted.Add(n);
            }
            if (!string.IsNullOrEmpty(Current?.ProcessName)) _wanted.Add(Current!.ProcessName);
            return _wanted;
        }

        public void Tick()
        {
            Dictionary<string, uint> procs = ProcessScanner.Snapshot(WantedNames());

            if (Current != null)
            {
                WatchActive(procs);
                return;
            }

            if (Settings.PauseDetection)
            {
                SetPending(null);
                return;
            }

            foreach (GameConfig game in Settings.Games)
            {
                if (!game.Enabled || string.IsNullOrWhiteSpace(game.ProcessName))
                {
                    continue;
                }
                if (procs.TryGetValue(game.ProcessName.Trim(), out uint pid))
                {
                    if (_pending == null || _pending.Id != game.Id)
                    {
                        SetPending(game);
                        AddLog(Loc.F("{0} detectado", game.ProcessName), Loc.F("jogo “{0}” · aguardando {1} s", game.Name, Settings.WaitAfterOpenSeconds), false);
                    }
                    else if ((DateTime.UtcNow - _pendingSince).TotalSeconds >= Settings.WaitAfterOpenSeconds)
                    {
                        SetPending(null);
                        Apply(game, pid, false);
                    }
                    return;
                }
            }

            SetPending(null);
        }

        private void WatchActive(Dictionary<string, uint> procs)
        {
            AppliedState st = Current!;
            bool running = !string.IsNullOrEmpty(st.ProcessName) && procs.ContainsKey(st.ProcessName);

            if (st.Manual)
            {
                // Teste manual: se o jogo abrir de verdade, passa a acompanhar o jogo.
                if (running)
                {
                    st.Manual = false;
                    Store.SaveApplied(st);
                    Changed?.Invoke();
                }
                return;
            }

            if (running)
            {
                _missing = 0;
                return;
            }

            _missing++;
            if (_missing >= RestoreAfterCloseTicks)
            {
                AddLog(Loc.F("{0} encerrado", st.ProcessName), "", false);
                RestoreNow(true);
            }
        }

        private void SetPending(GameConfig? game)
        {
            bool changed = (_pending?.Id) != (game?.Id);
            _pending = game;
            if (game != null && changed)
            {
                _pendingSince = DateTime.UtcNow;
            }
            if (changed)
            {
                Changed?.Invoke();
            }
        }

        // ------------------------------------------------------------------
        // Aplicar
        // ------------------------------------------------------------------
        /// <summary>Aplica a configuração do jogo. pid = 0 quando é um teste sem o jogo aberto.</summary>
        public string? Apply(GameConfig game, uint pid, bool manual)
        {
            if (Current != null)
            {
                RestoreNow(false);
            }

            string device = pid != 0 ? DisplayService.GetMonitorOfProcess(pid) : DisplayService.GetPrimaryDevice();
            MonitorSnapshot? before = DisplayService.GetCurrent(device);
            if (before == null)
            {
                string msg = Loc.F("Não consegui ler o monitor {0}.", device);
                AddLog(Loc.T("Erro ao aplicar"), msg, false);
                return msg;
            }

            var state = new AppliedState
            {
                GameId = game.Id,
                GameName = game.Name,
                ProcessName = game.ProcessName,
                Manual = manual,
                AppliedWidth = game.Width,
                AppliedHeight = game.Height,
                AppliedHz = game.Hz > 0 ? game.Hz : before.Hz,
                AppliedSaturation = game.SaturationEnabled ? game.Saturation : -1
            };
            state.Monitors.Add(before);

            bool changeSaturation = game.SaturationEnabled && Vibrance.Available;
            if (changeSaturation)
            {
                VibranceSnapshot? vs = Vibrance.Read(device);
                if (vs != null)
                {
                    state.Vibrance.Add(vs);
                }
            }

            // Grava ANTES de mexer: se o app cair, o próximo início desfaz.
            Store.SaveApplied(state);
            Current = state;
            _missing = 0;

            string? error = null;
            bool sameResolution = before.Width == game.Width && before.Height == game.Height && (game.Hz <= 0 || game.Hz == before.Hz);
            if (!sameResolution)
            {
                error = DisplayService.Apply(device, game.Width, game.Height, game.Hz);
                if (error != null && game.Hz > 0)
                {
                    // O Windows recusou esse Hz: só volta para "manter atual" e tenta de novo, sem desfazer o resto.
                    AddLog(Loc.T("Hz não aceito"), Loc.F("{0} Hz não funciona em {1} × {2}. Voltei para manter o atual.", game.Hz, game.Width, game.Height), false);
                    game.Hz = 0;
                    try { Store.SaveSettings(Settings); } catch { }
                    state.AppliedHz = before.Hz;
                    error = DisplayService.Apply(device, game.Width, game.Height, 0);
                }
            }

            if (error != null)
            {
                AddLog(Loc.T("Não consegui aplicar a resolução"), error, false);
                RestoreNow(false);
                Changed?.Invoke();
                return error;
            }

            if (!sameResolution)
            {
                AddLog(Loc.T("Resolução aplicada"), before.Width + " × " + before.Height + " → " + game.Width + " × " + game.Height + (game.Hz > 0 && game.Hz != before.Hz ? " · " + game.Hz + " Hz" : "") + " · " + device, true);
            }

            if (changeSaturation && state.Vibrance.Count > 0)
            {
                string? vErr = Vibrance.SetPercent(device, game.Saturation);
                if (vErr != null)
                {
                    AddLog(Loc.T("Saturação não aplicada"), vErr, false);
                }
                else
                {
                    AddLog(Loc.T("Saturação aplicada"), state.Vibrance[0].Percent + "% → " + game.Saturation + "%", true);
                }
            }
            else if (game.SaturationEnabled && !Vibrance.Available)
            {
                AddLog(Loc.T("Saturação ignorada"), Vibrance.Problem ?? Loc.T("indisponível"), false);
            }

            if (Settings.NotifyOnChange)
            {
                MonitorSnapshot? o = state.Monitors.FirstOrDefault();
                string back = o != null ? Loc.F("A resolução volta para {0} × {1} quando o jogo fechar.", o.Width, o.Height) : Loc.T("A resolução volta ao normal quando o jogo fechar.");
                Notify?.Invoke(Short(Loc.F("{0} × {1} para {2}", game.Width, game.Height, game.Name), 40), back);
            }
            Changed?.Invoke();
            return null;
        }

        /// <summary>Refaz a aplicação com os valores atuais da configuração do jogo (botão "Reaplicar").</summary>
        public string? Reapply(GameConfig game)
        {
            uint pid = 0;
            if (ProcessScanner.Snapshot(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { game.ProcessName.Trim() }).TryGetValue(game.ProcessName.Trim(), out uint found))
            {
                pid = found;
            }
            bool manual = pid == 0;
            return Apply(game, pid, manual);
        }

        // ------------------------------------------------------------------
        // Restaurar
        // ------------------------------------------------------------------
        public void RestoreNow(bool announce)
        {
            AppliedState? st = Current;
            if (st == null)
            {
                return;
            }

            foreach (MonitorSnapshot m in st.Monitors)
            {
                MonitorSnapshot? now = DisplayService.GetCurrent(m.Device);
                if (now != null && (now.Width != m.Width || now.Height != m.Height || now.Hz != m.Hz))
                {
                    string? err = DisplayService.Restore(m);
                    if (err != null)
                    {
                        AddLog(Loc.T("Erro ao restaurar a resolução"), err, false);
                    }
                    else
                    {
                        AddLog(Loc.T("Resolução restaurada"), now.Width + " × " + now.Height + " → " + m.Width + " × " + m.Height + " · " + m.Device, false);
                    }
                }
            }

            foreach (VibranceSnapshot v in st.Vibrance)
            {
                string? err = Vibrance.SetLevel(v.Device, v.Level);
                if (err != null)
                {
                    AddLog(Loc.T("Erro ao restaurar a saturação"), err, false);
                }
                else
                {
                    AddLog(Loc.T("Saturação restaurada"), Loc.F("volta para {0}% (o valor de antes do jogo)", v.Percent), false);
                }
            }

            Current = null;
            _missing = 0;
            Store.ClearApplied();

            if (announce && Settings.NotifyOnChange)
            {
                MonitorSnapshot? o = st.Monitors.FirstOrDefault();
                Notify?.Invoke(Short(Loc.F("{0} foi encerrado", st.GameName), 40),
                    o != null ? Loc.F("Resolução voltou para {0} × {1}.", o.Width, o.Height) : Loc.T("Resolução original restaurada."));
            }
            Changed?.Invoke();
        }

        /// <summary>No início do app: se na última vez ele caiu com algo aplicado, desfaz agora.</summary>
        public void RecoverFromCrash()
        {
            AppliedState? left = Store.LoadApplied();
            if (left == null)
            {
                return;
            }
            Current = left;
            AddLog(Loc.T("Recuperação"), Loc.T("o app tinha fechado com alterações ativas; desfazendo"), false);
            RestoreNow(false);
        }

        public void Shutdown()
        {
            RestoreNow(false);
            Vibrance.Shutdown();
        }

        // ------------------------------------------------------------------
        // Log
        // ------------------------------------------------------------------
        public void AddLog(string text, string detail, bool active)
        {
            Log.Insert(0, new LogEntry { Time = DateTime.Now, Text = text, Detail = detail, Active = active });
            if (Log.Count > 500)
            {
                Log.RemoveRange(500, Log.Count - 500);
            }
            Store.SaveLog(Log);
        }

        public void ClearLog()
        {
            Log.Clear();
            Store.SaveLog(Log);
            Changed?.Invoke();
        }
    }
}
