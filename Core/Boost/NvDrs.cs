using System;
using System.Runtime.InteropServices;
using System.Text;

// Acesso mínimo ao "Driver Settings" (DRS) da NVIDIA, o mesmo mecanismo que o Painel de Controle da NVIDIA
// e o NVIDIA Profile Inspector usam para gravar perfis por jogo.
// Os identificadores das funções e o formato das estruturas foram adaptados do NVIDIA Profile Inspector
// (Orbmu2k, licença MIT). Veja THIRD-PARTY-NOTICES.md.
namespace GBooster.Core.Boost
{
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode, Size = 4100)]
    internal struct NVDRS_SETTING_UNION
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4100)]
        public byte[] rawData;

        public uint dwordValue
        {
            get { return rawData == null ? 0u : BitConverter.ToUInt32(rawData, 0); }
            set
            {
                rawData = new byte[4100];
                Buffer.BlockCopy(BitConverter.GetBytes(value), 0, rawData, 0, 4);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    internal struct NVDRS_SETTING
    {
        public uint version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
        public string settingName;
        public uint settingId;
        public int settingType;
        public int settingLocation;
        public uint isCurrentPredefined;
        public uint isPredefinedValid;
        public NVDRS_SETTING_UNION predefinedValue;
        public NVDRS_SETTING_UNION currentValue;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    internal struct NVDRS_APPLICATION_V4
    {
        public uint version;
        public uint isPredefined;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
        public string appName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
        public string userFriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
        public string launcher;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
        public string fileInFolder;
        public uint bitvector1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
        public string commandLine;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    internal struct NVDRS_PROFILE
    {
        public uint version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
        public string profileName;
        public uint gpuSupport;
        public uint isPredefined;
        public uint numOfApps;
        public uint numOfSettings;
    }

    /// <summary>Uma sessão aberta no driver da NVIDIA. Use dentro de um <c>using</c>.</summary>
    internal sealed class NvDrs : IDisposable
    {
        public const int Ok = 0;
        public const int SettingNotFound = -160;
        public const int ProfileNotFound = -163;
        public const int ExecutableNotFound = -166;
        public const int ExecutableAlreadyInUse = -167;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr QueryInterfaceDelegate(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateSessionDelegate(ref IntPtr phSession);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionDelegate(IntPtr hSession);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateProfileDelegate(IntPtr hSession, ref NVDRS_PROFILE profile, ref IntPtr phProfile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProfileDelegate(IntPtr hSession, IntPtr hProfile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindProfileByNameDelegate(IntPtr hSession, [MarshalAs(UnmanagedType.LPWStr)] string name, ref IntPtr phProfile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateApplicationDelegate(IntPtr hSession, IntPtr hProfile, ref NVDRS_APPLICATION_V4 app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindApplicationByNameDelegate(IntPtr hSession, [MarshalAs(UnmanagedType.LPWStr)] string appName, ref IntPtr phProfile, ref NVDRS_APPLICATION_V4 app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSettingDelegate(IntPtr hSession, IntPtr hProfile, ref NVDRS_SETTING setting, uint x, uint y);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSettingDelegate(IntPtr hSession, IntPtr hProfile, uint settingId, ref NVDRS_SETTING setting, ref uint x);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteProfileSettingDelegate(IntPtr hSession, IntPtr hProfile, uint settingId);

        private static readonly uint ProfileVer = MakeVersion(Marshal.SizeOf<NVDRS_PROFILE>(), 1);
        private static readonly uint SettingVer = MakeVersion(Marshal.SizeOf<NVDRS_SETTING>(), 1);
        private static readonly uint ApplicationVer = MakeVersion(Marshal.SizeOf<NVDRS_APPLICATION_V4>(), 4);

        private static uint MakeVersion(int size, int version) => (uint)size | ((uint)version << 16);

        private readonly QueryInterfaceDelegate _query;
        private readonly SessionDelegate _destroy;
        private readonly SessionDelegate _load;
        private readonly SessionDelegate _save;
        private readonly CreateProfileDelegate _createProfile;
        private readonly ProfileDelegate _deleteProfile;
        private readonly FindProfileByNameDelegate _findProfile;
        private readonly CreateApplicationDelegate _createApp;
        private readonly FindApplicationByNameDelegate _findApp;
        private readonly SetSettingDelegate _setSetting;
        private readonly GetSettingDelegate _getSetting;
        private readonly DeleteProfileSettingDelegate _deleteSetting;
        private IntPtr _session;
        private bool _open;

        private NvDrs(QueryInterfaceDelegate query)
        {
            _query = query;
            var create = Get<CreateSessionDelegate>(0x0694D52E);
            _destroy = Get<SessionDelegate>(0xDAD9CFF8);
            _load = Get<SessionDelegate>(0x375DBD6B);
            _save = Get<SessionDelegate>(0xFCBC7E14);
            _createProfile = Get<CreateProfileDelegate>(0xCC176068);
            _deleteProfile = Get<ProfileDelegate>(0x17093206);
            _findProfile = Get<FindProfileByNameDelegate>(0x7E4A9A0B);
            _createApp = Get<CreateApplicationDelegate>(0x4347A9DE);
            _findApp = Get<FindApplicationByNameDelegate>(0xEEE566B2);
            _setSetting = Get<SetSettingDelegate>(0x8A2CF5F5, 0x577DD202);
            _getSetting = Get<GetSettingDelegate>(0xEA99498D, 0x73BF8338);
            _deleteSetting = Get<DeleteProfileSettingDelegate>(0xD20D29DF, 0xE4A26362);

            IntPtr s = IntPtr.Zero;
            int r = create(ref s);
            if (r != Ok) throw new InvalidOperationException("DRS_CreateSession " + r);
            _session = s;
            _open = true;
            r = _load(_session);
            if (r != Ok) throw new InvalidOperationException("DRS_LoadSettings " + r);
        }

        private T Get<T>(uint id, uint? fallback = null) where T : class
        {
            IntPtr p = _query(id);
            if (p == IntPtr.Zero && fallback.HasValue) p = _query(fallback.Value);
            if (p == IntPtr.Zero) throw new InvalidOperationException("NVAPI " + id.ToString("X"));
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T ?? throw new InvalidOperationException("NVAPI");
        }

        /// <summary>Abre a sessão. Devolve null se o PC não tem driver NVIDIA (ou ele não responde).</summary>
        public static NvDrs? TryOpen(out string? error)
        {
            error = null;
            try
            {
                if (!NativeLibrary.TryLoad("nvapi64.dll", out IntPtr lib) || lib == IntPtr.Zero)
                {
                    error = Loc.T("Não encontrei o driver da NVIDIA.");
                    return null;
                }
                if (!NativeLibrary.TryGetExport(lib, "nvapi_QueryInterface", out IntPtr q) || q == IntPtr.Zero)
                {
                    error = Loc.T("O driver da NVIDIA não respondeu.");
                    return null;
                }
                var query = Marshal.GetDelegateForFunctionPointer(q, typeof(QueryInterfaceDelegate)) as QueryInterfaceDelegate;
                if (query == null)
                {
                    error = Loc.T("O driver da NVIDIA não respondeu.");
                    return null;
                }
                IntPtr init = query(0x0150E828);
                if (init == IntPtr.Zero)
                {
                    error = Loc.T("O driver da NVIDIA não respondeu.");
                    return null;
                }
                var initialize = Marshal.GetDelegateForFunctionPointer(init, typeof(InitializeDelegate)) as InitializeDelegate;
                if (initialize == null || initialize() != Ok)
                {
                    error = Loc.T("O driver da NVIDIA não respondeu.");
                    return null;
                }
                return new NvDrs(query);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public void Save()
        {
            int r = _save(_session);
            if (r != Ok) throw new InvalidOperationException("DRS_SaveSettings " + r);
        }

        /// <summary>Perfil que contém este executável (pode ser um perfil da própria NVIDIA). Zero se nenhum.</summary>
        public IntPtr FindProfileOfApplication(string exeName)
        {
            IntPtr h = IntPtr.Zero;
            var app = new NVDRS_APPLICATION_V4 { version = ApplicationVer };
            int r = _findApp(_session, exeName, ref h, ref app);
            if (r == Ok) return h;
            if (r == ExecutableNotFound) return IntPtr.Zero;
            throw new InvalidOperationException("DRS_FindApplicationByName " + r);
        }

        public IntPtr FindProfileByName(string name)
        {
            IntPtr h = IntPtr.Zero;
            int r = _findProfile(_session, name, ref h);
            if (r == Ok) return h;
            if (r == ProfileNotFound) return IntPtr.Zero;
            throw new InvalidOperationException("DRS_FindProfileByName " + r);
        }

        public IntPtr CreateProfile(string name)
        {
            var p = new NVDRS_PROFILE { version = ProfileVer, profileName = name };
            IntPtr h = IntPtr.Zero;
            int r = _createProfile(_session, ref p, ref h);
            if (r != Ok) throw new InvalidOperationException("DRS_CreateProfile " + r);
            return h;
        }

        public void DeleteProfile(IntPtr hProfile)
        {
            int r = _deleteProfile(_session, hProfile);
            if (r != Ok) throw new InvalidOperationException("DRS_DeleteProfile " + r);
        }

        public void AddApplication(IntPtr hProfile, string exeName)
        {
            var app = new NVDRS_APPLICATION_V4 { version = ApplicationVer, appName = exeName };
            int r = _createApp(_session, hProfile, ref app);
            if (r != Ok && r != ExecutableAlreadyInUse) throw new InvalidOperationException("DRS_CreateApplication " + r);
        }

        public void SetDword(IntPtr hProfile, uint settingId, uint value)
        {
            var s = new NVDRS_SETTING
            {
                version = SettingVer,
                settingId = settingId,
                settingType = 0,        // DWORD
                settingLocation = 0,    // perfil atual
                currentValue = new NVDRS_SETTING_UNION { dwordValue = value },
            };
            int r = _setSetting(_session, hProfile, ref s, 0, 0);
            if (r != Ok) throw new InvalidOperationException("DRS_SetSetting " + settingId + " " + r);
        }

        /// <summary>Valor gravado neste perfil. Null se o perfil não tem esse item (o driver usa o padrão).</summary>
        public uint? GetDwordInProfile(IntPtr hProfile, uint settingId)
        {
            var s = new NVDRS_SETTING { version = SettingVer };
            uint x = 0;
            int r = _getSetting(_session, hProfile, settingId, ref s, ref x);
            if (r == SettingNotFound) return null;
            if (r != Ok) throw new InvalidOperationException("DRS_GetSetting " + settingId + " " + r);
            if (s.settingLocation != 0) return null;
            return s.currentValue.dwordValue;
        }

        public void DeleteSetting(IntPtr hProfile, uint settingId)
        {
            int r = _deleteSetting(_session, hProfile, settingId);
            if (r != Ok && r != SettingNotFound) throw new InvalidOperationException("DRS_DeleteProfileSetting " + settingId + " " + r);
        }

        public void Dispose()
        {
            if (_open)
            {
                _open = false;
                try { _destroy(_session); } catch { /* sessão já encerrada */ }
            }
        }
    }
}
