using System;
using System.Collections.Generic;

namespace GBooster.Core
{
    /// <summary>
    /// Descobre quais programas estão abertos lendo a lista de processos do Windows
    /// (a mesma que o Gerenciador de Tarefas mostra). Não abre nenhum processo.
    /// </summary>
    public static class ProcessScanner
    {
        /// <summary>Nome do executável (minúsculo) -> id do processo.</summary>
        public static Dictionary<string, uint> Snapshot(ICollection<string>? wanted = null)
        {
            // Só guarda os programas que interessam (os jogos cadastrados): menos memória a cada verificação.
            var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            IntPtr snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
            if (snap == NativeMethods.INVALID_HANDLE_VALUE || snap == IntPtr.Zero)
            {
                return result;
            }

            try
            {
                var entry = new NativeMethods.PROCESSENTRY32W();
                entry.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.PROCESSENTRY32W));

                bool ok = NativeMethods.Process32FirstW(snap, ref entry);
                while (ok)
                {
                    if (!string.IsNullOrEmpty(entry.szExeFile)
                        && (wanted == null || wanted.Contains(entry.szExeFile))
                        && !result.ContainsKey(entry.szExeFile))
                    {
                        result[entry.szExeFile] = entry.th32ProcessID;
                    }
                    ok = NativeMethods.Process32NextW(snap, ref entry);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(snap);
            }

            return result;
        }
    }
}
