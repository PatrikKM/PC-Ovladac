using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace PCOvladac
{
    public class CommandMapping
    {
        public string Name { get; set; }
        public string ActionType { get; set; }
        public string Parameter { get; set; }

        public CommandMapping()
        {
            Name = "";
            ActionType = "";
            Parameter = "";
        }

        public CommandMapping(string name, string actionType, string parameter)
        {
            Name = name;
            ActionType = actionType;
            Parameter = parameter ?? "";
        }
    }

    public class AppConfig
    {
        public int Port { get; set; }
        public bool AutoStart { get; set; }
        public bool FirewallPrompted { get; set; }
        public List<CommandMapping> Commands { get; set; }

        public AppConfig()
        {
            Port = 5000;
            AutoStart = false;
            FirewallPrompted = false;
            Commands = new List<CommandMapping>();
        }

        public static string ConfigFilePath
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(baseDir, "config.json");
            }
        }

        public static List<CommandMapping> CreateDefaultCommands()
        {
            return new List<CommandMapping>
            {
                new CommandMapping("hlasitost_nahoru", "VolumeUp", ""),
                new CommandMapping("hlasitost_dolu", "VolumeDown", ""),
                new CommandMapping("ztlumit", "VolumeMute", ""),
                new CommandMapping("pauza", "MediaPlayPause", ""),
                new CommandMapping("dalsi", "MediaNext", ""),
                new CommandMapping("predchozi", "MediaPrevious", ""),
                new CommandMapping("zamknout", "LockScreen", ""),
                new CommandMapping("f11", "Shortcut", "F11"),
                new CommandMapping("alt_tab", "Shortcut", "ALT+TAB")
            };
        }

        public static AppConfig Load()
        {
            try
            {
                string path = ConfigFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var serializer = new JavaScriptSerializer();
                    var loaded = serializer.Deserialize<AppConfig>(json);
                    if (loaded != null)
                    {
                        if (loaded.Port < 1 || loaded.Port > 65535)
                        {
                            loaded.Port = 5000;
                        }
                        if (loaded.Commands == null || !json.Contains("\"Commands\""))
                        {
                            loaded.Commands = CreateDefaultCommands();
                            loaded.Save();
                        }
                        return loaded;
                    }
                }
            }
            catch
            {
                // Při poškozeném souboru použijeme výchozí hodnoty
            }

            var defaultConfig = new AppConfig();
            defaultConfig.Commands = CreateDefaultCommands();
            defaultConfig.Save();
            return defaultConfig;
        }

        public void Save()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = FormatJson(serializer.Serialize(this));
                File.WriteAllText(ConfigFilePath, json);
            }
            catch
            {
                // Ignorujeme chybu zápisu (např. práva ke složce)
            }
        }

        private static string FormatJson(string json)
        {
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            int indent = 0;

            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    sb.Append(ch);
                }
                else if (!inQuotes)
                {
                    if (ch == '{' || ch == '[')
                    {
                        sb.Append(ch);
                        sb.AppendLine();
                        indent++;
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (ch == '}' || ch == ']')
                    {
                        sb.AppendLine();
                        indent = Math.Max(0, indent - 1);
                        sb.Append(new string(' ', indent * 2));
                        sb.Append(ch);
                    }
                    else if (ch == ',')
                    {
                        sb.Append(ch);
                        sb.AppendLine();
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (ch == ':')
                    {
                        sb.Append(": ");
                    }
                    else if (!char.IsWhiteSpace(ch))
                    {
                        sb.Append(ch);
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }
    }
}
