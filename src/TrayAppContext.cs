using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PCOvladac
{
    public class TrayAppContext : ApplicationContext
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusMenuItem;
        private readonly object _syncRoot = new object();
        private readonly List<CommandLogEntry> _logEntries = new List<CommandLogEntry>();

        private SettingsForm _settingsForm;
        private Icon _currentIcon;

        public AppConfig Config { get; private set; }
        public HttpServer HttpServer { get; private set; }
        public MdnsService MdnsService { get; private set; }

        public TrayAppContext(bool startMinimized)
        {
            Config = AppConfig.Load();
            HttpServer = new HttpServer(DispatchCommand, GetCommandsSnapshot);
            MdnsService = new MdnsService();

            _menu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Otevřít nastavení");
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            openItem.Click += (s, e) => ShowSettings();

            _statusMenuItem = new ToolStripMenuItem("Stav serveru");
            _statusMenuItem.Click += (s, e) => ShowServerStatus();

            var exitItem = new ToolStripMenuItem("Ukončit");
            exitItem.Click += (s, e) => ExitApplication();

            _menu.Items.Add(openItem);
            _menu.Items.Add(_statusMenuItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                ContextMenuStrip = _menu,
                Visible = true,
                Text = "PC-Ovladac"
            };
            _trayIcon.DoubleClick += (s, e) => ShowSettings();

            StartServices();
            CheckFirstRunFirewall();

            if (!startMinimized)
            {
                _trayIcon.ShowBalloonTip(
                    2500,
                    "PC-Ovladac běží",
                    "Naslouchá na " + NetworkHelper.GetPrimaryLocalIp() + ":" + Config.Port,
                    ToolTipIcon.Info);
            }
        }

        public List<CommandMapping> GetCommandsSnapshot()
        {
            lock (_syncRoot)
            {
                var copy = new List<CommandMapping>();
                foreach (var c in Config.Commands)
                {
                    copy.Add(new CommandMapping(c.Name, c.ActionType, c.Parameter));
                }
                return copy;
            }
        }

        public List<CommandLogEntry> GetLogSnapshot()
        {
            lock (_syncRoot)
            {
                return new List<CommandLogEntry>(_logEntries);
            }
        }

        public void SaveCommands(List<CommandMapping> updated)
        {
            lock (_syncRoot)
            {
                Config.Commands = updated ?? new List<CommandMapping>();
                Config.Save();
            }
        }

        public ActionExecutionResult DispatchCommand(string cmdName, string senderIp)
        {
            CommandMapping match = null;
            lock (_syncRoot)
            {
                foreach (var c in Config.Commands)
                {
                    if (string.Equals(c.Name, cmdName, StringComparison.OrdinalIgnoreCase))
                    {
                        match = new CommandMapping(c.Name, c.ActionType, c.Parameter);
                        break;
                    }
                }
            }

            ActionExecutionResult result = match != null
                ? ActionExecutor.Execute(match)
                : new ActionExecutionResult { Success = false, Message = "Neznámý příkaz: " + cmdName };

            var entry = new CommandLogEntry
            {
                Timestamp = DateTime.Now,
                SenderIp = senderIp,
                Command = cmdName,
                Success = result.Success,
                ResultMessage = result.Message
            };

            lock (_syncRoot)
            {
                _logEntries.Insert(0, entry);
                if (_logEntries.Count > 200)
                {
                    _logEntries.RemoveAt(_logEntries.Count - 1);
                }
            }

            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.AppendLogEntry(entry);
            }

            return result;
        }

        private void StartServices()
        {
            string err;
            bool ok = HttpServer.Start(Config.Port, out err);
            MdnsService.Start(Config.Port);
            UpdateTrayState(ok);

            if (!ok)
            {
                MessageBox.Show(
                    "Nepodařilo se spustit HTTP server na portu " + Config.Port + ":\n" + err +
                    "\n\nZkuste v nastavení zvolit jiný port.",
                    "PC-Ovladac",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        public void RestartServer(int newPort)
        {
            Config.Port = newPort;
            Config.Save();
            StartServices();
        }

        private void UpdateTrayState(bool isRunning)
        {
            Icon oldIcon = _currentIcon;
            _currentIcon = CreateTrayIcon(isRunning);
            _trayIcon.Icon = _currentIcon;
            if (oldIcon != null)
            {
                oldIcon.Dispose();
            }

            string ip = NetworkHelper.GetPrimaryLocalIp();
            string trayText = isRunning
                ? "PC-Ovladac (" + ip + ":" + Config.Port + ")"
                : "PC-Ovladac (server zastaven)";
            if (trayText.Length > 63) trayText = trayText.Substring(0, 63);
            _trayIcon.Text = trayText;

            _statusMenuItem.Text = isRunning
                ? "Stav serveru: Běží (port " + Config.Port + ")"
                : "Stav serveru: Zastaven";
        }

        private void CheckFirstRunFirewall()
        {
            if (Config.FirewallPrompted) return;
            Config.FirewallPrompted = true;
            Config.Save();

            if (!NetworkHelper.IsFirewallRulePresent(Config.Port))
            {
                DialogResult res = MessageBox.Show(
                    "Pro příjem příkazů z mobilního telefonu přes domácí Wi-Fi je potřeba povolit port " +
                    Config.Port + " ve Windows Firewallu.\n\nChcete výjimku přidat automaticky nyní?",
                    "PC-Ovladac — Windows Firewall",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (res == DialogResult.Yes)
                {
                    string err;
                    if (!NetworkHelper.AddFirewallRule(Config.Port, out err))
                    {
                        MessageBox.Show(
                            "Výjimku se nepodařilo přidat:\n" + err +
                            "\nMůžete ji přidat později v okně nastavení.",
                            "PC-Ovladac",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }
        }

        public void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(this);
            }
            _settingsForm.RefreshState();
            if (!_settingsForm.Visible)
            {
                _settingsForm.Show();
            }
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }
            _settingsForm.Activate();
        }

        private void ShowServerStatus()
        {
            string ip = NetworkHelper.GetPrimaryLocalIp();
            string statusText = HttpServer.IsRunning
                ? string.Format(
                    "HTTP server: Běží na portu {0}\n" +
                    "Lokální adresa: http://{1}:{0}\n" +
                    "mDNS služba: PC-Ovladac (http://PC-Ovladac.local:{0})\n" +
                    "Firewall: {2}",
                    Config.Port,
                    ip,
                    NetworkHelper.IsFirewallRulePresent(Config.Port) ? "Povoleno" : "Výjimka nenalezena")
                : "HTTP server je zastaven.\nChyba: " + (HttpServer.LastError ?? "neznámá");

            MessageBox.Show(statusText, "Stav serveru — PC-Ovladac", MessageBoxButtons.OK,
                HttpServer.IsRunning ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void ExitApplication()
        {
            _trayIcon.Visible = false;
            HttpServer.Dispose();
            MdnsService.Dispose();
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Dispose();
            }
            if (_currentIcon != null)
            {
                _currentIcon.Dispose();
            }
            _trayIcon.Dispose();
            Application.Exit();
        }

        private static Icon CreateTrayIcon(bool active)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                Color bg = active ? Color.FromArgb(37, 99, 235) : Color.FromArgb(100, 116, 139);
                using (var brush = new SolidBrush(bg))
                {
                    g.FillEllipse(brush, 2, 2, 28, 28);
                }

                using (var pen = new Pen(Color.White, 2.2f))
                {
                    g.DrawArc(pen, 8, 8, 16, 16, 210, 120);
                    g.DrawArc(pen, 11, 11, 10, 10, 210, 120);
                }

                Color dot = active ? Color.FromArgb(74, 222, 128) : Color.FromArgb(248, 113, 113);
                using (var dotBrush = new SolidBrush(dot))
                {
                    g.FillEllipse(dotBrush, 13, 18, 6, 6);
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
    }
}
