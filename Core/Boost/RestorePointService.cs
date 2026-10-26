using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GBooster.Core.Boost
{
    public enum RestoreState { Unknown, None, Recent, Old, Expired }

    public sealed class RestoreInfo
    {
        public RestoreState State { get; set; } = RestoreState.Unknown;
        public DateTime Created { get; set; }
        public int Days { get; set; }
        public string Error { get; set; } = "";
        /// <summary>Ponto existente e ainda dentro do prazo (até 60 dias).</summary>
        public bool Valid => State == RestoreState.Recent || State == RestoreState.Old;
        public bool Exists => State == RestoreState.Recent || State == RestoreState.Old || State == RestoreState.Expired;
    }

    public sealed class RestoreResult
    {
        public bool Ok { get; set; }
        /// <summary>O Windows só cria 1 ponto a cada 24 horas; já existe um recente.</summary>
        public bool TooSoon { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Consulta e cria o ponto de restauração do Windows usando os comandos do próprio Windows (PowerShell),
    /// com texto simples e sem nada escondido. Tudo isso exige o modo administrador.
    /// </summary>
    public static class RestorePointService
    {
        public const int OldAfterDays = 30;      // até aqui: "em dia"
        public const int ExpiredAfterDays = 60;  // depois daqui: "vencida"

        private const string QueryScript =
            "$ProgressPreference='SilentlyContinue'; " +
            "try { $r = Get-ComputerRestorePoint -ErrorAction Stop | Sort-Object SequenceNumber | Select-Object -Last 1; " +
            "if ($null -eq $r) { 'NONE' } else { 'OK|' + [string]$r.CreationTime } } " +
            "catch { 'ERR|' + $_.Exception.Message }";

        private const string CreateScript =
            "$ProgressPreference='SilentlyContinue'; " +
            "try { Enable-ComputerRestore -Drive ($env:SystemDrive + '\\') -ErrorAction Stop; " +
            "Checkpoint-Computer -Description 'GBooster' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop -WarningAction SilentlyContinue -WarningVariable w; " +
            "if ($w) { 'WARN|' + ($w -join ' ') } else { 'OK' } } " +
            "catch { 'ERR|' + $_.Exception.Message }";

        public static RestoreInfo Query()
        {
            var info = new RestoreInfo();
            if (!AdminHelper.IsAdmin)
            {
                info.State = RestoreState.Unknown;
                return info;
            }

            string? err;
            string output = RunPowerShell(QueryScript, 60000, out err);
            if (err != null)
            {
                info.Error = err;
                return info;
            }

            string line = output.Trim().TrimStart('﻿');
            if (line.StartsWith("NONE", StringComparison.Ordinal))
            {
                info.State = RestoreState.None;
            }
            else if (line.StartsWith("OK|", StringComparison.Ordinal))
            {
                DateTime? when = ParseDate(line.Substring(3));
                if (when == null)
                {
                    info.Error = Loc.T("Não entendi a data do ponto de restauração.");
                    return info;
                }
                info.Created = when.Value;
                info.Days = Math.Max(0, (int)Math.Floor((DateTime.Now - when.Value).TotalDays));
                info.State = info.Days <= OldAfterDays ? RestoreState.Recent
                           : info.Days <= ExpiredAfterDays ? RestoreState.Old
                           : RestoreState.Expired;
            }
            else
            {
                info.Error = line.StartsWith("ERR|", StringComparison.Ordinal) ? line.Substring(4) : line;
            }
            return info;
        }

        public static RestoreResult Create()
        {
            var res = new RestoreResult();
            if (!AdminHelper.IsAdmin)
            {
                res.Message = Loc.T("Precisa do modo administrador.");
                return res;
            }

            string? err;
            string output = RunPowerShell(CreateScript, 180000, out err);
            if (err != null)
            {
                res.Message = err;
                return res;
            }

            string line = output.Trim().TrimStart('﻿');
            if (line.StartsWith("OK", StringComparison.Ordinal))
            {
                res.Ok = true;
                return res;
            }

            string msg = line.StartsWith("ERR|", StringComparison.Ordinal) || line.StartsWith("WARN|", StringComparison.Ordinal)
                ? line.Substring(line.IndexOf('|') + 1)
                : line;
            if (msg.Contains("1440"))
            {
                res.TooSoon = true;
                res.Message = Loc.T("O Windows só cria um ponto de restauração a cada 24 horas, e já existe um recente.");
            }
            else
            {
                res.Message = msg;
            }
            return res;
        }

        /// <summary>Abre o assistente de restauração do próprio Windows. Devolve null se abriu, ou o erro.</summary>
        public static string? OpenWizard()
        {
            try
            {
                Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ------------------------------------------------------------------
        private static DateTime? ParseDate(string s)
        {
            s = s.Trim();
            // Formato do WMI: 20261025181234.123456-180
            if (s.Length >= 14 && s.Take(14).All(char.IsDigit)
                && DateTime.TryParseExact(s.Substring(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime wmi))
            {
                return wmi;
            }
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime plain))
            {
                return plain;
            }
            return null;
        }

        private static string RunPowerShell(string script, int timeoutMs, out string? error)
        {
            error = null;
            try
            {
                string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                if (!File.Exists(exe))
                {
                    error = Loc.T("Não encontrei o PowerShell do Windows.");
                    return "";
                }

                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-NonInteractive");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " + script);

                using (Process? p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        error = Loc.T("Não consegui abrir o PowerShell.");
                        return "";
                    }
                    Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                    Task<string> errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(true); } catch { /* já encerrou */ }
                        error = Loc.T("O Windows demorou demais para responder.");
                        return "";
                    }
                    string o = outTask.GetAwaiter().GetResult();
                    errTask.GetAwaiter().GetResult();   // só esvazia o canal de erros
                    return o;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return "";
            }
        }
    }
}
