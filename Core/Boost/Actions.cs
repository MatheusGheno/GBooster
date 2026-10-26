using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GBooster.Core.Boost
{
    // =====================================================================
    // Pequenos auxiliares
    // =====================================================================
    internal static class RegRead
    {
        public static int? Dword(bool machine, string path, string name)
        {
            try
            {
                using (RegistryKey? k = (machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(path, false))
                {
                    object? v = k?.GetValue(name, null);
                    return v is int i ? i : (int?)null;
                }
            }
            catch
            {
                return null;
            }
        }

        public static bool KeyExists(bool machine, string path)
        {
            try
            {
                using (RegistryKey? k = (machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(path, false))
                {
                    return k != null;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    // =====================================================================
    // 1. Gravação de tela desligada (só registro)
    // =====================================================================
    internal sealed class RecordingOffAction : BoostAction
    {
        public RecordingOffAction() : base("gamedvr-off",
            "Gravação de tela desligada",
            "Desliga a Game Bar de gravar, a gravação em segundo plano e os atalhos de captura. O Game Mode continua ligado.")
        {
            var t = new Tweak { Id = Id, Title = Title, Description = Description, NeedsAdmin = true };
            t.Items.Add(new RegItem(false, @"System\GameConfigStore", "GameDVR_Enabled", 0));
            t.Items.Add(new RegItem(false, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0));
            t.Items.Add(new RegItem(false, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 0));
            t.Items.Add(new RegItem(false, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AudioCaptureEnabled", 0));
            t.Items.Add(new RegItem(false, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "CursorCaptureEnabled", 0));
            t.Items.Add(new RegItem(false, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0));
            t.Items.Add(new RegItem(false, @"Software\Microsoft\GameBar", "ShowStartupPanel", 0));
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0));
            Reg = t;
        }
    }

    // =====================================================================
    // 2. Plano de energia GBooster
    // =====================================================================
    internal sealed class PowerPlanAction : BoostAction
    {
        private const string PlanName = "GBooster";
        private const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        private const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private static readonly Regex GuidName = new Regex(@"([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\s*\((.*)\)");
        private static readonly Regex GuidOnly = new Regex(@"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");

        // (grupo, configuração, valor) — todos na tomada (AC). Em bateria o plano segue o que o Windows já fazia.
        private static readonly string[][] Values =
        {
            new[] { "238c9fa8-0aad-41ed-83f4-97be242c8f20", "29f6c1db-86da-48c5-9fdb-f2b67b1f44da", "0" },     // suspender: nunca
            new[] { "238c9fa8-0aad-41ed-83f4-97be242c8f20", "9d7815a6-7ee4-497e-8888-515a05f02364", "0" },     // hibernar: nunca
            new[] { "7516b95f-f776-4464-8c53-06167f40cc99", "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e", "0" },     // desligar a tela: nunca
            new[] { "2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", "0" },     // suspensão seletiva do USB: desligada
            new[] { "501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", "0" },     // economia do PCIe: desligada
            new[] { "54533251-82be-4824-96c1-47b60b740d00", "893dee8e-2bef-41e0-89c6-b55d0929964c", "100" },   // processador mínimo
            new[] { "54533251-82be-4824-96c1-47b60b740d00", "bc5038f7-23e0-4960-96da-33abaf5935ec", "100" },   // processador máximo
            new[] { "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", "0" },     // Wi-Fi: desempenho máximo
        };

        public PowerPlanAction() : base("power-plan",
            "Plano de energia GBooster",
            "Cria uma cópia do plano Desempenho Máximo e ativa. Tela, suspensão e hibernação nunca; suspensão seletiva de USB desligada; economia de energia do PCIe desligada; processador fixo em 100%; Wi-Fi em desempenho máximo.")
        {
        }

        protected override bool HasExtra => true;

        private static string Powercfg => Shell.SysExe("powercfg.exe");

        private static bool Active(out string? guid, out string name)
        {
            guid = null; name = "";
            ShellResult r = Shell.Run(Powercfg, 15000, "/getactivescheme");
            Match m = GuidName.Match(r.Output);
            if (!m.Success) return false;
            guid = m.Groups[1].Value;
            name = m.Groups[2].Value.Trim();
            return true;
        }

        private static bool PlanExists(string guid)
        {
            ShellResult r = Shell.Run(Powercfg, 15000, "/list");
            return r.Output.IndexOf(guid, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string? Duplicate(string baseGuid)
        {
            ShellResult r = Shell.Run(Powercfg, 20000, "/duplicatescheme", baseGuid);
            if (!r.Ok) return null;
            Match m = GuidOnly.Match(r.Output);
            return m.Success ? m.Value : null;
        }

        protected override bool? ExtraApplied()
        {
            if (!Active(out _, out string name)) return false;
            return string.Equals(name, PlanName, StringComparison.OrdinalIgnoreCase);
        }

        protected override string? ExtraApply(Dictionary<string, string> bk)
        {
            bool haveActive = Active(out string? activeGuid, out string activeName);
            bool activeIsOurs = haveActive && string.Equals(activeName, PlanName, StringComparison.OrdinalIgnoreCase);

            if (!bk.ContainsKey("prevGuid") && haveActive && !activeIsOurs && activeGuid != null) bk["prevGuid"] = activeGuid;

            string? plan = bk.TryGetValue("planGuid", out string? saved) && PlanExists(saved) ? saved : null;
            if (plan == null && activeIsOurs) plan = activeGuid;
            if (plan == null)
            {
                plan = Duplicate(Ultimate) ?? Duplicate(HighPerf) ?? Duplicate("SCHEME_CURRENT");
                if (plan == null) return Loc.T("Não consegui criar o plano de energia.");
                Shell.Run(Powercfg, 15000, "/changename", plan, PlanName, "Criado pelo GBooster");
                bk["planGuid"] = plan;
            }

            foreach (string[] v in Values)
            {
                // Se o PC não tem a configuração (ex.: sem Wi-Fi), o powercfg recusa e seguimos.
                Shell.Run(Powercfg, 15000, "/setacvalueindex", plan, v[0], v[1], v[2]);
            }
            ShellResult act = Shell.Run(Powercfg, 15000, "/setactive", plan);
            if (!act.Ok) return Loc.F("Não consegui ativar o plano de energia: {0}", act.Brief);
            return null;
        }

        protected override string? ExtraUndo(Dictionary<string, string> bk)
        {
            bk.TryGetValue("planGuid", out string? plan);
            bk.TryGetValue("prevGuid", out string? prev);

            string target = prev != null && PlanExists(prev) ? prev : Balanced;
            bool haveActive = Active(out string? activeGuid, out _);
            // Só troca se o plano ativo ainda é o nosso; se o usuário já escolheu outro, respeita.
            if (plan != null && haveActive && string.Equals(activeGuid, plan, StringComparison.OrdinalIgnoreCase))
            {
                ShellResult act = Shell.Run(Powercfg, 15000, "/setactive", target);
                if (!act.Ok) return Loc.F("Não consegui voltar o plano de energia: {0}", act.Brief);
            }
            if (plan != null && PlanExists(plan)) Shell.Run(Powercfg, 15000, "/delete", plan);
            return null;
        }
    }

    // =====================================================================
    // 3. Sem hibernação e sem inicialização rápida
    // =====================================================================
    internal sealed class HibernateAction : BoostAction
    {
        private static readonly string[] Logs =
        {
            "Microsoft-Windows-SleepStudy/Diagnostic",
            "Microsoft-Windows-Kernel-Processor-Power/Diagnostic",
            "Microsoft-Windows-UserModePowerService/Diagnostic",
        };

        public HibernateAction() : base("hibernate-off",
            "Sem hibernação e sem inicialização rápida",
            "Desliga a hibernação (powercfg -h off), a inicialização rápida e o Sleep Study. Libera o arquivo hiberfil.sys e o PC desliga por completo.")
        {
            var t = new Tweak { Id = Id, Title = Title, Description = Description, NeedsAdmin = true };
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0));
            Reg = t;
        }

        protected override bool HasExtra => true;

        protected override bool? ExtraApplied()
        {
            int? h = RegRead.Dword(true, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
            return h == 0;
        }

        protected override string? ExtraApply(Dictionary<string, string> bk)
        {
            if (!bk.ContainsKey("hibernate"))
            {
                int? h = RegRead.Dword(true, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
                bk["hibernate"] = h == 0 ? "0" : "1";
            }
            ShellResult r = Shell.Sys("powercfg.exe", 30000, "-h", "off");
            if (!r.Ok) return Loc.F("Não consegui desligar a hibernação: {0}", r.Brief);

            foreach (string log in Logs)
            {
                string key = "log:" + log;
                if (!bk.ContainsKey(key))
                {
                    ShellResult q = Shell.Sys("wevtutil.exe", 15000, "gl", log);
                    if (!q.Ok) continue;   // o registro de eventos não existe neste Windows
                    bk[key] = Regex.IsMatch(q.Output, @"enabled:\s*true", RegexOptions.IgnoreCase) ? "1" : "0";
                }
                if (bk[key] == "1") Shell.Sys("wevtutil.exe", 15000, "sl", log, "/e:false");
            }
            return null;
        }

        protected override string? ExtraUndo(Dictionary<string, string> bk)
        {
            if (bk.TryGetValue("hibernate", out string? h) && h == "1")
            {
                ShellResult r = Shell.Sys("powercfg.exe", 30000, "-h", "on");
                if (!r.Ok) return Loc.F("Não consegui ligar a hibernação: {0}", r.Brief);
            }
            foreach (string log in Logs)
            {
                if (bk.TryGetValue("log:" + log, out string? on) && on == "1") Shell.Sys("wevtutil.exe", 15000, "sl", log, "/e:true");
            }
            return null;
        }
    }

    // =====================================================================
    // 4. Timing do Windows mais preciso
    // =====================================================================
    internal sealed class TimerAction : BoostAction
    {
        // (nome no bcdedit, valor que queremos; null = remover)
        private static readonly string[][] Bcd =
        {
            new[] { "disabledynamictick", "yes" },
            new[] { "useplatformtick", "yes" },
            new[] { "useplatformclock", "" },
        };

        public TimerAction() : base("timer-resolution",
            "Timing do Windows mais preciso",
            "Troca o timing para 0,5 ms (em vez de 15,6 ms) e ajusta o boot com três comandos do bcdedit: disabledynamictick, useplatformtick e remove useplatformclock. Também ativa GlobalTimerResolutionRequests. Pede reinício.")
        {
            NeedsReboot = true;
            var t = new Tweak { Id = Id, Title = Title, Description = Description, NeedsAdmin = true };
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1));
            Reg = t;
        }

        protected override bool HasExtra => true;

        /// <summary>Lê as opções do boot atual: nome → valor (minúsculo).</summary>
        private static Dictionary<string, string>? ReadBcd()
        {
            ShellResult r = Shell.Sys("bcdedit.exe", 20000, "/enum", "{current}");
            if (!r.Ok) return null;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in r.Output.Split('\n'))
            {
                string line = raw.Trim();
                int sp = line.IndexOfAny(new[] { ' ', '\t' });
                if (sp <= 0) continue;
                d[line.Substring(0, sp)] = line.Substring(sp).Trim().ToLowerInvariant();
            }
            return d;
        }

        protected override bool? ExtraApplied()
        {
            Dictionary<string, string>? b = ReadBcd();
            if (b == null) return ActionStore.Has(Id);   // sem administrador não dá para ler o boot: confia no que o app fez
            return b.TryGetValue("disabledynamictick", out string? a) && a == "yes"
                   && b.TryGetValue("useplatformtick", out string? p) && p == "yes"
                   && !b.ContainsKey("useplatformclock");
        }

        protected override string? ExtraApply(Dictionary<string, string> bk)
        {
            Dictionary<string, string>? now = ReadBcd();
            if (now == null) return Loc.T("Não consegui ler as opções de boot do Windows.");

            var errors = new List<string>();
            foreach (string[] item in Bcd)
            {
                string name = item[0];
                string key = "bcd:" + name;
                if (!bk.ContainsKey(key)) bk[key] = now.TryGetValue(name, out string? cur) ? cur : "";

                if (item[1].Length == 0)
                {
                    if (now.ContainsKey(name))
                    {
                        ShellResult r = Shell.Sys("bcdedit.exe", 20000, "/deletevalue", name);
                        if (!r.Ok) errors.Add(name + ": " + r.Brief);
                    }
                }
                else
                {
                    ShellResult r = Shell.Sys("bcdedit.exe", 20000, "/set", name, item[1]);
                    if (!r.Ok) errors.Add(name + ": " + r.Brief);
                }
            }
            if (errors.Count > 0) return string.Join(" | ", errors);

            TimerService.Start();
            return null;
        }

        protected override string? ExtraUndo(Dictionary<string, string> bk)
        {
            var errors = new List<string>();
            foreach (string[] item in Bcd)
            {
                string name = item[0];
                if (!bk.TryGetValue("bcd:" + name, out string? prior)) continue;
                ShellResult r = prior.Length == 0
                    ? Shell.Sys("bcdedit.exe", 20000, "/deletevalue", name)
                    : Shell.Sys("bcdedit.exe", 20000, "/set", name, prior);
                // Remover algo que já não existe não é erro.
                if (!r.Ok && prior.Length != 0) errors.Add(name + ": " + r.Brief);
            }
            TimerService.Stop();
            return errors.Count == 0 ? null : string.Join(" | ", errors);
        }
    }

    // =====================================================================
    // 5. Prioridade para jogos (só registro)
    // =====================================================================
    internal sealed class GamePriorityAction : BoostAction
    {
        public GamePriorityAction() : base("game-priority",
            "Prioridade para jogos",
            "Ajusta o agendador multimídia (MMCSS) para dar prioridade de CPU e GPU a jogos, deixa o Windows mais responsivo ao primeiro plano (SystemResponsiveness 10, Win32PrioritySeparation 38) e tira o limite de rede NetworkThrottlingIndex.")
        {
            var t = new Tweak { Id = Id, Title = Title, Description = Description, NeedsAdmin = true };
            string prof = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            t.Items.Add(new RegItem(true, prof, "SystemResponsiveness", 10));
            t.Items.Add(new RegItem(true, prof, "NetworkThrottlingIndex", -1));   // 0xFFFFFFFF = sem limite
            t.Items.Add(new RegItem(true, prof + @"\Tasks\Games", "GPU Priority", 8));
            t.Items.Add(new RegItem(true, prof + @"\Tasks\Games", "Priority", 6));
            t.Items.Add(new RegItem(true, prof + @"\Tasks\Games", "Scheduling Category", "High"));
            t.Items.Add(new RegItem(true, prof + @"\Tasks\Games", "SFIO Priority", "High"));
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38));
            Reg = t;
        }
    }

    // =====================================================================
    // 6. Disco NTFS mais leve
    // =====================================================================
    internal sealed class NtfsAction : BoostAction
    {
        public NtfsAction() : base("ntfs-light",
            "Disco NTFS mais leve",
            "Para de gravar a data do último acesso em cada arquivo (disablelastaccess), reserva mais espaço para a tabela de arquivos (mftzone) e mantém o TRIM ligado. Não muda nenhum dado seu.")
        {
        }

        protected override bool HasExtra => true;

        private static int? Query(string name)
        {
            ShellResult r = Shell.Sys("fsutil.exe", 20000, "behavior", "query", name);
            if (!r.Ok) return null;
            Match m = Regex.Match(r.Output, @"=\s*(\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : (int?)null;
        }

        protected override bool? ExtraApplied()
        {
            int? last = Query("disablelastaccess");
            int? mft = Query("mftzone");
            if (last == null || mft == null) return ActionStore.Has(Id);
            return (last == 1 || last == 3) && mft == 2;
        }

        protected override string? ExtraApply(Dictionary<string, string> bk)
        {
            if (!bk.ContainsKey("lastaccess"))
            {
                int? v = Query("disablelastaccess");
                if (v.HasValue) bk["lastaccess"] = v.Value.ToString(CultureInfo.InvariantCulture);
            }
            if (!bk.ContainsKey("mftzone"))
            {
                int? v = Query("mftzone");
                if (v.HasValue) bk["mftzone"] = v.Value.ToString(CultureInfo.InvariantCulture);
            }
            if (!bk.ContainsKey("trim"))
            {
                int? v = Query("disabledeletenotify");
                if (v.HasValue) bk["trim"] = v.Value.ToString(CultureInfo.InvariantCulture);
            }

            var errors = new List<string>();
            ShellResult a = Shell.Sys("fsutil.exe", 20000, "behavior", "set", "disablelastaccess", "1");
            if (!a.Ok) errors.Add(a.Brief);
            ShellResult b = Shell.Sys("fsutil.exe", 20000, "behavior", "set", "mftzone", "2");
            if (!b.Ok) errors.Add(b.Brief);
            if (bk.TryGetValue("trim", out string? trim) && trim != "0")
            {
                ShellResult c = Shell.Sys("fsutil.exe", 20000, "behavior", "set", "disabledeletenotify", "0");
                if (!c.Ok) errors.Add(c.Brief);
            }
            return errors.Count == 0 ? null : string.Join(" | ", errors);
        }

        protected override string? ExtraUndo(Dictionary<string, string> bk)
        {
            var errors = new List<string>();
            if (bk.TryGetValue("lastaccess", out string? la))
            {
                ShellResult r = Shell.Sys("fsutil.exe", 20000, "behavior", "set", "disablelastaccess", la);
                if (!r.Ok) errors.Add(r.Brief);
            }
            if (bk.TryGetValue("mftzone", out string? mz))
            {
                ShellResult r = Shell.Sys("fsutil.exe", 20000, "behavior", "set", "mftzone", mz);
                if (!r.Ok) errors.Add(r.Brief);
            }
            if (bk.TryGetValue("trim", out string? tr) && tr != "0")
            {
                ShellResult r = Shell.Sys("fsutil.exe", 20000, "behavior", "set", "disabledeletenotify", tr);
                if (!r.Ok) errors.Add(r.Brief);
            }
            return errors.Count == 0 ? null : string.Join(" | ", errors);
        }
    }

    // =====================================================================
    // 7. Memória sem atrasos
    // =====================================================================
    internal sealed class MemoryAction : BoostAction
    {
        public MemoryAction() : base("memory-light",
            "Memória sem atrasos",
            "Desliga a compressão de memória, o Prefetch/Superfetch e o paginamento do executivo, e ajusta o limite de divisão do svchost. Em PC com 16 GB ou mais, a memória sobra e isso diminui travadas.")
        {
            NeedsReboot = true;
            Weight = 2;
            var t = new Tweak { Id = Id, Title = Title, Description = Description, NeedsAdmin = true };
            string mm = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
            t.Items.Add(new RegItem(true, mm + @"\PrefetchParameters", "EnablePrefetcher", 0));
            t.Items.Add(new RegItem(true, mm + @"\PrefetchParameters", "EnableSuperfetch", 0));
            t.Items.Add(new RegItem(true, mm, "DisablePagingExecutive", 1));
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", 4) { OnlyIfExists = true });
            long kb = SystemInfoService.TotalRamBytes() / 1024;
            if (kb > 0 && kb < int.MaxValue) t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", (int)kb));
            Reg = t;
        }

        // Com pouca memória, essas mudanças pioram em vez de ajudar.
        public override bool IsApplicable() => SystemInfoService.TotalRamBytes() >= 15L * 1024 * 1024 * 1024;

        protected override bool HasExtra => true;

        protected override bool? ExtraApplied()
        {
            ActionStore.Get(Id).TryGetValue("mc", out string? v);
            return v != null;
        }

        protected override string? ExtraApply(Dictionary<string, string> bk)
        {
            if (bk.ContainsKey("mc")) return null;
            string output = Shell.PowerShell(
                "try { $on = (Get-MMAgent).MemoryCompression; if ($on) { Disable-MMAgent -MemoryCompression -ErrorAction Stop; 'ON' } else { 'OFF' } } catch { 'ERR|' + $_.Exception.Message }",
                60000, out string? err);
            if (err != null) return err;
            string line = output.Trim();
            if (line.StartsWith("ERR|", StringComparison.Ordinal)) return line.Substring(4);
            bk["mc"] = line == "ON" ? "on" : "off";
            return null;
        }

        protected override string? ExtraUndo(Dictionary<string, string> bk)
        {
            if (bk.TryGetValue("mc", out string? was) && was == "on")
            {
                Shell.PowerShell("try { Enable-MMAgent -MemoryCompression -ErrorAction Stop } catch { }", 60000, out string? err);
                if (err != null) return err;
            }
            return null;
        }
    }

    // =====================================================================
    // 8. Windows mais ágil ao fechar (só registro)
    // =====================================================================
    internal sealed class SnappyAction : BoostAction
    {
        public SnappyAction() : base("snappy-close",
            "Windows mais ágil ao fechar",
            "Fecha apps travados mais rápido (HungAppTimeout, WaitToKillAppTimeout, AutoEndTasks) e tira o atraso dos menus (MenuShowDelay 0).")
        {
            var t = new Tweak { Id = Id, Title = Title, Description = Description, NeedsAdmin = true };
            t.Items.Add(new RegItem(false, @"Control Panel\Desktop", "HungAppTimeout", "2000"));
            t.Items.Add(new RegItem(false, @"Control Panel\Desktop", "WaitToKillAppTimeout", "2000"));
            t.Items.Add(new RegItem(false, @"Control Panel\Desktop", "AutoEndTasks", "1"));
            t.Items.Add(new RegItem(false, @"Control Panel\Desktop", "MenuShowDelay", "0"));
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000"));
            Reg = t;
        }
    }

    // =====================================================================
    // 9. Telemetria, Cortana e sincronização desligadas
    // =====================================================================
    internal sealed class TelemetryAction : BoostAction
    {
        private static readonly string[] Tasks =
        {
            @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
            @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
            @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
            @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
            @"\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
            @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
            @"\Microsoft\Windows\Feedback\Siuf\DmClient",
            @"\Microsoft\Windows\Windows Error Reporting\QueueReporting",
        };

        public TelemetryAction() : base("telemetry-off",
            "Telemetria, Cortana e sincronização desligadas",
            "Desliga serviços e tarefas de telemetria, a Cortana, o relatório de erros, o programa de experiência do cliente, a sincronização de configurações e as notificações de dicas. Não mexe no Windows Defender.")
        {
            Weight = 2;
            var t = new Tweak { Id = Id, Title = Title, Description = Description, NeedsAdmin = true };
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0));
            t.Items.Add(new RegItem(true, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0));
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4) { OnlyIfExists = true });
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Services\dmwappushservice", "Start", 4) { OnlyIfExists = true });
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0));
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1));
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0));
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSync", 2));
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSyncUserOverride", 1));
            string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
            t.Items.Add(new RegItem(false, cdm, "SubscribedContent-338389Enabled", 0));
            t.Items.Add(new RegItem(false, cdm, "SubscribedContent-310093Enabled", 0));
            t.Items.Add(new RegItem(false, cdm, "SoftLandingEnabled", 0));
            t.Items.Add(new RegItem(false, cdm, "SystemPaneSuggestionsEnabled", 0));
            t.Items.Add(new RegItem(false, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications", 0));
            Reg = t;
        }

        protected override bool HasExtra => true;

        protected override bool? ExtraApplied()
        {
            return ActionStore.Get(Id).ContainsKey("tasksDone");
        }

        private static string Schtasks => Shell.SysExe("schtasks.exe");

        protected override string? ExtraApply(Dictionary<string, string> bk)
        {
            if (bk.ContainsKey("tasksDone")) return null;
            var disabledByUs = new List<string>();
            foreach (string task in Tasks)
            {
                ShellResult q = Shell.Run(Schtasks, 20000, "/Query", "/TN", task, "/XML");
                if (!q.Ok) continue;   // a tarefa não existe neste Windows
                if (q.Output.IndexOf("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase) >= 0) continue;   // já estava desligada
                ShellResult c = Shell.Run(Schtasks, 20000, "/Change", "/TN", task, "/DISABLE");
                if (c.Ok) disabledByUs.Add(task);
            }
            bk["tasks"] = string.Join("\n", disabledByUs);
            bk["tasksDone"] = "1";
            return null;
        }

        protected override string? ExtraUndo(Dictionary<string, string> bk)
        {
            if (bk.TryGetValue("tasks", out string? list))
            {
                foreach (string task in list.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    // Só religa tarefas que fazem parte da nossa lista.
                    if (Array.IndexOf(Tasks, task) < 0) continue;
                    Shell.Run(Schtasks, 20000, "/Change", "/TN", task, "/ENABLE");
                }
            }
            return null;
        }
    }

    // =====================================================================
    // 10. Apps inúteis do Windows removidos (não volta pelo Desfazer)
    // =====================================================================
    internal sealed class AppsAction : BoostAction
    {
        // Nunca entram aqui: Xbox, Game Bar, Loja, Teams, Terminal, Fotos, Calculadora.
        private static readonly string[] Packages =
        {
            "Microsoft.BingNews",
            "Microsoft.BingFinance",
            "Microsoft.BingSports",
            "Microsoft.Getstarted",
            "Microsoft.GetHelp",
            "Microsoft.WindowsMaps",
            "Microsoft.ZuneVideo",
            "Microsoft.MicrosoftSolitaireCollection",
            "Microsoft.WindowsFeedbackHub",
            "Microsoft.MicrosoftOfficeHub",
            "Microsoft.People",
            "Microsoft.SkypeApp",
            "Microsoft.Microsoft3DViewer",
            "Microsoft.MixedReality.Portal",
        };

        public AppsAction() : base("apps-remove",
            "Apps inúteis do Windows removidos",
            "Remove apps que ninguém usa em jogo (notícias, dicas, Mapas, Filmes e TV, Solitaire e semelhantes). Não remove Teams, Xbox app nem Game Bar. Não volta pelo Desfazer (para reinstalar, use a Microsoft Store).")
        {
            Reversible = false;
            Weight = 4;
        }

        protected override bool HasExtra => true;

        private static List<string>? InstalledNames()
        {
            try
            {
                using (RegistryKey? k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages", false))
                {
                    if (k == null) return null;
                    return k.GetSubKeyNames().ToList();
                }
            }
            catch
            {
                return null;
            }
        }

        protected override bool? ExtraApplied()
        {
            List<string>? names = InstalledNames();
            if (names == null) return ActionStore.Has(Id);
            foreach (string p in Packages)
            {
                if (names.Any(n => n.StartsWith(p + "_", StringComparison.OrdinalIgnoreCase))) return false;
            }
            return true;
        }

        protected override string? ExtraApply(Dictionary<string, string> bk)
        {
            if (ExtraApplied() == true) return null;
            var sb = new StringBuilder();
            sb.Append("$names = @(");
            sb.Append(string.Join(",", Packages.Select(p => "'" + p + "'")));
            sb.Append("); foreach ($n in $names) { try { Get-AppxPackage -AllUsers -Name $n | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue } catch { } }; 'OK'");
            Shell.PowerShell(sb.ToString(), 300000, out string? err);
            if (err != null) return err;
            bk["done"] = "1";
            return null;
        }
    }

    // =====================================================================
    // 11. Perfil NVIDIA para os seus jogos
    // =====================================================================
    internal sealed class NvidiaAction : BoostAction
    {
        private static List<string>? _gpus;
        private static List<string> Gpus => _gpus ??= MsiMode.Find("Display", "10DE");

        public NvidiaAction() : base("nvidia-games",
            "Perfil NVIDIA para os seus jogos",
            "Aplica em cada jogo da sua lista um perfil de desempenho da NVIDIA (12 opções, como baixa latência e energia no máximo), liga o modo MSI da placa de vídeo e desliga a telemetria da NVIDIA.")
        {
            Weight = 2;
            var t = new Tweak { Id = Id, Title = Title, Description = Description, NeedsAdmin = true };
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Services\NvTelemetryContainer", "Start", 4) { OnlyIfExists = true });
            t.Items.Add(new RegItem(true, @"SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client", "OptInOrOutPreference", 0) { OnlyIfExists = true });
            t.Items.Add(new RegItem(true, @"SOFTWARE\NVIDIA Corporation\Global\FTS", "EnableRID44231", 0) { OnlyIfExists = true });
            t.Items.Add(new RegItem(true, @"SOFTWARE\NVIDIA Corporation\Global\FTS", "EnableRID64640", 0) { OnlyIfExists = true });
            t.Items.Add(new RegItem(true, @"SOFTWARE\NVIDIA Corporation\Global\FTS", "EnableRID66610", 0) { OnlyIfExists = true });
            Reg = t;
        }

        private static bool? _nvPresent;

        /// <summary>Placa NVIDIA instalada? Lê a lista de placas de vídeo do Windows, que qualquer usuário pode ler.</summary>
        private static bool NvidiaPresent()
        {
            if (_nvPresent.HasValue) return _nvPresent.Value;
            bool found = Gpus.Count > 0;
            if (!found)
            {
                try
                {
                    using (RegistryKey? cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", false))
                    {
                        if (cls != null)
                        {
                            foreach (string name in cls.GetSubKeyNames())
                            {
                                using (RegistryKey? k = cls.OpenSubKey(name, false))
                                {
                                    string txt = (k?.GetValue("ProviderName") as string ?? "") + " " + (k?.GetValue("DriverDesc") as string ?? "");
                                    if (txt.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) { found = true; break; }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // sem leitura: tenta o próximo jeito
                }
            }
            if (!found) found = File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"));
            _nvPresent = found;
            return found;
        }

        public override bool IsApplicable() => NvidiaPresent();

        public override string NotApplicableText => Loc.T("Não se aplica: a placa de vídeo não é NVIDIA.");

        protected override bool HasExtra => true;

        private static List<KeyValuePair<string, string>> Games()
        {
            var list = new List<KeyValuePair<string, string>>();
            Func<List<KeyValuePair<string, string>>>? provider = BoostCatalog.GameProvider;
            if (provider != null) list = provider();
            return list;
        }

        protected override bool? ExtraApplied()
        {
            if (Gpus.Count > 0 && !MsiMode.AllOn(Gpus)) return false;
            foreach (KeyValuePair<string, string> g in Games())
            {
                if (!NvidiaProfileService.Has(g.Key)) return false;
            }
            return true;
        }

        protected override string? ExtraApply(Dictionary<string, string> bk)
        {
            var errors = new List<string>();
            string? msi = MsiMode.Apply(Gpus, bk, "msi:");
            if (msi != null) errors.Add(msi);
            foreach (KeyValuePair<string, string> g in Games())
            {
                string? e = NvidiaProfileService.Apply(g.Key, g.Value);
                if (e != null) errors.Add(e);
            }
            return errors.Count == 0 ? null : string.Join(" | ", errors);
        }

        protected override string? ExtraUndo(Dictionary<string, string> bk)
        {
            var errors = new List<string>();
            foreach (string exe in NvidiaProfileService.Applied())
            {
                string? e = NvidiaProfileService.Undo(exe);
                if (e != null) errors.Add(e);
            }
            string? msi = MsiMode.Undo(bk, "msi:");
            if (msi != null) errors.Add(msi);
            return errors.Count == 0 ? null : string.Join(" | ", errors);
        }
    }

    // =====================================================================
    // 12. USB, mouse e teclado
    // =====================================================================
    internal sealed class InputAction : BoostAction
    {
        private static List<string>? _usb;
        private static List<string> Usb => _usb ??= MsiMode.Find("USB", null);

        public InputAction() : base("usb-input",
            "USB, mouse e teclado",
            "Desliga a economia de energia do USB, liga o modo MSI nos controladores USB, desliga a aceleração do mouse (1:1 com os pixels), acelera a repetição do teclado e desliga as teclas de aderência e de filtro. A fila de dados do mouse e do teclado segue o tipo do seu processador.")
        {
            NeedsReboot = true;
            var t = new Tweak { Id = Id, Title = Title, Description = Description, NeedsAdmin = true };
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Services\USB", "DisableSelectiveSuspend", 1) { OnlyIfExists = true });
            t.Items.Add(new RegItem(false, @"Control Panel\Mouse", "MouseSpeed", "0"));
            t.Items.Add(new RegItem(false, @"Control Panel\Mouse", "MouseThreshold1", "0"));
            t.Items.Add(new RegItem(false, @"Control Panel\Mouse", "MouseThreshold2", "0"));
            t.Items.Add(new RegItem(false, @"Control Panel\Keyboard", "KeyboardDelay", "0"));
            t.Items.Add(new RegItem(false, @"Control Panel\Keyboard", "KeyboardSpeed", "31"));
            t.Items.Add(new RegItem(false, @"Control Panel\Accessibility\StickyKeys", "Flags", "506"));
            t.Items.Add(new RegItem(false, @"Control Panel\Accessibility\Keyboard Response", "Flags", "122"));
            t.Items.Add(new RegItem(false, @"Control Panel\Accessibility\ToggleKeys", "Flags", "58"));
            int queue = QueueSize();
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", queue) { OnlyIfExists = true, Optional = true });
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", queue) { OnlyIfExists = true, Optional = true });
            Reg = t;
        }

        /// <summary>Tamanho da fila de dados do mouse/teclado: processador mais forte → fila menor. O Windows usa 100 por padrão.</summary>
        private static int QueueSize()
        {
            int threads = Environment.ProcessorCount;
            if (threads >= 12) return 16;
            if (threads >= 8) return 20;
            return 32;
        }

        protected override bool HasExtra => true;

        protected override bool? ExtraApplied() => MsiMode.AllOn(Usb);

        protected override string? ExtraApply(Dictionary<string, string> bk)
        {
            string? msi = MsiMode.Apply(Usb, bk, "msi:");
            MouseApplyNow();
            return msi;
        }

        protected override string? ExtraUndo(Dictionary<string, string> bk)
        {
            return MsiMode.Undo(bk, "msi:");
        }

        /// <summary>Aplica já a aceleração do mouse do registro, sem precisar sair do Windows.</summary>
        public static void MouseApplyNow()
        {
            try
            {
                int speed = ParseInt(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed");
                int t1 = ParseInt(Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1");
                int t2 = ParseInt(Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2");
                NativeMethods.SetMouseAcceleration(t1, t2, speed);
            }
            catch
            {
                // vale no próximo login
            }
        }

        private static int ParseInt(RegistryKey root, string path, string name)
        {
            using (RegistryKey? k = root.OpenSubKey(path, false))
            {
                string s = Convert.ToString(k?.GetValue(name), CultureInfo.InvariantCulture) ?? "0";
                return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
            }
        }
    }

    // =====================================================================
    // Pausar atualizações do Windows (botão à parte; não faz parte do Otimizar)
    // =====================================================================
    internal sealed class WindowsUpdateAction : BoostAction
    {
        public WindowsUpdateAction() : base("wu-pause",
            "Pausar atualizações do Windows",
            "Impede o Windows de baixar e instalar atualizações sozinho. Não mexe no Windows Defender.")
        {
            NeedsReboot = true;
            var t = new Tweak { Id = Id, Title = Title, Description = Description, NeedsAdmin = true };
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1));
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", 1));
            // Sem estes dois, a página de Configurações ainda sai procurando atualização quando é aberta.
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "SetDisableUXWUAccess", 1));
            t.Items.Add(new RegItem(true, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DoNotConnectToWindowsUpdateInternetLocations", 1));
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", 4) { OnlyIfExists = true });
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Services\UsoSvc", "Start", 4) { OnlyIfExists = true });
            // O Windows protege este serviço; se recusar, o bloqueio continua valendo pelos itens acima.
            t.Items.Add(new RegItem(true, @"SYSTEM\CurrentControlSet\Services\WaaSMedicSvc", "Start", 4) { OnlyIfExists = true, Optional = true });
            Reg = t;
        }

        protected override bool HasExtra => true;

        protected override bool? ExtraApplied() => null;

        protected override string? ExtraApply(Dictionary<string, string> bk)
        {
            // Para os serviços agora, sem esperar o reinício.
            Shell.Sys("sc.exe", 20000, "stop", "wuauserv");
            Shell.Sys("sc.exe", 20000, "stop", "UsoSvc");
            bk["stopped"] = "1";
            return null;
        }
    }
}
