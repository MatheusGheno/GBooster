using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GBooster.Core
{
    /// <summary>Tudo é salvo em %AppData%\GBooster (nada no registro, exceto "iniciar com o Windows").</summary>
    public static class Store
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        public static string Folder
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData, "GBooster");
                if (!Directory.Exists(dir))
                {
                    // nome antigo da pasta (antes do app se chamar GBooster): traz as configurações junto
                    string old = Path.Combine(appData, "GhenoBoost");
                    try { if (Directory.Exists(old)) Directory.Move(old, dir); } catch { /* segue com pasta nova */ }
                }
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string SettingsPath => Path.Combine(Folder, "settings.json");
        private static string AppliedPath => Path.Combine(Folder, "applied.json");
        private static string LogPath => Path.Combine(Folder, "log.json");

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                    if (s != null && s.Games.Count > 0)
                    {
                        Sanitize(s);
                        return s;
                    }
                }
            }
            catch { /* arquivo corrompido: volta ao padrão */ }
            return AppSettings.CreateDefault();
        }

        /// <summary>Salva uma cópia das configurações no caminho escolhido (backup). Devolve null se deu certo, ou a mensagem de erro.</summary>
        public static string? Export(AppSettings s, string path)
        {
            try { File.WriteAllText(path, JsonSerializer.Serialize(s, Json)); return null; }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Lê um backup. Devolve null (e a mensagem) se o arquivo não for um backup válido do GBooster.</summary>
        public static AppSettings? Import(string path, out string? error)
        {
            error = null;
            try
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                if (s == null || s.Games == null || s.Games.Count == 0)
                {
                    error = Loc.T("Esse arquivo não parece um backup do GBooster.");
                    return null;
                }
                Sanitize(s);
                return s;
            }
            catch
            {
                error = Loc.T("Não consegui ler esse arquivo. Ele precisa ser um backup exportado pelo GBooster.");
                return null;
            }
        }

        private static bool ValidMode(int w, int h) => w >= 320 && w <= 16384 && h >= 200 && h <= 16384;

        /// <summary>Corrige valores fora do possível (arquivo editado à mão ou corrompido).</summary>
        private static void Sanitize(AppSettings s)
        {
            if (s.Language != "pt" && s.Language != "en") s.Language = "auto";
            s.WaitAfterOpenSeconds = Math.Max(0, Math.Min(60, s.WaitAfterOpenSeconds));
            s.Games.RemoveAll(g => g == null);
            foreach (GameConfig g in s.Games)
            {
                g.Name = string.IsNullOrWhiteSpace(g.Name) ? "Jogo" : g.Name.Trim();
                g.ProcessName = (g.ProcessName ?? "").Trim();
                if (g.ProcessName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
                {
                    g.ProcessName = System.IO.Path.GetFileName(g.ProcessName);
                }
                if (!ValidMode(g.Width, g.Height)) { g.Width = 1280; g.Height = 960; }
                g.Saturation = Math.Max(0, Math.Min(100, g.Saturation));
            }
        }

        public static void SaveSettings(AppSettings s)
        {
            try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, Json)); } catch { /* sem disco: ignora */ }
        }

        // ---- O que foi alterado e ainda precisa voltar (usado para recuperar após queda) ----
        public static AppliedState? LoadApplied()
        {
            try
            {
                if (File.Exists(AppliedPath))
                {
                    AppliedState? st = JsonSerializer.Deserialize<AppliedState>(File.ReadAllText(AppliedPath));
                    if (st != null)
                    {
                        // O arquivo fica numa pasta que o usuário (e outros programas dele) podem editar:
                        // descarta valores absurdos em vez de mandá-los ao driver de vídeo.
                        st.Monitors.RemoveAll(m => !ValidMode(m.Width, m.Height) || m.Hz < 0 || m.Hz > 1000 || m.Bpp < 8 || m.Bpp > 64
                                                   || !m.Device.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase));
                        st.Vibrance.RemoveAll(v => !v.Device.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase));
                    }
                    return st;
                }
            }
            catch { }
            return null;
        }

        public static void SaveApplied(AppliedState state)
        {
            File.WriteAllText(AppliedPath, JsonSerializer.Serialize(state, Json));
        }

        public static void ClearApplied()
        {
            try { if (File.Exists(AppliedPath)) File.Delete(AppliedPath); } catch { }
        }

        // ---- Log (últimos 500 eventos) ----
        public static List<LogEntry> LoadLog()
        {
            try
            {
                if (File.Exists(LogPath))
                {
                    return JsonSerializer.Deserialize<List<LogEntry>>(File.ReadAllText(LogPath)) ?? new List<LogEntry>();
                }
            }
            catch { }
            return new List<LogEntry>();
        }

        public static void SaveLog(List<LogEntry> entries)
        {
            try { File.WriteAllText(LogPath, JsonSerializer.Serialize(entries, Json)); } catch { }
        }
    }

    public sealed class LogEntry
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string Text { get; set; } = "";
        public string Detail { get; set; } = "";
        /// <summary>True para eventos de "algo foi aplicado" (bolinha laranja).</summary>
        public bool Active { get; set; }
        /// <summary>"game" (troca de tela) ou "boost" (Desempenho, Limpeza).</summary>
        public string Kind { get; set; } = "game";

        [System.Text.Json.Serialization.JsonIgnore]
        public string DateText
        {
            get
            {
                int d = (DateTime.Today - Time.Date).Days;
                if (d <= 0) return Loc.T("hoje");
                if (d == 1) return Loc.T("ontem");
                return Time.ToString("dd/MM");
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string TimeText => Time.ToString("HH:mm:ss");
    }
}
