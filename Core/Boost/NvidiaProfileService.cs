using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace GBooster.Core.Boost
{
    /// <summary>O que o GBooster gravou no driver da NVIDIA para um jogo (nvidia.json).</summary>
    public sealed class NvGameEntry
    {
        public string ProfileName { get; set; } = "";
        /// <summary>true = o perfil foi criado pelo GBooster (no Desfazer, é apagado). false = já existia (volta valor por valor).</summary>
        public bool Created { get; set; }
        /// <summary>Valor de antes de cada opção; -1 = a opção não estava gravada no perfil.</summary>
        public Dictionary<string, long> Prior { get; set; } = new Dictionary<string, long>();
    }

    /// <summary>
    /// Grava, para cada jogo da lista, o perfil de desempenho da NVIDIA (as 12 opções do perfil-base do GBooster).
    /// Só liga o executável do jogo a um perfil no driver; não abre nem lê o jogo.
    /// </summary>
    public static class NvidiaProfileService
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>(identificador, valor) das 12 opções. Origem: GBooster_Base_Profile.nip.</summary>
        private static readonly KeyValuePair<uint, uint>[] Settings =
        {
            new KeyValuePair<uint, uint>(274197361, 1),    // Power management mode: preferir desempenho máximo
            new KeyValuePair<uint, uint>(390467, 2),       // Baixa latência (painel): ultra
            new KeyValuePair<uint, uint>(277041152, 1),    // Baixa latência: ligada
            new KeyValuePair<uint, uint>(8102046, 1),      // Quadros pré-renderizados: 1
            new KeyValuePair<uint, uint>(277041154, 0),    // Limitador de FPS do driver: desligado
            new KeyValuePair<uint, uint>(6600001, 0),      // Taxa de atualização preferida
            new KeyValuePair<uint, uint>(13510289, 20),    // Filtragem de textura: qualidade
            new KeyValuePair<uint, uint>(15151633, 1),     // Otimização de amostra anisotrópica
            new KeyValuePair<uint, uint>(3066610, 0),      // Otimização trilinear
            new KeyValuePair<uint, uint>(1686376, 1),      // LOD bias negativo
            new KeyValuePair<uint, uint>(276089202, 0),    // FXAA
            new KeyValuePair<uint, uint>(549528094, 0),    // Otimização com threads
        };

        private static string FilePath => Path.Combine(Store.Folder, "nvidia.json");

        private static Dictionary<string, NvGameEntry> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonSerializer.Deserialize<Dictionary<string, NvGameEntry>>(File.ReadAllText(FilePath));
                    if (d != null) return new Dictionary<string, NvGameEntry>(d, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // arquivo corrompido: começa vazio
            }
            return new Dictionary<string, NvGameEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private static void Save(Dictionary<string, NvGameEntry> d)
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(d, Json));
        }

        public static bool Has(string exe) => Load().ContainsKey(exe);

        public static List<string> Applied() => new List<string>(Load().Keys);

        /// <summary>Grava o perfil para um jogo. Devolve null se deu certo, ou a mensagem de erro.</summary>
        public static string? Apply(string exe, string gameName)
        {
            try
            {
                using (NvDrs? drs = NvDrs.TryOpen(out string? openErr))
                {
                    if (drs == null) return openErr ?? Loc.T("Não encontrei o driver da NVIDIA.");

                    Dictionary<string, NvGameEntry> all = Load();
                    bool known = all.TryGetValue(exe, out NvGameEntry? entry);
                    if (entry == null) entry = new NvGameEntry();

                    IntPtr h = drs.FindProfileOfApplication(exe);
                    if (h == IntPtr.Zero)
                    {
                        string name = "GBooster - " + gameName;
                        h = drs.FindProfileByName(name);
                        if (h == IntPtr.Zero) h = drs.CreateProfile(name);
                        drs.AddApplication(h, exe);
                        if (!known)
                        {
                            entry.ProfileName = name;
                            entry.Created = true;
                        }
                    }
                    else if (!known)
                    {
                        entry.Created = false;
                    }

                    if (!known)
                    {
                        foreach (KeyValuePair<uint, uint> s in Settings)
                        {
                            uint? cur = drs.GetDwordInProfile(h, s.Key);
                            entry.Prior[s.Key.ToString(CultureInfo.InvariantCulture)] = cur.HasValue ? (long)cur.Value : -1L;
                        }
                        // Guarda antes de mexer: se algo falhar no meio, o Desfazer ainda sabe o que devolver.
                        all[exe] = entry;
                        Save(all);
                    }

                    foreach (KeyValuePair<uint, uint> s in Settings)
                    {
                        drs.SetDword(h, s.Key, s.Value);
                    }
                    drs.Save();
                    return null;
                }
            }
            catch (Exception ex)
            {
                return Loc.F("NVIDIA ({0}): {1}", exe, ex.Message);
            }
        }

        /// <summary>Desfaz o perfil de um jogo. Devolve null se deu certo, ou a mensagem de erro.</summary>
        public static string? Undo(string exe)
        {
            try
            {
                Dictionary<string, NvGameEntry> all = Load();
                if (!all.TryGetValue(exe, out NvGameEntry? entry)) return null;

                using (NvDrs? drs = NvDrs.TryOpen(out string? openErr))
                {
                    if (drs == null) return openErr ?? Loc.T("Não encontrei o driver da NVIDIA.");

                    if (entry.Created)
                    {
                        IntPtr h = drs.FindProfileByName(entry.ProfileName);
                        if (h != IntPtr.Zero) drs.DeleteProfile(h);
                    }
                    else
                    {
                        IntPtr h = drs.FindProfileOfApplication(exe);
                        if (h != IntPtr.Zero)
                        {
                            foreach (KeyValuePair<uint, uint> s in Settings)
                            {
                                string key = s.Key.ToString(CultureInfo.InvariantCulture);
                                if (!entry.Prior.TryGetValue(key, out long prior)) continue;
                                if (prior < 0) drs.DeleteSetting(h, s.Key);
                                else drs.SetDword(h, s.Key, (uint)prior);
                            }
                        }
                    }
                    drs.Save();
                }

                all.Remove(exe);
                Save(all);
                return null;
            }
            catch (Exception ex)
            {
                return Loc.F("NVIDIA ({0}): {1}", exe, ex.Message);
            }
        }
    }
}
