using System;
using Microsoft.Win32;

namespace GBooster.Core
{
    /// <summary>"Iniciar com o Windows": uma entrada na chave Run do usuário (não precisa de administrador).</summary>
    public static class AutoStart
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "GBooster";

        public static void Set(bool enabled)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, true))
                {
                    if (key == null) return;
                    key.DeleteValue("GhenoBoost", false);   // entrada do nome antigo, se existir
                    if (enabled)
                    {
                        string exe = Environment.ProcessPath ?? "";
                        key.SetValue(ValueName, "\"" + exe + "\" --tray");
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch { /* sem permissão: ignora */ }
        }
    }
}
