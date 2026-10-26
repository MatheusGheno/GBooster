using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace GBooster.Core.Boost
{
    /// <summary>
    /// O GBooster abre normalmente (trocar a resolução não precisa de administrador).
    /// Só a página Desempenho precisa: ela mexe no Windows. Aqui fica a checagem e o "reiniciar como administrador".
    /// </summary>
    public static class AdminHelper
    {
        public static bool IsAdmin
        {
            get
            {
                try
                {
                    using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    {
                        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Abre outra cópia do app pedindo permissão de administrador (o Windows mostra a janela de confirmação).
        /// Devolve null se a nova cópia abriu, ou a mensagem de erro. Quem chamou deve fechar esta cópia em seguida.
        /// </summary>
        public static string? RelaunchAsAdmin()
        {
            try
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                {
                    return Loc.T("Não consegui descobrir onde o app está instalado.");
                }
                var psi = new ProcessStartInfo(exe, "--elevated") { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
                return null;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return Loc.T("O pedido de administrador foi cancelado.");
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
