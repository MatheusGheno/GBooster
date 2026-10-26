using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace GBooster.Core.Boost
{
    /// <summary>
    /// Liga o modo MSI (interrupções por mensagem) em dispositivos PCI, como a placa de vídeo e os controladores USB.
    /// É só um valor do registro (MSISupported); cada valor de antes é guardado para o Desfazer.
    /// </summary>
    internal static class MsiMode
    {
        private const string PciRoot = @"SYSTEM\CurrentControlSet\Enum\PCI";
        private const string Sub = @"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
        private const string ValueName = "MSISupported";

        /// <summary>Caminhos (dentro de HKLM) dos dispositivos PCI da classe pedida (Display, USB…), opcionalmente de um fabricante (10DE = NVIDIA).</summary>
        public static List<string> Find(string deviceClass, string? vendorId)
        {
            var list = new List<string>();
            try
            {
                using (RegistryKey? pci = Registry.LocalMachine.OpenSubKey(PciRoot, false))
                {
                    if (pci == null) return list;
                    foreach (string dev in pci.GetSubKeyNames())
                    {
                        if (vendorId != null && dev.IndexOf("VEN_" + vendorId, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        using (RegistryKey? dk = pci.OpenSubKey(dev, false))
                        {
                            if (dk == null) continue;
                            foreach (string inst in dk.GetSubKeyNames())
                            {
                                using (RegistryKey? ik = dk.OpenSubKey(inst, false))
                                {
                                    if (ik == null) continue;
                                    string cls = ik.GetValue("Class") as string ?? "";
                                    if (string.Equals(cls, deviceClass, StringComparison.OrdinalIgnoreCase))
                                    {
                                        list.Add(PciRoot + "\\" + dev + "\\" + inst);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // sem permissão de leitura: lista vazia
            }
            return list;
        }

        private static int? Read(string devicePath)
        {
            try
            {
                using (RegistryKey? k = Registry.LocalMachine.OpenSubKey(devicePath + "\\" + Sub, false))
                {
                    object? v = k?.GetValue(ValueName, null);
                    return v is int i ? i : (int?)null;
                }
            }
            catch
            {
                return null;
            }
        }

        public static bool AllOn(List<string> devices)
        {
            foreach (string d in devices)
            {
                if (Read(d) != 1) return false;
            }
            return true;
        }

        /// <summary>Guarda o valor de antes (só na primeira vez) e liga o MSI. Devolve null se deu certo, ou os erros.</summary>
        public static string? Apply(List<string> devices, Dictionary<string, string> bk, string prefix)
        {
            var errors = new List<string>();
            foreach (string d in devices)
            {
                string key = prefix + d;
                if (!bk.ContainsKey(key))
                {
                    int? cur = Read(d);
                    bk[key] = cur.HasValue ? cur.Value.ToString(CultureInfo.InvariantCulture) : "absent";
                }
                try
                {
                    using (RegistryKey? k = Registry.LocalMachine.CreateSubKey(d + "\\" + Sub, true))
                    {
                        if (k == null) throw new InvalidOperationException(Loc.T("não consegui abrir a chave"));
                        k.SetValue(ValueName, 1, RegistryValueKind.DWord);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }
            return errors.Count == 0 ? null : string.Join(" | ", errors);
        }

        public static string? Undo(Dictionary<string, string> bk, string prefix)
        {
            var errors = new List<string>();
            foreach (KeyValuePair<string, string> kv in bk)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string dev = kv.Key.Substring(prefix.Length);
                // O arquivo de backup é editável: só mexe em dispositivos PCI.
                if (!dev.StartsWith(PciRoot + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (kv.Value == "absent")
                    {
                        using (RegistryKey? k = Registry.LocalMachine.OpenSubKey(dev + "\\" + Sub, true))
                        {
                            k?.DeleteValue(ValueName, false);
                        }
                    }
                    else if (int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int prior))
                    {
                        using (RegistryKey? k = Registry.LocalMachine.CreateSubKey(dev + "\\" + Sub, true))
                        {
                            k?.SetValue(ValueName, prior, RegistryValueKind.DWord);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }
            return errors.Count == 0 ? null : string.Join(" | ", errors);
        }
    }
}
