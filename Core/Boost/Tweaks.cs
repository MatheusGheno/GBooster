using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace GBooster.Core.Boost
{
    public enum TweakState { Applied, Partial, NotApplied, NotApplicable }

    /// <summary>Um valor do registro do Windows e o conteúdo que o ajuste quer deixar nele.</summary>
    public sealed class RegItem
    {
        public bool Machine { get; }          // true = HKEY_LOCAL_MACHINE (exige administrador), false = HKEY_CURRENT_USER
        public string KeyPath { get; }
        public string Name { get; }
        public int? Dword { get; }
        public string? Text { get; }
        /// <summary>Só mexe se a chave já existe (serviços e programas que podem não estar instalados). Nunca cria a chave.</summary>
        public bool OnlyIfExists { get; set; }
        /// <summary>Se o Windows recusar a escrita (valor protegido), ignora em vez de contar como erro.</summary>
        public bool Optional { get; set; }

        public RegItem(bool machine, string keyPath, string name, int dword)
        {
            Machine = machine; KeyPath = keyPath; Name = name; Dword = dword;
        }

        public RegItem(bool machine, string keyPath, string name, string text)
        {
            Machine = machine; KeyPath = keyPath; Name = name; Text = text;
        }

        public string Display => (Machine ? "HKLM\\" : "HKCU\\") + KeyPath + "\\" + Name;
    }

    /// <summary>Um ajuste do Windows: um grupo de valores do registro que são aplicados e desfeitos juntos.</summary>
    public sealed class Tweak
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public bool NeedsAdmin { get; set; }
        public bool NeedsReboot { get; set; }
        /// <summary>Quando não é nulo e devolve false, o ajuste não faz sentido neste PC ("não se aplica").</summary>
        public Func<bool>? Applicable { get; set; }
        public List<RegItem> Items { get; } = new List<RegItem>();
    }

    public sealed class TweakResult
    {
        public bool Ok { get; set; }
        /// <summary>Nada mudou porque já estava como pedido (ou não havia o que desfazer).</summary>
        public bool NoChange { get; set; }
        public bool NeedsReboot { get; set; }
        /// <summary>Não se aplica a este PC (pulado, sem erro).</summary>
        public bool Skipped { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class BackupEntry
    {
        public bool Machine { get; set; }
        public string KeyPath { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Existed { get; set; }
        public string Kind { get; set; } = "";
        public string Data { get; set; } = "";
    }

    public sealed class TweakBackup
    {
        public DateTime Date { get; set; } = DateTime.Now;
        public List<BackupEntry> Entries { get; set; } = new List<BackupEntry>();
    }

    /// <summary>
    /// Aplica e desfaz ajustes. Antes de mexer, guarda o valor que estava (tweaks.json em %AppData%\GBooster),
    /// então "Desfazer" devolve exatamente o que havia, ou apaga o valor se ele não existia.
    /// </summary>
    public static class TweakService
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        private static string BackupPath => Path.Combine(Store.Folder, "tweaks.json");

        private static RegistryKey Root(bool machine) => machine ? Registry.LocalMachine : Registry.CurrentUser;

        // ---------------- Estado ----------------
        private static bool Matches(RegItem it)
        {
            try
            {
                using (RegistryKey? k = Root(it.Machine).OpenSubKey(it.KeyPath, false))
                {
                    if (k == null && (it.OnlyIfExists || it.Optional)) return true;   // nada para ajustar neste PC
                    object? cur = k?.GetValue(it.Name, null);
                    if (cur == null) return false;
                    if (it.Text != null) return string.Equals(Convert.ToString(cur, CultureInfo.InvariantCulture), it.Text, StringComparison.OrdinalIgnoreCase);
                    return cur is int i && i == (it.Dword ?? 0);
                }
            }
            catch
            {
                return false;
            }
        }

        public static TweakState GetState(Tweak t)
        {
            if (t.Applicable != null && !t.Applicable()) return TweakState.NotApplicable;
            List<RegItem> required = t.Items.Where(i => !i.Optional).ToList();
            int ok = required.Count(Matches);
            if (ok == required.Count) return TweakState.Applied;
            return ok == 0 ? TweakState.NotApplied : TweakState.Partial;
        }

        public static bool HasBackup(Tweak t) => LoadBackups().ContainsKey(t.Id);

        // ---------------- Aplicar ----------------
        public static TweakResult Apply(Tweak t)
        {
            var r = new TweakResult();
            if (t.Applicable != null && !t.Applicable())
            {
                r.Message = Loc.T("Não se aplica a este PC.");
                return r;
            }
            if (GetState(t) == TweakState.Applied)
            {
                r.Ok = true; r.NoChange = true;
                r.Message = Loc.T("Já estava aplicado.");
                return r;
            }
            if (t.NeedsAdmin && !AdminHelper.IsAdmin)
            {
                r.Message = Loc.T("Precisa do modo administrador.");
                return r;
            }

            Dictionary<string, TweakBackup> backups = LoadBackups();
            if (!backups.ContainsKey(t.Id))
            {
                // Primeira vez: guarda como estava. Se já existe backup (ajuste que o Windows desfez), mantém o original.
                var b = new TweakBackup();
                foreach (RegItem it in t.Items) b.Entries.Add(Capture(it));
                backups[t.Id] = b;
                string? saveErr = SaveBackups(backups);
                if (saveErr != null)
                {
                    r.Message = Loc.F("Não consegui guardar o backup: {0}", saveErr);
                    return r;
                }
            }
            else
            {
                // Backup feito por uma versão anterior do app, que talvez controlasse menos valores do que esta.
                // Guarda também os valores que faltam, senão o Desfazer os deixaria para trás.
                TweakBackup old = backups[t.Id];
                bool added = false;
                foreach (RegItem it in t.Items)
                {
                    bool has = old.Entries.Any(e => e.Machine == it.Machine
                                                    && string.Equals(e.KeyPath, it.KeyPath, StringComparison.OrdinalIgnoreCase)
                                                    && string.Equals(e.Name, it.Name, StringComparison.OrdinalIgnoreCase));
                    if (has) continue;
                    BackupEntry extra = Capture(it);
                    // Se o valor já está igual ao do ajuste, foi o próprio app que colocou: antes não existia.
                    string target = it.Text ?? (it.Dword ?? 0).ToString(CultureInfo.InvariantCulture);
                    if (extra.Existed && string.Equals(extra.Data, target, StringComparison.Ordinal))
                    {
                        extra.Existed = false;
                        extra.Data = "";
                    }
                    old.Entries.Add(extra);
                    added = true;
                }
                if (added) SaveBackups(backups);
            }

            var errors = new List<string>();
            foreach (RegItem it in t.Items)
            {
                try { Write(it); }
                catch (Exception ex) { if (!it.Optional) errors.Add(it.Display + ": " + ex.Message); }
            }
            if (errors.Count > 0)
            {
                r.Message = Loc.F("Não consegui aplicar tudo: {0}", string.Join(" | ", errors));
                return r;
            }

            r.Ok = true;
            r.NeedsReboot = t.NeedsReboot;
            r.Message = Loc.T("Aplicado.");
            return r;
        }

        // ---------------- Desfazer ----------------
        public static TweakResult Undo(Tweak t)
        {
            var r = new TweakResult();
            Dictionary<string, TweakBackup> backups = LoadBackups();
            if (!backups.TryGetValue(t.Id, out TweakBackup? b))
            {
                r.Ok = true; r.NoChange = true;
                r.Message = Loc.T("Nada para desfazer: o app não guardou valores anteriores.");
                return r;
            }
            if (t.NeedsAdmin && !AdminHelper.IsAdmin)
            {
                r.Message = Loc.T("Precisa do modo administrador.");
                return r;
            }

            var errors = new List<string>();
            foreach (BackupEntry e in b.Entries)
            {
                // O arquivo de backup fica numa pasta editável: só devolve valores que este ajuste realmente controla.
                bool known = t.Items.Any(i => i.Machine == e.Machine
                                              && string.Equals(i.KeyPath, e.KeyPath, StringComparison.OrdinalIgnoreCase)
                                              && string.Equals(i.Name, e.Name, StringComparison.OrdinalIgnoreCase));
                if (!known) continue;
                bool optional = t.Items.Any(i => i.Optional && i.Machine == e.Machine
                                                 && string.Equals(i.KeyPath, e.KeyPath, StringComparison.OrdinalIgnoreCase)
                                                 && string.Equals(i.Name, e.Name, StringComparison.OrdinalIgnoreCase));
                try { RestoreEntry(e); }
                catch (Exception ex) { if (!optional) errors.Add(e.Name + ": " + ex.Message); }
            }
            if (errors.Count > 0)
            {
                r.Message = Loc.F("Não consegui desfazer tudo: {0}", string.Join(" | ", errors));
                return r;
            }

            backups.Remove(t.Id);
            SaveBackups(backups);
            r.Ok = true;
            r.NeedsReboot = t.NeedsReboot;
            r.Message = Loc.T("Desfeito.");
            return r;
        }

        // ---------------- Registro ----------------
        private static void Write(RegItem it)
        {
            if (it.OnlyIfExists)
            {
                using (RegistryKey? probe = Root(it.Machine).OpenSubKey(it.KeyPath, false))
                {
                    if (probe == null) return;
                }
            }
            using (RegistryKey? k = Root(it.Machine).CreateSubKey(it.KeyPath, true))
            {
                if (k == null) throw new InvalidOperationException(Loc.T("não consegui abrir a chave"));
                if (it.Text != null) k.SetValue(it.Name, it.Text, RegistryValueKind.String);
                else k.SetValue(it.Name, it.Dword ?? 0, RegistryValueKind.DWord);
            }
        }

        private static BackupEntry Capture(RegItem it)
        {
            var e = new BackupEntry { Machine = it.Machine, KeyPath = it.KeyPath, Name = it.Name };
            try
            {
                using (RegistryKey? k = Root(it.Machine).OpenSubKey(it.KeyPath, false))
                {
                    if (k != null && Array.IndexOf(k.GetValueNames(), it.Name) >= 0)
                    {
                        RegistryValueKind kind = k.GetValueKind(it.Name);
                        object? v = k.GetValue(it.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        e.Existed = v != null;
                        e.Kind = kind.ToString();
                        if (v is byte[] bytes) e.Data = Convert.ToBase64String(bytes);
                        else if (v is string[] multi) e.Data = string.Join("\0", multi);
                        else e.Data = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
                    }
                }
            }
            catch
            {
                // sem leitura: trata como "não existia"
            }
            return e;
        }

        private static void RestoreEntry(BackupEntry e)
        {
            if (e.Existed)
            {
                using (RegistryKey? k = Root(e.Machine).CreateSubKey(e.KeyPath, true))
                {
                    if (k == null) throw new InvalidOperationException(Loc.T("não consegui abrir a chave"));
                    RegistryValueKind kind = Enum.TryParse(e.Kind, out RegistryValueKind parsed) ? parsed : RegistryValueKind.String;
                    object val;
                    switch (kind)
                    {
                        case RegistryValueKind.DWord: val = int.Parse(e.Data, CultureInfo.InvariantCulture); break;
                        case RegistryValueKind.QWord: val = long.Parse(e.Data, CultureInfo.InvariantCulture); break;
                        case RegistryValueKind.Binary: val = Convert.FromBase64String(e.Data); break;
                        case RegistryValueKind.MultiString: val = e.Data.Split('\0'); break;
                        default: val = e.Data; break;
                    }
                    k.SetValue(e.Name, val, kind);
                }
            }
            else
            {
                using (RegistryKey? k = Root(e.Machine).OpenSubKey(e.KeyPath, true))
                {
                    k?.DeleteValue(e.Name, false);
                }
            }
        }

        // ---------------- Arquivo de backup ----------------
        private static Dictionary<string, TweakBackup> LoadBackups()
        {
            try
            {
                if (File.Exists(BackupPath))
                {
                    var d = JsonSerializer.Deserialize<Dictionary<string, TweakBackup>>(File.ReadAllText(BackupPath));
                    if (d != null) return d;
                }
            }
            catch
            {
                // arquivo corrompido: começa vazio
            }
            return new Dictionary<string, TweakBackup>();
        }

        private static string? SaveBackups(Dictionary<string, TweakBackup> d)
        {
            try
            {
                File.WriteAllText(BackupPath, JsonSerializer.Serialize(d, Json));
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
