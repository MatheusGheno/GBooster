using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using GBooster.Core.Boost;
using Microsoft.Win32;

namespace GBooster.Core
{
    public sealed class CleanItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Selected { get; set; }
        /// <summary>Tamanho estimado; -1 = o Windows decide na hora (sem estimativa).</summary>
        public long Bytes { get; set; }
        /// <summary>Preenchido depois de limpar.</summary>
        public long Freed { get; set; }
        public int Skipped { get; set; }
        public bool NeedsAdmin { get; set; }
        /// <summary>Não há nada a fazer neste PC (ex.: só 1 ponto de restauração).</summary>
        public bool Empty { get; set; }
        internal Func<List<string>>? Folders { get; set; }
        internal Func<List<string>>? Files { get; set; }
    }

    public sealed class CleanProgress
    {
        public int Percent { get; set; }
        public string Step { get; set; } = "";
    }

    public sealed class CleanSummary
    {
        public long Freed { get; set; }
        public int Skipped { get; set; }
        public bool Interrupted { get; set; }
        public string StoppedAt { get; set; } = "";
        public int Percent { get; set; }
    }

    /// <summary>
    /// Procura e apaga arquivos que o Windows e os programas recriam sozinhos (temporários, caches).
    /// Só apaga dentro de pastas conhecidas, nunca segue atalhos/junções e pula o que estiver em uso.
    /// </summary>
    public static class CleanupService
    {
        private static string Env(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        private static string Local => Env(Environment.SpecialFolder.LocalApplicationData);
        private static string Roaming => Env(Environment.SpecialFolder.ApplicationData);
        private static string ProgramData => Env(Environment.SpecialFolder.CommonApplicationData);
        private static string WinDir => Env(Environment.SpecialFolder.Windows);

        public static List<CleanItem> Catalog()
        {
            return new List<CleanItem>
            {
                new CleanItem
                {
                    Id = "temp", Title = "Arquivos temporários do Windows",
                    Description = "Pastas Temp do usuário e do sistema. O Windows recria sozinho.", Selected = true,
                    Folders = () => new List<string> { Path.GetTempPath(), Path.Combine(WinDir, "Temp") },
                },
                new CleanItem
                {
                    Id = "wu", Title = "Cache de atualizações do Windows",
                    Description = "Arquivos já instalados que sobraram em SoftwareDistribution.", Selected = true, NeedsAdmin = true,
                    Folders = () => new List<string> { Path.Combine(WinDir, "SoftwareDistribution", "Download") },
                },
                new CleanItem
                {
                    Id = "misc", Title = "Lixeira, miniaturas e relatórios de erro",
                    Description = "Inclui os dumps de travamento.", Selected = true,
                    Folders = () => new List<string>
                    {
                        Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"),
                        Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"),
                        Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "Temp"),
                        Path.Combine(Local, "CrashDumps"),
                        Path.Combine(WinDir, "Minidump"),
                    },
                    Files = () =>
                    {
                        var files = new List<string> { Path.Combine(WinDir, "MEMORY.DMP") };
                        string explorer = Path.Combine(Local, "Microsoft", "Windows", "Explorer");
                        try
                        {
                            if (Directory.Exists(explorer)) files.AddRange(Directory.GetFiles(explorer, "thumbcache_*.db"));
                        }
                        catch { /* sem acesso: pula */ }
                        return files;
                    },
                },
                new CleanItem
                {
                    Id = "shaders", Title = "Cache de shaders (NVIDIA e DirectX)",
                    Description = "O jogo refaz na próxima vez e pode dar uma leve travada na primeira partida.",
                    Folders = () => new List<string>
                    {
                        Path.Combine(Local, "NVIDIA", "DXCache"),
                        Path.Combine(Local, "NVIDIA", "GLCache"),
                        Path.Combine(Local, "D3DSCache"),
                        Path.Combine(Local, "AMD", "DxCache"),
                        Path.Combine(ProgramData, "NVIDIA Corporation", "NV_Cache"),
                    },
                },
                new CleanItem
                {
                    Id = "chrome", Title = "Cache do Google Chrome",
                    Description = "Só imagens e arquivos temporários dos sites. Não apaga histórico, senhas, cookies nem logins.",
                    Folders = () => BrowserCaches(Path.Combine(Local, "Google", "Chrome", "User Data")),
                },
                new CleanItem
                {
                    Id = "edge", Title = "Cache do Microsoft Edge",
                    Description = "Só imagens e arquivos temporários dos sites. Não apaga histórico, senhas, cookies nem logins.",
                    Folders = () => BrowserCaches(Path.Combine(Local, "Microsoft", "Edge", "User Data")),
                },
                new CleanItem
                {
                    Id = "firefox", Title = "Cache do Mozilla Firefox",
                    Description = "Só imagens e arquivos temporários dos sites. Não apaga histórico, senhas, cookies nem logins.",
                    Folders = FirefoxCaches,
                },
                new CleanItem
                {
                    Id = "discord", Title = "Cache do Discord",
                    Description = "Imagens e arquivos temporários. O Discord refaz na próxima abertura; você continua logado.",
                    Folders = () => new List<string>
                    {
                        Path.Combine(Roaming, "discord", "Cache"),
                        Path.Combine(Roaming, "discord", "Code Cache"),
                        Path.Combine(Roaming, "discord", "GPUCache"),
                    },
                },
                new CleanItem
                {
                    Id = "steam", Title = "Cache da Steam",
                    Description = "Páginas da loja e da comunidade guardadas no app. Não mexe nos jogos nem nos salvamentos.",
                    Folders = () => new List<string> { Path.Combine(Local, "Steam", "htmlcache") },
                },
                new CleanItem
                {
                    Id = "restore", Title = "Pontos de restauração antigos",
                    Description = "Mantém só o mais recente.", NeedsAdmin = true,
                },
                new CleanItem
                {
                    Id = "winclean", Title = "Limpeza profunda do Windows",
                    Description = "Usa o Liberador de Espaço em Disco do próprio Windows (logs de instalação, arquivos de atualização e de entrega). Pode levar alguns minutos.",
                    NeedsAdmin = true, Bytes = -1,
                },
                new CleanItem
                {
                    Id = "disks", Title = "Otimizar discos",
                    Description = "Roda o TRIM nos SSDs e desfragmenta os HDDs, do jeito que o Windows faz. Pode levar alguns minutos.",
                    NeedsAdmin = true, Bytes = -1,
                },
            };
        }

        private static List<string> BrowserCaches(string root)
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(root)) return list;
                foreach (string profile in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(profile);
                    if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
                    list.Add(Path.Combine(profile, "Cache"));
                    list.Add(Path.Combine(profile, "Code Cache"));
                    list.Add(Path.Combine(profile, "GPUCache"));
                }
            }
            catch
            {
                // sem acesso: pula
            }
            return list;
        }

        private static List<string> FirefoxCaches()
        {
            var list = new List<string>();
            try
            {
                string root = Path.Combine(Local, "Mozilla", "Firefox", "Profiles");
                if (!Directory.Exists(root)) return list;
                foreach (string profile in Directory.GetDirectories(root))
                {
                    list.Add(Path.Combine(profile, "cache2"));
                }
            }
            catch
            {
                // sem acesso: pula
            }
            return list;
        }

        // ------------------------------------------------------------------
        // Análise (não apaga nada)
        // ------------------------------------------------------------------
        public static void Analyze(List<CleanItem> items, IProgress<CleanProgress> progress, CancellationToken cancel)
        {
            for (int i = 0; i < items.Count; i++)
            {
                CleanItem it = items[i];
                if (cancel.IsCancellationRequested) break;
                progress.Report(new CleanProgress { Percent = (int)Math.Round(i * 100.0 / items.Count), Step = Loc.T(it.Title) });

                if (it.Id == "restore")
                {
                    it.Bytes = RestorePointsReclaimable(out bool onlyOne);
                    it.Empty = onlyOne || it.Bytes <= 0;
                }
                else if (it.Id == "winclean" || it.Id == "disks")
                {
                    it.Bytes = -1;
                    it.Empty = false;
                }
                else
                {
                    long total = 0;
                    if (it.Folders != null)
                    {
                        foreach (string f in it.Folders()) total += SizeOfFolder(f, cancel);
                    }
                    if (it.Files != null)
                    {
                        foreach (string f in it.Files()) total += SizeOfFile(f);
                    }
                    if (it.Id == "misc") total += RecycleBinSize();
                    it.Bytes = total;
                    it.Empty = total <= 0;
                }
                if (it.Empty) it.Selected = false;
            }
            progress.Report(new CleanProgress { Percent = 100, Step = Loc.T("Concluído") });
        }

        private static long SizeOfFile(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch { return 0; }
        }

        private static bool IsReparse(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return true; }
        }

        private static long SizeOfFolder(string dir, CancellationToken cancel)
        {
            long total = 0;
            if (cancel.IsCancellationRequested || !Directory.Exists(dir) || IsReparse(dir)) return 0;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir))
                {
                    try { total += new FileInfo(f).Length; } catch { /* em uso */ }
                }
                foreach (string d in Directory.EnumerateDirectories(dir))
                {
                    if (IsReparse(d)) continue;
                    total += SizeOfFolder(d, cancel);
                }
            }
            catch
            {
                // sem acesso: conta o que deu
            }
            return total;
        }

        private static long RecycleBinSize()
        {
            try
            {
                var info = new NativeMethods.SHQUERYRBINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.SHQUERYRBINFO)) };
                return NativeMethods.SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
            }
            catch
            {
                return 0;
            }
        }

        // ------------------------------------------------------------------
        // Limpeza
        // ------------------------------------------------------------------
        public static CleanSummary Clean(List<CleanItem> items, IProgress<CleanProgress> progress, Func<bool> gameOpen, CancellationToken cancel)
        {
            var sum = new CleanSummary();
            List<CleanItem> todo = items.Where(i => i.Selected).ToList();
            for (int i = 0; i < todo.Count; i++)
            {
                CleanItem it = todo[i];
                if (cancel.IsCancellationRequested || gameOpen())
                {
                    sum.Interrupted = true;
                    sum.StoppedAt = Loc.T(it.Title);
                    sum.Percent = (int)Math.Round(i * 100.0 / todo.Count);
                    return sum;
                }
                progress.Report(new CleanProgress { Percent = (int)Math.Round(i * 100.0 / todo.Count), Step = Loc.T(it.Title) });

                if (it.NeedsAdmin && !AdminHelper.IsAdmin)
                {
                    it.Skipped++;
                    sum.Skipped++;
                    continue;
                }

                switch (it.Id)
                {
                    case "restore": CleanRestorePoints(it); break;
                    case "winclean": RunDiskCleanup(it); break;
                    case "disks": OptimizeDisks(it); break;
                    default: CleanFolders(it, cancel); break;
                }
                sum.Freed += it.Freed;
                sum.Skipped += it.Skipped;
            }
            sum.Percent = 100;
            progress.Report(new CleanProgress { Percent = 100, Step = Loc.T("Concluído") });
            return sum;
        }

        private static void CleanFolders(CleanItem it, CancellationToken cancel)
        {
            if (it.Folders != null)
            {
                foreach (string f in it.Folders()) DeleteContents(f, it, cancel);
            }
            if (it.Files != null)
            {
                foreach (string f in it.Files())
                {
                    try
                    {
                        if (!File.Exists(f)) continue;
                        long len = new FileInfo(f).Length;
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                        it.Freed += len;
                    }
                    catch
                    {
                        it.Skipped++;
                    }
                }
            }
            if (it.Id == "misc")
            {
                long before = RecycleBinSize();
                try
                {
                    NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
                        NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);
                    long after = RecycleBinSize();
                    it.Freed += Math.Max(0, before - after);
                }
                catch
                {
                    it.Skipped++;
                }
            }
        }

        private static void DeleteContents(string dir, CleanItem it, CancellationToken cancel)
        {
            // Trava de segurança: nunca limpa raiz de disco nem pasta curta demais.
            if (string.IsNullOrWhiteSpace(dir) || dir.Length < 10 || cancel.IsCancellationRequested) return;
            if (!Directory.Exists(dir) || IsReparse(dir)) return;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir))
                {
                    try
                    {
                        long len = new FileInfo(f).Length;
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                        it.Freed += len;
                    }
                    catch
                    {
                        it.Skipped++;   // em uso ou sem permissão
                    }
                }
                foreach (string d in Directory.EnumerateDirectories(dir))
                {
                    if (IsReparse(d)) continue;
                    DeleteContents(d, it, cancel);
                    try { Directory.Delete(d, false); } catch { /* ainda tem arquivo em uso */ }
                }
            }
            catch
            {
                it.Skipped++;
            }
        }

        // ---------------- Pontos de restauração ----------------
        private static long ParseSize(string text)
        {
            Match m = Regex.Match(text, @"([\d.,]+)\s*(KB|MB|GB|TB)", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            string n = m.Groups[1].Value;
            if (n.Contains(',') && n.Contains('.'))
            {
                // o último separador é o decimal
                int lastComma = n.LastIndexOf(',');
                int lastDot = n.LastIndexOf('.');
                n = lastComma > lastDot ? n.Replace(".", "").Replace(',', '.') : n.Replace(",", "");
            }
            else
            {
                n = n.Replace(',', '.');
            }
            if (!double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return 0;
            double mult = m.Groups[2].Value.ToUpperInvariant() switch
            {
                "KB" => 1024.0,
                "MB" => 1048576.0,
                "GB" => 1073741824.0,
                _ => 1099511627776.0,
            };
            return (long)(v * mult);
        }

        private static long ShadowUsed()
        {
            ShellResult r = Shell.Sys("vssadmin.exe", 30000, "list", "shadowstorage");
            if (!r.Ok) return 0;
            // A primeira medida de tamanho da saída é o espaço usado.
            return ParseSize(r.Output);
        }

        private static int RestorePointCount()
        {
            string o = Shell.PowerShell("try { @(Get-ComputerRestorePoint -ErrorAction Stop).Count } catch { 0 }", 60000, out string? err);
            if (err != null) return 0;
            return int.TryParse(o.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
        }

        private static long RestorePointsReclaimable(out bool onlyOne)
        {
            onlyOne = true;
            if (!AdminHelper.IsAdmin) return 0;
            int n = RestorePointCount();
            if (n <= 1) return 0;
            onlyOne = false;
            long used = ShadowUsed();
            return used <= 0 ? 0 : (long)(used * (n - 1.0) / n);
        }

        private static void CleanRestorePoints(CleanItem it)
        {
            int n = RestorePointCount();
            long before = ShadowUsed();
            int guard = 0;
            while (n > 1 && guard++ < 40)
            {
                ShellResult r = Shell.Sys("vssadmin.exe", 120000, "delete", "shadows", "/for=" + Environment.GetEnvironmentVariable("SystemDrive"), "/oldest", "/quiet");
                if (!r.Ok) { it.Skipped++; break; }
                n--;
            }
            long after = ShadowUsed();
            it.Freed += Math.Max(0, before - after);
        }

        // ---------------- Limpeza profunda e discos ----------------
        private static readonly string[] CleanHandlers =
        {
            "Windows Update Cleanup",
            "Delivery Optimization Files",
            "Temporary Setup Files",
            "Setup Log Files",
            "Old ChkDsk Files",
            "Windows Error Reporting Files",
        };

        private const string VolumeCaches = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";
        private const string FlagName = "StateFlags0099";

        private static void RunDiskCleanup(CleanItem it)
        {
            var touched = new List<string>();
            try
            {
                foreach (string h in CleanHandlers)
                {
                    using (RegistryKey? k = Registry.LocalMachine.OpenSubKey(VolumeCaches + "\\" + h, true))
                    {
                        if (k == null) continue;
                        k.SetValue(FlagName, 2, RegistryValueKind.DWord);
                        touched.Add(h);
                    }
                }
                if (touched.Count == 0) { it.Skipped++; return; }

                long before = FreeSpaceOnSystemDrive();
                ShellResult r = Shell.Run(Shell.SysExe("cleanmgr.exe"), 40 * 60 * 1000, "/sagerun:99");
                if (!r.Started || r.TimedOut) it.Skipped++;
                long after = FreeSpaceOnSystemDrive();
                it.Freed += Math.Max(0, after - before);
            }
            catch
            {
                it.Skipped++;
            }
            finally
            {
                // Deixa o registro como estava.
                foreach (string h in touched)
                {
                    try
                    {
                        using (RegistryKey? k = Registry.LocalMachine.OpenSubKey(VolumeCaches + "\\" + h, true))
                        {
                            k?.DeleteValue(FlagName, false);
                        }
                    }
                    catch { /* não essencial */ }
                }
            }
        }

        private static long FreeSpaceOnSystemDrive()
        {
            try { return new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").AvailableFreeSpace; }
            catch { return 0; }
        }

        private static void OptimizeDisks(CleanItem it)
        {
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    string letter = d.Name.TrimEnd('\\');
                    // /O = a otimização certa para cada tipo (TRIM no SSD, desfragmentar no HDD)
                    ShellResult r = Shell.Sys("defrag.exe", 40 * 60 * 1000, letter, "/O");
                    if (!r.Ok) it.Skipped++;
                }
            }
            catch
            {
                it.Skipped++;
            }
        }
    }
}
