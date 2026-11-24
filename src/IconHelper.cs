using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;

namespace PCOvladac
{
    public static class IconHelper
    {
        public static string ExtractProgramIconBase64(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return "";

            string target = ExtractExecutableToken(commandLine.Trim());
            if (string.IsNullOrWhiteSpace(target)) return "";

            string resolved = ResolveExecutablePath(target);
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved)) return "";

            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(resolved))
                {
                    if (icon == null) return "";
                    using (Bitmap src = icon.ToBitmap())
                    using (Bitmap resized = new Bitmap(64, 64))
                    using (Graphics g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        g.DrawImage(src, 0, 0, 64, 64);

                        using (var ms = new MemoryStream())
                        {
                            resized.Save(ms, ImageFormat.Png);
                            return Convert.ToBase64String(ms.ToArray());
                        }
                    }
                }
            }
            catch
            {
                return "";
            }
        }

        private static string ExtractExecutableToken(string commandLine)
        {
            if (commandLine.StartsWith("\""))
            {
                int endQuote = commandLine.IndexOf('"', 1);
                if (endQuote > 1) return commandLine.Substring(1, endQuote - 1);
                return commandLine.Trim('"');
            }

            if (File.Exists(commandLine)) return commandLine;

            int spaceIdx = commandLine.IndexOf(' ');
            return spaceIdx > 0 ? commandLine.Substring(0, spaceIdx) : commandLine;
        }

        private static string ResolveExecutablePath(string target)
        {
            if (File.Exists(target)) return target;

            string withExe = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? target
                : target + ".exe";

            if (File.Exists(withExe)) return withExe;

            // 1. Hledání v PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string candidate1 = Path.Combine(dir.Trim(), target);
                    if (File.Exists(candidate1)) return candidate1;
                    string candidate2 = Path.Combine(dir.Trim(), withExe);
                    if (File.Exists(candidate2)) return candidate2;
                }
                catch { }
            }

            // 2. Hledání v App Paths v registru Windows (Chrome, Edge, Word, atd.)
            string regPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + Path.GetFileName(withExe);
            string fromHklm = ReadAppPath(Registry.LocalMachine, regPath);
            if (!string.IsNullOrEmpty(fromHklm)) return fromHklm;

            string fromHkcu = ReadAppPath(Registry.CurrentUser, regPath);
            if (!string.IsNullOrEmpty(fromHkcu)) return fromHkcu;

            return null;
        }

        private static string ReadAppPath(RegistryKey root, string subKey)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(subKey, false))
                {
                    if (key == null) return null;
                    object val = key.GetValue(null);
                    if (val == null) return null;
                    string path = val.ToString().Trim().Trim('"');
                    return File.Exists(path) ? path : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
