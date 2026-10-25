using System.Collections.Generic;
using System.Windows.Input;

using GBooster.Core;

namespace GBooster.UI
{
    /// <summary>Transforma o atalho guardado (números do Windows) em texto, ex.: "Ctrl + Alt + R".</summary>
    public static class HotkeyFormatter
    {
        public static string Format(uint modifiers, uint virtualKey)
        {
            if (virtualKey == 0)
            {
                return Loc.T("Desativado");
            }

            var parts = new List<string>();
            if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
            if ((modifiers & 0x0001) != 0) parts.Add("Alt");
            if ((modifiers & 0x0004) != 0) parts.Add("Shift");
            if ((modifiers & 0x0008) != 0) parts.Add("Win");

            string key = KeyInterop.KeyFromVirtualKey((int)virtualKey).ToString();
            if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) key = key.Substring(1); // D5 -> 5
            if (key.StartsWith("NumPad")) key = "Num " + key.Substring(6);
            parts.Add(key);
            return string.Join(" + ", parts);
        }
    }
}
