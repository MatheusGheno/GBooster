using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GBooster.Core.Boost;
using Microsoft.Win32;

namespace GBooster.Core
{
    public sealed class InfoRow
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        /// <summary>Etiqueta de aviso ao lado do valor (ex.: perfil XMP inativo).</summary>
        public string Tag { get; set; } = "";
        public string TagTip { get; set; } = "";
        /// <summary>true = valor em destaque verde (ex.: Resizable BAR ativo).</summary>
        public bool Good { get; set; }
    }

    public sealed class DiskInfo
    {
        public string Letter { get; set; } = "";
        public string Kind { get; set; } = "";
        public long Total { get; set; }
        public long Used { get; set; }
        public int Percent => Total <= 0 ? 0 : (int)Math.Round(Used * 100.0 / Total);
    }

    public sealed class SystemSummary
    {
        public List<InfoRow> Cpu { get; } = new List<InfoRow>();
        public List<InfoRow> Gpu { get; } = new List<InfoRow>();
        public List<DiskInfo> Disks { get; } = new List<DiskInfo>();
        public List<InfoRow> Windows { get; } = new List<InfoRow>();
        /// <summary>Última medição do relógio (preenchida pela tela, para ir junto no resumo).</summary>
        public List<InfoRow> Timer { get; } = new List<InfoRow>();
        public string Error { get; set; } = "";

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("GBooster · " + Loc.T("Meu sistema"));
            Section(sb, Loc.T("Processador e memória"), Cpu);
            Section(sb, Loc.T("Placa de vídeo"), Gpu);
            sb.AppendLine();
            sb.AppendLine(Loc.T("Discos"));
            foreach (DiskInfo d in Disks)
            {
                sb.AppendLine("  " + d.Letter + ": " + SystemInfoService.FormatBytes(d.Used) + " / " + SystemInfoService.FormatBytes(d.Total) + (d.Kind.Length > 0 ? " (" + d.Kind + ")" : ""));
            }
            Section(sb, "Windows", Windows);
            if (Timer.Count > 0) Section(sb, Loc.T("Timing do Windows"), Timer);
            return sb.ToString();
        }

        /// <summary>Texto para o Bloco de Notas, na mesma ordem e com a mesma cara da página: seções, linhas alinhadas e barras dos discos.</summary>
        public string ToReport()
        {
            const int width = 60;
            var sb = new StringBuilder();
            string ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
            sb.AppendLine("GBOOSTER · " + Loc.T("Meu sistema").ToUpper(CultureInfo.CurrentCulture) + (ver.Length > 0 ? "  (v" + ver + ")" : ""));
            sb.AppendLine(Loc.T("Gerado em") + " " + DateTime.Now.ToString("g", CultureInfo.CurrentCulture));
            sb.AppendLine(Loc.T("Só leitura: nada foi alterado no PC."));
            ReportSection(sb, Loc.T("Processador e memória"), Cpu, width);
            ReportSection(sb, Loc.T("Placa de vídeo"), Gpu, width);

            sb.AppendLine();
            sb.AppendLine(Loc.T("Discos").ToUpper(CultureInfo.CurrentCulture));
            sb.AppendLine(new string('─', width));
            if (Disks.Count == 0) sb.AppendLine(Loc.T("Nenhum disco encontrado."));
            foreach (DiskInfo d in Disks)
            {
                int filled = Math.Max(0, Math.Min(20, (int)Math.Round(d.Percent / 5.0)));
                sb.AppendLine(d.Letter + ":" + (d.Kind.Length > 0 ? "  ·  " + d.Kind : ""));
                sb.AppendLine("  [" + new string('█', filled) + new string('░', 20 - filled) + "]  " + d.Percent + "%");
                sb.AppendLine("  " + SystemInfoService.FormatBytes(d.Used) + " / " + SystemInfoService.FormatBytes(d.Total));
                if (d.Percent > 85) sb.AppendLine("  ! " + Loc.T("Pouco espaço livre. Use a página Limpeza."));
                sb.AppendLine();
            }
            ReportSection(sb, "Windows", Windows, width);
            if (Timer.Count > 0) ReportSection(sb, Loc.T("Timing do Windows"), Timer, width);
            return sb.ToString();
        }

        private static void ReportSection(StringBuilder sb, string title, List<InfoRow> rows, int width)
        {
            sb.AppendLine();
            sb.AppendLine(title.ToUpper(CultureInfo.CurrentCulture));
            sb.AppendLine(new string('─', width));
            foreach (InfoRow r in rows)
            {
                sb.AppendLine(r.Label.PadRight(26) + r.Value + (r.Tag.Length > 0 ? "   [" + r.Tag + "]" : ""));
            }
        }

        private static void Section(StringBuilder sb, string title, List<InfoRow> rows)
        {
            sb.AppendLine();
            sb.AppendLine(title);
            foreach (InfoRow r in rows)
            {
                sb.AppendLine("  " + r.Label + ": " + r.Value + (r.Tag.Length > 0 ? " [" + r.Tag + "]" : ""));
            }
        }
    }

    /// <summary>Lê as informações do PC só para mostrar (nada é alterado). Usa os dados do próprio Windows.</summary>
    public static class SystemInfoService
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static long _ram = -1;

        public static long TotalRamBytes()
        {
            if (_ram >= 0) return _ram;
            try
            {
                var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                _ram = GlobalMemoryStatusEx(ref ms) ? (long)ms.ullTotalPhys : 0;
            }
            catch
            {
                _ram = 0;
            }
            return _ram;
        }

        public static string FormatBytes(long bytes)
        {
            double gb = bytes / 1073741824.0;
            if (gb >= 100) return gb.ToString("0", CultureInfo.CurrentCulture) + " GB";
            if (gb >= 1) return gb.ToString("0.#", CultureInfo.CurrentCulture) + " GB";
            double mb = bytes / 1048576.0;
            if (mb >= 1) return mb.ToString("0", CultureInfo.CurrentCulture) + " MB";
            return (bytes / 1024) + " KB";
        }

        private const string Script =
            "$ErrorActionPreference='SilentlyContinue'; " +
            "$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1; " +
            "$mem = @(Get-CimInstance Win32_PhysicalMemory); " +
            "$bb = Get-CimInstance Win32_BaseBoard | Select-Object -First 1; " +
            "$bios = Get-CimInstance Win32_BIOS | Select-Object -First 1; " +
            "$gpus = @(Get-CimInstance Win32_VideoController); " +
            "$os = Get-CimInstance Win32_OperatingSystem | Select-Object -First 1; " +
            "$vols = @(); " +
            "foreach ($v in @(Get-Volume | Where-Object { $_.DriveType -eq 'Fixed' -and $_.DriveLetter })) { " +
            "  $media=''; try { $media = [string](Get-Partition -DriveLetter $v.DriveLetter | Get-Disk | Get-PhysicalDisk).MediaType } catch {} " +
            "  $bus=''; try { $bus = [string](Get-Partition -DriveLetter $v.DriveLetter | Get-Disk | Get-PhysicalDisk).BusType } catch {} " +
            "  $vols += [pscustomobject]@{ Letter=[string]$v.DriveLetter; Size=[int64]$v.Size; Free=[int64]$v.SizeRemaining; Media=$media; Bus=$bus } } " +
            "$o = [pscustomobject]@{ " +
            "  CpuName=[string]$cpu.Name; Cores=[int]$cpu.NumberOfCores; Threads=[int]$cpu.NumberOfLogicalProcessors; " +
            "  Mem=@($mem | ForEach-Object { [pscustomobject]@{ Cap=[int64]$_.Capacity; Speed=[int]$_.Speed; Conf=[int]$_.ConfiguredClockSpeed; Type=[int]$_.SMBIOSMemoryType; Part=([string]$_.PartNumber).Trim() } }); " +
            "  Board=(([string]$bb.Manufacturer).Trim() + ' ' + ([string]$bb.Product).Trim()).Trim(); Bios=[string]$bios.SMBIOSBIOSVersion; " +
            "  Gpus=@($gpus | ForEach-Object { [pscustomobject]@{ Name=[string]$_.Name; Driver=[string]$_.DriverVersion; Date=$(if ($_.DriverDate) { $_.DriverDate.ToString('MM/yyyy') } else { '' }) } }); " +
            "  OsName=[string]$os.Caption; OsBuild=[string]$os.BuildNumber; OsVer=[string]$os.Version; Vols=$vols } ; " +
            "$o | ConvertTo-Json -Depth 5 -Compress";

        /// <summary>Monta o resumo do PC. Pode levar alguns segundos: chame fora da tela principal.</summary>
        public static SystemSummary Read()
        {
            var sum = new SystemSummary();

            string json = Shell.PowerShell(Script, 60000, out string? err);
            JsonElement root = default;
            bool haveJson = false;
            if (err == null && json.Trim().Length > 0)
            {
                try
                {
                    root = JsonDocument.Parse(json.Trim()).RootElement;
                    haveJson = true;
                }
                catch
                {
                    haveJson = false;
                }
            }
            if (!haveJson) sum.Error = err ?? Loc.T("Não consegui ler as informações do PC.");

            // ---------------- Processador e memória ----------------
            string cpuName = haveJson ? Str(root, "CpuName") : RegistryCpuName();
            if (cpuName.Length == 0) cpuName = RegistryCpuName();
            string cpuLine = cpuName;
            if (haveJson && Int(root, "Cores") > 0)
            {
                cpuLine += " · " + Loc.F("{0} núcleos, {1} threads", Int(root, "Cores"), Int(root, "Threads"));
            }
            sum.Cpu.Add(new InfoRow { Label = Loc.T("Processador"), Value = cpuLine });
            sum.Cpu.Add(MemoryRow(haveJson ? root : (JsonElement?)null));
            if (haveJson)
            {
                string board = Str(root, "Board");
                string bios = Str(root, "Bios");
                if (board.Length > 0) sum.Cpu.Add(new InfoRow { Label = Loc.T("Placa-mãe"), Value = board + (bios.Length > 0 ? " · BIOS " + bios : "") });
            }

            // ---------------- Placa de vídeo e monitores ----------------
            AddGpu(sum, haveJson ? root : (JsonElement?)null);
            AddMonitors(sum);

            // ---------------- Discos ----------------
            if (haveJson && root.TryGetProperty("Vols", out JsonElement vols))
            {
                foreach (JsonElement v in Elements(vols))
                {
                    long size = Long(v, "Size");
                    long free = Long(v, "Free");
                    if (size <= 0) continue;
                    sum.Disks.Add(new DiskInfo
                    {
                        Letter = Str(v, "Letter"),
                        Total = size,
                        Used = Math.Max(0, size - free),
                        Kind = DiskKind(Str(v, "Media"), Str(v, "Bus")),
                    });
                }
            }

            // ---------------- Windows ----------------
            string osName = haveJson ? Str(root, "OsName") : "";
            string build = haveJson ? Str(root, "OsBuild") : "";
            if (osName.Length > 0) sum.Windows.Add(new InfoRow { Label = Loc.T("Versão"), Value = osName + (build.Length > 0 ? " · " + Loc.T("compilação") + " " + build : "") });
            sum.Windows.Add(new InfoRow { Label = Loc.T("Modo Jogo"), Value = GameModeOn() ? Loc.T("Ligado") : Loc.T("Desligado") });
            int hags = HagsMode();
            sum.Windows.Add(new InfoRow { Label = Loc.T("Agendamento de GPU acelerado"), Value = hags == 2 ? Loc.T("Ligado") : hags == 1 ? Loc.T("Desligado") : Loc.T("Não disponível") });
            double ms = TimerService.CurrentMs();
            if (ms > 0) sum.Windows.Add(new InfoRow { Label = Loc.T("Timing do Windows"), Value = ms.ToString("0.0#", CultureInfo.CurrentCulture) + " ms" });
            return sum;
        }

        // ------------------------------------------------------------------
        private static InfoRow MemoryRow(JsonElement? root)
        {
            var row = new InfoRow { Label = Loc.T("Memória RAM") };
            long total = TotalRamBytes();
            string size = total > 0 ? FormatBytes((long)(Math.Ceiling(total / 1073741824.0) * 1073741824.0)) : "";
            if (root == null || !root.Value.TryGetProperty("Mem", out JsonElement mem))
            {
                row.Value = size.Length > 0 ? size : "—";
                return row;
            }

            int sticks = 0, speed = 0, type = 0;
            var parts = new List<string>();
            foreach (JsonElement m in Elements(mem))
            {
                sticks++;
                int conf = Int(m, "Conf");
                int rated = Int(m, "Speed");
                int s = conf > 0 ? conf : rated;
                if (s > speed) speed = s;
                if (type == 0) type = Int(m, "Type");
                parts.Add(Str(m, "Part"));
            }
            string ddr = type == 34 ? "DDR5" : type == 26 ? "DDR4" : type == 24 ? "DDR3" : "";
            var sb = new StringBuilder(size);
            if (ddr.Length > 0) sb.Append(" " + ddr);
            if (speed > 0) sb.Append(" · " + speed + " MHz");
            if (sticks > 0) sb.Append(" (" + Loc.F("{0} pentes", sticks) + ")");
            row.Value = sb.ToString().Trim();

            // Perfil XMP/EXPO: o Windows não informa se está ligado. Pela referência do próprio pente (ex.: ...3200C16)
            // dá para saber se roda abaixo do que o kit promete. Sem essa pista, só compara com a velocidade padrão.
            int hint = 0;
            foreach (string p in parts)
            {
                foreach (Match mt in Regex.Matches(p, @"(?<!\d)(\d{4})(?!\d)"))
                {
                    int v = int.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (v >= 2400 && v <= 9000 && v > hint) hint = v;
                }
            }
            bool jedec = (type == 34 && speed > 0 && speed <= 4800) || (type != 34 && speed > 0 && speed <= 2666);
            if (hint > 0 && speed > 0 && hint > speed + 100)
            {
                row.Tag = Loc.T("PERFIL XMP/EXPO INATIVO");
                row.TagTip = Loc.F("O kit parece ser de {0} MHz, mas roda a {1} MHz. Ative o XMP/EXPO na BIOS para usar a velocidade completa.", hint, speed);
            }
            else if (hint == 0 && jedec)
            {
                row.Tag = Loc.T("TALVEZ SEM XMP/EXPO");
                row.TagTip = Loc.T("A memória roda na velocidade padrão. Se o seu kit é mais rápido, ative o XMP/EXPO na BIOS.");
            }
            return row;
        }

        private static void AddGpu(SystemSummary sum, JsonElement? root)
        {
            string name = "";
            string driver = "";
            var gpus = new List<(string Name, string Driver, string Date)>();
            if (root != null && root.Value.TryGetProperty("Gpus", out JsonElement g))
            {
                foreach (JsonElement e in Elements(g))
                {
                    string n = Str(e, "Name");
                    if (n.Length == 0 || n.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    gpus.Add((n, Str(e, "Driver"), Str(e, "Date")));
                }
            }
            if (gpus.Count == 0)
            {
                sum.Gpu.Add(new InfoRow { Label = "GPU", Value = "—" });
                return;
            }

            // A placa principal é a primeira da NVIDIA/AMD; sobra a integrada como segunda linha.
            int main = 0;
            for (int i = 0; i < gpus.Count; i++)
            {
                string n = gpus[i].Name;
                if (n.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("Graphics", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    main = i;
                    break;
                }
            }
            name = gpus[main].Name;
            driver = gpus[main].Driver;

            string vramText = "";
            string rebar = "";
            bool nvidia = name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
            if (nvidia)
            {
                ReadNvidiaSmi(out string? smiVram, out string? smiDriver, out string? smiRebar);
                if (smiVram != null) vramText = smiVram;
                if (smiDriver != null) driver = smiDriver;
                if (smiRebar != null) rebar = smiRebar;
            }
            if (vramText.Length == 0)
            {
                long v = RegistryVram(name);
                if (v > 0) vramText = FormatBytes(v);
            }

            sum.Gpu.Add(new InfoRow { Label = "GPU", Value = name + (vramText.Length > 0 ? " · " + vramText : "") });
            string dateText = gpus[main].Date;
            if (driver.Length > 0) sum.Gpu.Add(new InfoRow { Label = Loc.T("Driver"), Value = (nvidia ? NvidiaDriverShort(driver) : driver) + (dateText.Length > 0 ? " · " + dateText : "") });
            if (rebar.Length > 0)
            {
                bool on = rebar == "on";
                sum.Gpu.Add(new InfoRow { Label = "Resizable BAR", Value = on ? Loc.T("Ativo") : Loc.T("Inativo"), Good = on });
            }
            for (int i = 0; i < gpus.Count; i++)
            {
                if (i == main) continue;
                sum.Gpu.Add(new InfoRow { Label = Loc.T("Outra GPU"), Value = gpus[i].Name });
            }
        }

        /// <summary>Driver NVIDIA no formato que a NVIDIA usa (ex.: 566.36), a partir de 32.0.15.6636.</summary>
        private static string NvidiaDriverShort(string v)
        {
            string[] p = v.Split('.');
            if (p.Length == 4 && p[2].Length >= 1 && p[3].Length >= 1)
            {
                string tail = (p[2] + p[3]);
                if (tail.Length >= 5)
                {
                    string last5 = tail.Substring(tail.Length - 5);
                    return last5.Substring(0, 3) + "." + last5.Substring(3);
                }
            }
            return v;
        }

        private static void ReadNvidiaSmi(out string? vram, out string? driver, out string? rebar)
        {
            vram = null; driver = null; rebar = null;
            string? exe = FindNvidiaSmi();
            if (exe == null) return;

            ShellResult r = Shell.Run(exe, 15000, "--query-gpu=memory.total,driver_version", "--format=csv,noheader,nounits");
            if (r.Ok)
            {
                string line = FirstLine(r.Output);
                string[] f = line.Split(',');
                if (f.Length >= 2 && long.TryParse(f[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long mib))
                {
                    vram = FormatBytes(mib * 1048576L);
                    driver = f[1].Trim();
                }
            }

            ShellResult q = Shell.Run(exe, 15000, "-q", "-d", "MEMORY");
            if (q.Ok)
            {
                // Seção "BAR1 Memory Usage" → "Total : 16384 MiB". Com Resizable BAR o BAR1 tem o tamanho da memória de vídeo.
                Match m = Regex.Match(q.Output, @"BAR1 Memory Usage\s*\r?\n\s*Total\s*:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase);
                if (m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bar))
                {
                    rebar = bar > 256 ? "on" : "off";
                }
            }
        }

        private static string? FindNvidiaSmi()
        {
            string[] candidates =
            {
                Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            };
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            return null;
        }

        private static void AddMonitors(SystemSummary sum)
        {
            try
            {
                List<string> devices = DisplayService.GetAttachedDevices();
                int n = 0;
                foreach (string dev in devices)
                {
                    MonitorSnapshot? cur = DisplayService.GetCurrent(dev);
                    if (cur == null) continue;
                    n++;
                    string label = devices.Count > 1 ? Loc.F("Monitor {0}", n) : Loc.T("Monitor");
                    sum.Gpu.Add(new InfoRow { Label = label, Value = cur.Width + "×" + cur.Height + " · " + cur.Hz + " Hz" });
                }
            }
            catch
            {
                // sem monitores: não mostra
            }
        }

        private static string DiskKind(string media, string bus)
        {
            bool nvme = bus.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0;
            if (media.IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0) return nvme ? "SSD NVMe" : "SSD";
            if (media.IndexOf("HDD", StringComparison.OrdinalIgnoreCase) >= 0) return "HDD";
            return nvme ? "NVMe" : "";
        }

        private static bool GameModeOn()
        {
            try
            {
                using (RegistryKey? k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", false))
                {
                    object? v = k?.GetValue("AutoGameModeEnabled", null);
                    return !(v is int i) || i != 0;   // sem valor: o Windows deixa ligado
                }
            }
            catch
            {
                return true;
            }
        }

        private static int HagsMode()
        {
            try
            {
                using (RegistryKey? k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", false))
                {
                    object? v = k?.GetValue("HwSchMode", null);
                    return v is int i ? i : 0;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static string RegistryCpuName()
        {
            try
            {
                using (RegistryKey? k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false))
                {
                    return ((k?.GetValue("ProcessorNameString") as string) ?? "").Trim();
                }
            }
            catch
            {
                return "";
            }
        }

        private static long RegistryVram(string gpuName)
        {
            try
            {
                using (RegistryKey? cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", false))
                {
                    if (cls == null) return 0;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        using (RegistryKey? k = cls.OpenSubKey(sub, false))
                        {
                            if (k == null) continue;
                            string desc = (k.GetValue("DriverDesc") as string) ?? "";
                            if (!string.Equals(desc, gpuName, StringComparison.OrdinalIgnoreCase)) continue;
                            object? v = k.GetValue("HardwareInformation.qwMemorySize", null);
                            if (v is long l && l > 0) return l;
                        }
                    }
                }
            }
            catch
            {
                // sem leitura
            }
            return 0;
        }

        // ---------------- JSON ----------------
        private static IEnumerable<JsonElement> Elements(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement x in e.EnumerateArray()) yield return x;
            }
            else if (e.ValueKind == JsonValueKind.Object)
            {
                yield return e;   // o PowerShell devolve um item só como objeto, não como lista
            }
        }

        private static string Str(JsonElement e, string name)
        {
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                ? (v.GetString() ?? "").Trim()
                : "";
        }

        private static long Long(JsonElement e, string name)
        {
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long l)
                ? l
                : 0;
        }

        private static int Int(JsonElement e, string name) => (int)Math.Min(int.MaxValue, Long(e, name));

        private static string FirstLine(string s)
        {
            using (var r = new StringReader(s))
            {
                return r.ReadLine() ?? "";
            }
        }
    }
}
