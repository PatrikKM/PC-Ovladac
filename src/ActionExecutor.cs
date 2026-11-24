using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PCOvladac
{
    public class ActionTypeOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public bool UsesParameter { get; set; }
        public string ParameterHint { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class ActionExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public static class ActionExecutor
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint GW_OWNER = 4;
        private const int SW_RESTORE = 9;
        private const int DWMWA_CLOAKED = 14;

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const byte VK_VOLUME_MUTE = 0xAD;
        private const byte VK_VOLUME_DOWN = 0xAE;
        private const byte VK_VOLUME_UP = 0xAF;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

        public static readonly List<ActionTypeOption> SupportedActions = new List<ActionTypeOption>
        {
            new ActionTypeOption { Id = "VolumeUp", DisplayName = "Zvýšit hlasitost", UsesParameter = true, ParameterHint = "Volitelně počet kroků (výchozí 1)" },
            new ActionTypeOption { Id = "VolumeDown", DisplayName = "Snížit hlasitost", UsesParameter = true, ParameterHint = "Volitelně počet kroků (výchozí 1)" },
            new ActionTypeOption { Id = "VolumeMute", DisplayName = "Ztlumit / zapnout zvuk", UsesParameter = false, ParameterHint = "" },
            new ActionTypeOption { Id = "MediaPlayPause", DisplayName = "Média: Přehrát / Pauza", UsesParameter = false, ParameterHint = "" },
            new ActionTypeOption { Id = "MediaNext", DisplayName = "Média: Další skladba", UsesParameter = false, ParameterHint = "" },
            new ActionTypeOption { Id = "MediaPrevious", DisplayName = "Média: Předchozí skladba", UsesParameter = false, ParameterHint = "" },
            new ActionTypeOption { Id = "LockScreen", DisplayName = "Zamknout obrazovku", UsesParameter = false, ParameterHint = "" },
            new ActionTypeOption { Id = "Sleep", DisplayName = "Uspat PC", UsesParameter = false, ParameterHint = "" },
            new ActionTypeOption { Id = "Shutdown", DisplayName = "Vypnout PC", UsesParameter = true, ParameterHint = "Volitelně zpoždění v sekundách (výchozí 5)" },
            new ActionTypeOption { Id = "RunProgram", DisplayName = "Spustit program / skript", UsesParameter = true, ParameterHint = "Např. notepad.exe nebo C:\\cesta\\skript.bat" },
            new ActionTypeOption { Id = "Shortcut", DisplayName = "Klávesová zkratka", UsesParameter = true, ParameterHint = "Např. CTRL+ALT+T, WIN+D, ALT+F4, SPACE" }
        };

        public static string GetDisplayName(string actionId)
        {
            foreach (var opt in SupportedActions)
            {
                if (string.Equals(opt.Id, actionId, StringComparison.OrdinalIgnoreCase))
                {
                    return opt.DisplayName;
                }
            }
            return actionId;
        }

        public static ActionExecutionResult Execute(CommandMapping mapping)
        {
            if (mapping == null)
            {
                return new ActionExecutionResult { Success = false, Message = "Prázdný příkaz." };
            }

            string action = (mapping.ActionType ?? "").Trim();
            string param = (mapping.Parameter ?? "").Trim();

            try
            {
                switch (action)
                {
                    case "VolumeUp":
                    {
                        int steps = ParsePositiveInt(param, 1, 20);
                        for (int i = 0; i < steps; i++) PressVirtualKey(VK_VOLUME_UP, true);
                        return Ok("Hlasitost zvýšena (" + steps + "x).");
                    }
                    case "VolumeDown":
                    {
                        int steps = ParsePositiveInt(param, 1, 20);
                        for (int i = 0; i < steps; i++) PressVirtualKey(VK_VOLUME_DOWN, true);
                        return Ok("Hlasitost snížena (" + steps + "x).");
                    }
                    case "VolumeMute":
                        PressVirtualKey(VK_VOLUME_MUTE, true);
                        return Ok("Zvuk přepnut (ztlumení).");

                    case "MediaPlayPause":
                        PressVirtualKey(VK_MEDIA_PLAY_PAUSE, true);
                        return Ok("Odeslán povel Přehrát/Pauza.");

                    case "MediaNext":
                        PressVirtualKey(VK_MEDIA_NEXT_TRACK, true);
                        return Ok("Odeslán povel Další skladba.");

                    case "MediaPrevious":
                        PressVirtualKey(VK_MEDIA_PREV_TRACK, true);
                        return Ok("Odeslán povel Předchozí skladba.");

                    case "LockScreen":
                        if (LockWorkStation())
                            return Ok("Obrazovka uzamčena.");
                        return Fail("Nepodařilo se uzamknout obrazovku.");

                    case "Sleep":
                        if (SetSuspendState(false, false, false))
                            return Ok("PC přechází do režimu spánku.");
                        return Fail("Nepodařilo se uspat PC.");

                    case "Shutdown":
                    {
                        int delay = ParsePositiveInt(param, 5, 3600);
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown.exe",
                            Arguments = "/s /t " + delay,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        return Ok("Naplánováno vypnutí PC za " + delay + " s.");
                    }

                    case "RunProgram":
                        return ExecuteProgram(param);

                    case "Shortcut":
                        return ExecuteShortcut(param);

                    default:
                        return Fail("Neznámý typ akce: " + action);
                }
            }
            catch (Exception ex)
            {
                return Fail("Chyba při vykonání: " + ex.Message);
            }
        }

        private static ActionExecutionResult ExecuteProgram(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return Fail("Není zadán program ani skript ke spuštění.");
            }

            string fileName;
            string arguments = "";

            if (commandLine.StartsWith("\""))
            {
                int endQuote = commandLine.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    fileName = commandLine.Substring(1, endQuote - 1);
                    arguments = commandLine.Substring(endQuote + 1).Trim();
                }
                else
                {
                    fileName = commandLine.Trim('"');
                }
            }
            else
            {
                int spaceIdx = commandLine.IndexOf(' ');
                if (spaceIdx > 0 && !System.IO.File.Exists(commandLine))
                {
                    fileName = commandLine.Substring(0, spaceIdx);
                    arguments = commandLine.Substring(spaceIdx + 1).Trim();
                }
                else
                {
                    fileName = commandLine;
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            };
            Process.Start(psi);
            return Ok("Spuštěno: " + fileName);
        }

        private static ActionExecutionResult ExecuteShortcut(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut))
            {
                return Fail("Není zadána klávesová zkratka.");
            }

            string normalized = shortcut.Trim().ToUpperInvariant().Replace(" ", "");
            if (normalized == "ALT+TAB")
            {
                return SwitchToNextWindow();
            }

            string[] tokens = shortcut.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            var vks = new List<byte>();

            foreach (string rawToken in tokens)
            {
                byte vk;
                if (!TryParseVirtualKey(rawToken.Trim(), out vk))
                {
                    return Fail("Neznámá klávesa ve zkratce: " + rawToken.Trim());
                }
                vks.Add(vk);
            }

            if (vks.Count == 0)
            {
                return Fail("Neplatná klávesová zkratka.");
            }

            for (int i = 0; i < vks.Count; i++)
            {
                uint flags = IsExtendedKey(vks[i]) ? KEYEVENTF_EXTENDEDKEY : 0;
                keybd_event(vks[i], 0, flags, UIntPtr.Zero);
                System.Threading.Thread.Sleep(35);
            }

            for (int i = vks.Count - 1; i >= 0; i--)
            {
                uint flags = KEYEVENTF_KEYUP | (IsExtendedKey(vks[i]) ? KEYEVENTF_EXTENDEDKEY : 0);
                keybd_event(vks[i], 0, flags, UIntPtr.Zero);
                System.Threading.Thread.Sleep(25);
            }

            return Ok("Odeslána zkratka: " + shortcut.ToUpperInvariant());
        }

        private static ActionExecutionResult SwitchToNextWindow()
        {
            var windows = new List<IntPtr>();
            uint ownPid = (uint)Process.GetCurrentProcess().Id;

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                if (GetWindowTextLength(hWnd) == 0) return true;
                if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;

                int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;
                if ((exStyle & WS_EX_NOACTIVATE) != 0) return true;

                int cloaked = 0;
                if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == ownPid) return true;

                windows.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            if (windows.Count == 0)
            {
                return Fail("Nebylo nalezeno žádné další okno k přepnutí.");
            }

            // Stabilní pořadí podle handle, aby opakované volání procházelo všechna otevřená okna dokola
            windows.Sort((a, b) => a.ToInt64().CompareTo(b.ToInt64()));

            IntPtr fg = GetForegroundWindow();
            int currentIdx = windows.IndexOf(fg);
            IntPtr target = (currentIdx >= 0)
                ? windows[(currentIdx + 1) % windows.Count]
                : windows[0];

            if (IsIconic(target))
            {
                ShowWindow(target, SW_RESTORE);
            }

            uint fgPid;
            uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out fgPid) : 0;
            uint curThread = GetCurrentThreadId();
            bool attached = false;

            try
            {
                if (fgThread != 0 && fgThread != curThread)
                {
                    attached = AttachThreadInput(curThread, fgThread, true);
                }
                // Krátký stisk ALT odemkne ForegroundLock na Windows
                keybd_event(0x12, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(0x12, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                SetForegroundWindow(target);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(curThread, fgThread, false);
                }
            }

            return Ok("Přepnuto na další okno.");
        }

        private static bool TryParseVirtualKey(string token, out byte vk)
        {
            vk = 0;
            string upper = token.ToUpperInvariant();

            switch (upper)
            {
                case "CTRL":
                case "CONTROL": vk = 0x11; return true;
                case "ALT":
                case "MENU": vk = 0x12; return true;
                case "SHIFT": vk = 0x10; return true;
                case "WIN":
                case "LWIN": vk = 0x5B; return true;
                case "ENTER":
                case "RETURN": vk = 0x0D; return true;
                case "ESC":
                case "ESCAPE": vk = 0x1B; return true;
                case "SPACE":
                case "MEZERNIK": vk = 0x20; return true;
                case "TAB": vk = 0x09; return true;
                case "BACK":
                case "BACKSPACE": vk = 0x08; return true;
                case "DEL":
                case "DELETE": vk = 0x2E; return true;
                case "INS":
                case "INSERT": vk = 0x2D; return true;
                case "HOME": vk = 0x24; return true;
                case "END": vk = 0x23; return true;
                case "PGUP":
                case "PAGEUP": vk = 0x21; return true;
                case "PGDN":
                case "PAGEDOWN": vk = 0x22; return true;
                case "LEFT": vk = 0x25; return true;
                case "UP": vk = 0x26; return true;
                case "RIGHT": vk = 0x27; return true;
                case "DOWN": vk = 0x28; return true;
                case "PRTSC":
                case "PRINTSCREEN": vk = 0x2C; return true;
            }

            if (upper.Length == 1)
            {
                char ch = upper[0];
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                {
                    vk = (byte)ch;
                    return true;
                }
            }

            if (upper.StartsWith("F"))
            {
                int fNum;
                if (int.TryParse(upper.Substring(1), out fNum) && fNum >= 1 && fNum <= 24)
                {
                    vk = (byte)(0x70 + (fNum - 1));
                    return true;
                }
            }

            return false;
        }

        private static bool IsExtendedKey(byte vk)
        {
            return vk == 0x5B || vk == 0x21 || vk == 0x22 || vk == 0x23 || vk == 0x24 ||
                   vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 ||
                   vk == 0x2D || vk == 0x2E ||
                   (vk >= 0xAD && vk <= 0xB3);
        }

        private static void PressVirtualKey(byte vk, bool extended)
        {
            uint downFlags = extended ? KEYEVENTF_EXTENDEDKEY : 0;
            uint upFlags = KEYEVENTF_KEYUP | downFlags;
            keybd_event(vk, 0, downFlags, UIntPtr.Zero);
            keybd_event(vk, 0, upFlags, UIntPtr.Zero);
        }

        private static int ParsePositiveInt(string text, int defaultValue, int maxValue)
        {
            int val;
            if (int.TryParse(text, out val) && val > 0)
            {
                return Math.Min(val, maxValue);
            }
            return defaultValue;
        }

        private static ActionExecutionResult Ok(string msg)
        {
            return new ActionExecutionResult { Success = true, Message = msg };
        }

        private static ActionExecutionResult Fail(string msg)
        {
            return new ActionExecutionResult { Success = false, Message = msg };
        }
    }
}
