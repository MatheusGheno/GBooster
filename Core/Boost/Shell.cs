using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GBooster.Core.Boost
{
    /// <summary>Resultado de um programa do próprio Windows (powercfg, bcdedit, fsutil…).</summary>
    public sealed class ShellResult
    {
        public bool Started { get; set; }
        public bool TimedOut { get; set; }
        public int Code { get; set; } = -1;
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
        public bool Ok => Started && !TimedOut && Code == 0;

        /// <summary>Texto curto para mostrar em caso de erro.</summary>
        public string Brief
        {
            get
            {
                if (!Started) return Loc.T("Não consegui abrir o programa do Windows.");
                if (TimedOut) return Loc.T("O Windows demorou demais para responder.");
                string t = (Output + " " + Error).Trim();
                t = t.Replace("\r", " ").Replace("\n", " ");
                return t.Length > 200 ? t.Substring(0, 200) : t;
            }
        }
    }

    /// <summary>Executa os programas de linha de comando do Windows, sem janela e com tempo limite.</summary>
    public static class Shell
    {
        public static string SysExe(string name) => Path.Combine(Environment.SystemDirectory, name);

        public static ShellResult Run(string exe, int timeoutMs, params string[] args)
        {
            var res = new ShellResult();
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (string a in args) psi.ArgumentList.Add(a);

                using (Process? p = Process.Start(psi))
                {
                    if (p == null) return res;
                    res.Started = true;
                    Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                    Task<string> errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(true); } catch { /* já encerrou */ }
                        res.TimedOut = true;
                        return res;
                    }
                    res.Output = outTask.GetAwaiter().GetResult();
                    res.Error = errTask.GetAwaiter().GetResult();
                    res.Code = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
            }
            return res;
        }

        /// <summary>Atalho para os programas que ficam em System32.</summary>
        public static ShellResult Sys(string exeName, int timeoutMs, params string[] args) => Run(SysExe(exeName), timeoutMs, args);

        /// <summary>Roda um comando do PowerShell do Windows. Devolve o texto de saída; se não deu para rodar, <paramref name="error"/> traz o motivo.</summary>
        public static string PowerShell(string script, int timeoutMs, out string? error)
        {
            error = null;
            string exe = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(exe))
            {
                error = Loc.T("Não encontrei o PowerShell do Windows.");
                return "";
            }
            try
            {
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
                psi.ArgumentList.Add("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; " + script);

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
                    errTask.GetAwaiter().GetResult();
                    return o.TrimStart('﻿');
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
