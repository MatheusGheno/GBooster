using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace GBooster.Core
{
    public sealed class DisplayMode
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public override string ToString() => Width + " × " + Height;
    }

    /// <summary>
    /// Troca a resolução do monitor com a API oficial do Windows (ChangeDisplaySettingsEx),
    /// a mesma usada pelo QRes. A mudança é temporária (sem CDS_UPDATEREGISTRY): se o
    /// Windows reiniciar, volta sozinho. A taxa de atualização (Hz) é mantida como está.
    /// </summary>
    public static class DisplayService
    {
        private static NativeMethods.DEVMODE NewDevMode()
        {
            var dm = new NativeMethods.DEVMODE();
            dm.dmDeviceName = new string('\0', 32);
            dm.dmFormName = new string('\0', 32);
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(NativeMethods.DEVMODE));
            return dm;
        }

        /// <summary>Monitores ligados ao desktop (ex.: \\.\DISPLAY1).</summary>
        public static List<string> GetAttachedDevices()
        {
            var list = new List<string>();
            for (uint i = 0; ; i++)
            {
                var dd = new NativeMethods.DISPLAY_DEVICE();
                dd.cb = (uint)Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));
                if (!NativeMethods.EnumDisplayDevices(null, i, ref dd, 0))
                {
                    break;
                }
                if ((dd.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                {
                    list.Add(dd.DeviceName);
                }
            }
            return list;
        }

        public static string GetPrimaryDevice()
        {
            for (uint i = 0; ; i++)
            {
                var dd = new NativeMethods.DISPLAY_DEVICE();
                dd.cb = (uint)Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));
                if (!NativeMethods.EnumDisplayDevices(null, i, ref dd, 0))
                {
                    break;
                }
                if ((dd.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0)
                {
                    return dd.DeviceName;
                }
            }
            var all = GetAttachedDevices();
            return all.Count > 0 ? all[0] : "";
        }

        public static MonitorSnapshot? GetCurrent(string device)
        {
            var dm = NewDevMode();
            if (!NativeMethods.EnumDisplaySettingsEx(device, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm, 0))
            {
                return null;
            }
            return new MonitorSnapshot
            {
                Device = device,
                Width = (int)dm.dmPelsWidth,
                Height = (int)dm.dmPelsHeight,
                Hz = (int)dm.dmDisplayFrequency,
                Bpp = (int)dm.dmBitsPerPel,
                X = dm.dmPositionX,
                Y = dm.dmPositionY
            };
        }

        /// <summary>Resoluções que o monitor aceita, da maior para a menor (sem repetir).</summary>
        public static List<DisplayMode> ListModes(string device)
        {
            var seen = new HashSet<long>();
            var modes = new List<DisplayMode>();
            for (int i = 0; ; i++)
            {
                var dm = NewDevMode();
                if (!NativeMethods.EnumDisplaySettingsEx(device, i, ref dm, 0))
                {
                    break;
                }
                if (dm.dmBitsPerPel < 32 || dm.dmPelsWidth < 640)
                {
                    continue;
                }
                long key = ((long)dm.dmPelsWidth << 32) | dm.dmPelsHeight;
                if (seen.Add(key))
                {
                    modes.Add(new DisplayMode { Width = (int)dm.dmPelsWidth, Height = (int)dm.dmPelsHeight });
                }
            }
            return modes.OrderByDescending(m => m.Width).ThenByDescending(m => m.Height).ToList();
        }

        /// <summary>Taxas de atualização (Hz) que o monitor aceita nessa resolução, da maior para a menor.</summary>
        public static List<int> ListRefreshRates(string device, int width, int height)
        {
            var set = new HashSet<int>();
            for (int i = 0; ; i++)
            {
                var dm = NewDevMode();
                if (!NativeMethods.EnumDisplaySettingsEx(device, i, ref dm, 0))
                {
                    break;
                }
                if (dm.dmBitsPerPel >= 32 && dm.dmPelsWidth == (uint)width && dm.dmPelsHeight == (uint)height && dm.dmDisplayFrequency > 1)
                {
                    set.Add((int)dm.dmDisplayFrequency);
                }
            }
            return set.OrderByDescending(x => x).ToList();
        }

        /// <summary>Aplica largura x altura (e Hz, se hz &gt; 0; senão mantém o atual), cores e posição. Devolve texto de erro ou null se deu certo.</summary>
        public static string? Apply(string device, int width, int height, int hz = 0)
        {
            var current = GetCurrent(device);
            if (current == null)
            {
                return Loc.F("Não consegui ler o monitor {0}.", device);
            }
            return Set(current, width, height, hz);
        }

        /// <summary>Volta o monitor para o estado guardado.</summary>
        public static string? Restore(MonitorSnapshot snap)
        {
            return Set(snap, snap.Width, snap.Height);
        }

        private static string? Set(MonitorSnapshot keep, int width, int height, int hz = 0)
        {
            int useHz = hz > 0 ? hz : keep.Hz;
            var dm = NewDevMode();
            dm.dmPelsWidth = (uint)width;
            dm.dmPelsHeight = (uint)height;
            dm.dmDisplayFrequency = (uint)useHz;
            dm.dmBitsPerPel = (uint)keep.Bpp;
            dm.dmPositionX = keep.X;
            dm.dmPositionY = keep.Y;
            dm.dmFields = NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT
                        | NativeMethods.DM_DISPLAYFREQUENCY | NativeMethods.DM_BITSPERPEL
                        | NativeMethods.DM_POSITION;

            // 1) Só testa: o Windows diz se aceitaria, sem mexer na tela.
            int test = NativeMethods.ChangeDisplaySettingsEx(keep.Device, ref dm, IntPtr.Zero, NativeMethods.CDS_TEST, IntPtr.Zero);
            if (test != NativeMethods.DISP_CHANGE_SUCCESSFUL)
            {
                return Loc.F("O monitor não aceita {0} × {1} a {2} Hz (código {3}).", width, height, useHz, test);
            }

            // 2) Aplica (temporário: não grava no registro).
            int res = NativeMethods.ChangeDisplaySettingsEx(keep.Device, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
            return res == NativeMethods.DISP_CHANGE_SUCCESSFUL ? null : Loc.F("O Windows recusou a troca (código {0}).", res);
        }

        /// <summary>
        /// Em qual monitor está a janela principal do processo? Só consulta janelas
        /// (EnumWindows); não abre o processo. Se não achar, usa o monitor principal.
        /// </summary>
        public static string GetMonitorOfProcess(uint pid)
        {
            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumWindows((hWnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint wpid);
                if (wpid == pid && NativeMethods.IsWindowVisible(hWnd))
                {
                    found = hWnd;
                    return false; // achou, para a busca
                }
                return true;
            }, IntPtr.Zero);

            if (found == IntPtr.Zero)
            {
                return GetPrimaryDevice();
            }

            IntPtr mon = NativeMethods.MonitorFromWindow(found, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
            var info = new NativeMethods.MONITORINFOEX();
            info.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
            if (mon != IntPtr.Zero && NativeMethods.GetMonitorInfo(mon, ref info) && !string.IsNullOrEmpty(info.szDevice))
            {
                return info.szDevice;
            }
            return GetPrimaryDevice();
        }
    }
}
