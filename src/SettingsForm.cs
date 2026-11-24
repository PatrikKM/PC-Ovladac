using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PCOvladac
{
    public class SettingsForm : Form
    {
        private readonly TrayAppContext _context;

        // Tab 1: Příkazy
        private DataGridView _dgvCommands;
        private List<CommandMapping> _commandsCache = new List<CommandMapping>();

        // Tab 2: Log
        private DataGridView _dgvLog;

        // Tab 3: Síť a nastavení
        private Label _lblServerStatus;
        private Label _lblMdnsStatus;
        private TextBox _txtPrimaryUrl;
        private TextBox _txtMdnsUrl;
        private ListBox _lstAddresses;
        private NumericUpDown _numPort;
        private CheckBox _chkAutoStart;
        private Label _lblFirewallStatus;
        private Button _btnFirewall;
        private bool _isUpdatingUi;

        public SettingsForm(TrayAppContext context)
        {
            _context = context;
            InitializeUi();
            RefreshState();
        }

        private void InitializeUi()
        {
            Text = "PC-Ovladac — Nastavení a správa příkazů";
            Size = new Size(740, 560);
            MinimumSize = new Size(660, 480);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.FromArgb(248, 250, 252);

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(12, 6)
            };
            Controls.Add(tabs);

            var tabCommands = new TabPage("Příkazy a akce") { BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(12) };
            var tabLog = new TabPage("Log příkazů") { BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(12) };
            var tabNetwork = new TabPage("Síť a nastavení") { BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(12) };

            tabs.TabPages.Add(tabCommands);
            tabs.TabPages.Add(tabLog);
            tabs.TabPages.Add(tabNetwork);

            BuildCommandsTab(tabCommands);
            BuildLogTab(tabLog);
            BuildNetworkTab(tabNetwork);

            FormClosing += SettingsForm_FormClosing;
        }

        private void BuildCommandsTab(TabPage tab)
        {
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 36 };
            var lblInfo = new Label
            {
                Text = "Mapování příkazů přijatých z mobilu (/cmd/<nazev>) na akce ve Windows:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(71, 85, 105)
            };
            topPanel.Controls.Add(lblInfo);

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var btnAdd = new Button { Text = "Přidat příkaz", Location = new Point(0, 8), Size = new Size(120, 30) };
            var btnEdit = new Button { Text = "Upravit", Location = new Point(128, 8), Size = new Size(100, 30) };
            var btnDelete = new Button { Text = "Smazat", Location = new Point(236, 8), Size = new Size(100, 30) };

            btnAdd.Click += BtnAddCommand_Click;
            btnEdit.Click += BtnEditCommand_Click;
            btnDelete.Click += BtnDeleteCommand_Click;

            bottomPanel.Controls.Add(btnAdd);
            bottomPanel.Controls.Add(btnEdit);
            bottomPanel.Controls.Add(btnDelete);

            _dgvCommands = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            _dgvCommands.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "Název příkazu",
                FillWeight = 24
            });
            _dgvCommands.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ActionDisplay",
                HeaderText = "Typ akce",
                FillWeight = 30
            });
            _dgvCommands.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Parameter",
                HeaderText = "Parametr",
                FillWeight = 30
            });
            _dgvCommands.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "TestBtn",
                HeaderText = "Test",
                Text = "Otestovat",
                UseColumnTextForButtonValue = true,
                FillWeight = 16
            });

            _dgvCommands.CellClick += DgvCommands_CellClick;
            _dgvCommands.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != 3) EditSelectedCommand(); };

            tab.Controls.Add(_dgvCommands);
            tab.Controls.Add(topPanel);
            tab.Controls.Add(bottomPanel);
        }

        private void BuildLogTab(TabPage tab)
        {
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var btnClearLog = new Button { Text = "Vymazat zobrazení logu", Location = new Point(0, 8), Size = new Size(170, 30) };
            btnClearLog.Click += (s, e) => _dgvLog.Rows.Clear();
            bottomPanel.Controls.Add(btnClearLog);

            _dgvLog = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _dgvLog.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Čas", FillWeight = 16 });
            _dgvLog.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ip", HeaderText = "IP odesílatele", FillWeight = 20 });
            _dgvLog.Columns.Add(new DataGridViewTextBoxColumn { Name = "Command", HeaderText = "Příkaz", FillWeight = 22 });
            _dgvLog.Columns.Add(new DataGridViewTextBoxColumn { Name = "Result", HeaderText = "Výsledek", FillWeight = 42 });

            tab.Controls.Add(_dgvLog);
            tab.Controls.Add(bottomPanel);
        }

        private void BuildNetworkTab(TabPage tab)
        {
            int y = 8;
            int width = 660;

            var grpNetwork = new GroupBox
            {
                Text = "Stav serveru a adresa pro připojení",
                Location = new Point(8, y),
                Size = new Size(width, 195),
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(grpNetwork);

            _lblServerStatus = new Label
            {
                Location = new Point(14, 26),
                Size = new Size(width - 28, 22),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            grpNetwork.Controls.Add(_lblServerStatus);

            _lblMdnsStatus = new Label
            {
                Location = new Point(14, 50),
                Size = new Size(width - 28, 20),
                ForeColor = Color.FromArgb(71, 85, 105)
            };
            grpNetwork.Controls.Add(_lblMdnsStatus);

            var lblUrlTitle = new Label
            {
                Text = "Lokální adresa PC:",
                Location = new Point(14, 78),
                Size = new Size(260, 20)
            };
            grpNetwork.Controls.Add(lblUrlTitle);

            _txtPrimaryUrl = new TextBox
            {
                Location = new Point(14, 98),
                Size = new Size(width - 126, 26),
                ReadOnly = true,
                Font = new Font("Consolas", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(241, 245, 249),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            grpNetwork.Controls.Add(_txtPrimaryUrl);

            var btnCopyUrl = new Button
            {
                Text = "Kopírovat",
                Location = new Point(width - 102, 97),
                Size = new Size(88, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnCopyUrl.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_txtPrimaryUrl.Text))
                    Clipboard.SetText(_txtPrimaryUrl.Text);
            };
            grpNetwork.Controls.Add(btnCopyUrl);

            _txtMdnsUrl = new TextBox
            {
                Location = new Point(14, 130),
                Size = new Size(width - 126, 24),
                ReadOnly = true,
                Font = new Font("Consolas", 9f),
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(71, 85, 105),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            grpNetwork.Controls.Add(_txtMdnsUrl);

            var btnRefreshIp = new Button
            {
                Text = "Obnovit IP",
                Location = new Point(width - 102, 129),
                Size = new Size(88, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnRefreshIp.Click += (s, e) => RefreshNetworkAddresses();
            grpNetwork.Controls.Add(btnRefreshIp);

            _lstAddresses = new ListBox
            {
                Location = new Point(14, 160),
                Size = new Size(width - 28, 26),
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(100, 116, 139),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            grpNetwork.Controls.Add(_lstAddresses);

            y += grpNetwork.Height + 12;

            var grpSettings = new GroupBox
            {
                Text = "Nastavení aplikace",
                Location = new Point(8, y),
                Size = new Size(width, 145),
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(grpSettings);

            var lblPort = new Label
            {
                Text = "Port HTTP serveru:",
                Location = new Point(14, 30),
                Size = new Size(135, 22)
            };
            grpSettings.Controls.Add(lblPort);

            _numPort = new NumericUpDown
            {
                Location = new Point(155, 27),
                Size = new Size(85, 25),
                Minimum = 1,
                Maximum = 65535,
                Value = 5000
            };
            grpSettings.Controls.Add(_numPort);

            var btnApplyPort = new Button
            {
                Text = "Uložit a restartovat server",
                Location = new Point(252, 26),
                Size = new Size(185, 27)
            };
            btnApplyPort.Click += BtnApplyPort_Click;
            grpSettings.Controls.Add(btnApplyPort);

            _chkAutoStart = new CheckBox
            {
                Text = "Spouštět automaticky po startu Windows",
                Location = new Point(14, 65),
                Size = new Size(340, 24)
            };
            _chkAutoStart.CheckedChanged += ChkAutoStart_CheckedChanged;
            grpSettings.Controls.Add(_chkAutoStart);

            _lblFirewallStatus = new Label
            {
                Location = new Point(14, 102),
                Size = new Size(420, 24),
                Text = "Windows Firewall: kontroluji..."
            };
            grpSettings.Controls.Add(_lblFirewallStatus);

            _btnFirewall = new Button
            {
                Text = "Povolit ve Firewallu",
                Location = new Point(width - 170, 98),
                Size = new Size(156, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnFirewall.Click += BtnFirewall_Click;
            grpSettings.Controls.Add(_btnFirewall);
        }

        private void SettingsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        public void RefreshState()
        {
            _isUpdatingUi = true;
            try
            {
                int port = _context.Config.Port;
                _numPort.Value = port;
                _chkAutoStart.Checked = NetworkHelper.IsAutoStartEnabled();

                if (_context.HttpServer.IsRunning)
                {
                    _lblServerStatus.Text = "HTTP server: Běží na portu " + port;
                    _lblServerStatus.ForeColor = Color.FromArgb(22, 163, 74);
                }
                else
                {
                    _lblServerStatus.Text = "HTTP server: Zastaven (" + (_context.HttpServer.LastError ?? "chyba") + ")";
                    _lblServerStatus.ForeColor = Color.FromArgb(220, 38, 38);
                }

                _lblMdnsStatus.Text = _context.MdnsService.IsRunning
                    ? "mDNS služba: Registrována v síti jako \"PC-Ovladac\""
                    : "mDNS služba: Neaktivní";

                RefreshNetworkAddresses();
                RefreshFirewallStatus();
                ReloadCommandsTable();
                ReloadLogTable();
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private void ReloadCommandsTable()
        {
            _commandsCache = _context.GetCommandsSnapshot();
            _dgvCommands.Rows.Clear();
            foreach (var cmd in _commandsCache)
            {
                _dgvCommands.Rows.Add(
                    cmd.Name,
                    ActionExecutor.GetDisplayName(cmd.ActionType),
                    cmd.Parameter ?? "");
            }
        }

        private void ReloadLogTable()
        {
            _dgvLog.Rows.Clear();
            foreach (var entry in _context.GetLogSnapshot())
            {
                AddLogRow(entry, false);
            }
        }

        public void AppendLogEntry(CommandLogEntry entry)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<CommandLogEntry>(AppendLogEntry), entry);
                return;
            }
            AddLogRow(entry, true);
        }

        private void AddLogRow(CommandLogEntry entry, bool insertAtTop)
        {
            string statusPrefix = entry.Success ? "OK: " : "Chyba: ";
            int rowIndex;
            if (insertAtTop)
            {
                _dgvLog.Rows.Insert(0, entry.Timestamp.ToString("HH:mm:ss"), entry.SenderIp, entry.Command, statusPrefix + entry.ResultMessage);
                rowIndex = 0;
            }
            else
            {
                rowIndex = _dgvLog.Rows.Add(entry.Timestamp.ToString("HH:mm:ss"), entry.SenderIp, entry.Command, statusPrefix + entry.ResultMessage);
            }
            _dgvLog.Rows[rowIndex].Cells[3].Style.ForeColor = entry.Success
                ? Color.FromArgb(22, 163, 74)
                : Color.FromArgb(220, 38, 38);
        }

        private void DgvCommands_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _commandsCache.Count) return;
            if (e.ColumnIndex == _dgvCommands.Columns["TestBtn"].Index)
            {
                string cmdName = _commandsCache[e.RowIndex].Name;
                _context.DispatchCommand(cmdName, "127.0.0.1 (lokální test)");
            }
        }

        private void BtnAddCommand_Click(object sender, EventArgs e)
        {
            using (var dlg = new CommandEditDialog(null))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _commandsCache.Add(dlg.ResultMapping);
                    _context.SaveCommands(_commandsCache);
                    ReloadCommandsTable();
                }
            }
        }

        private void BtnEditCommand_Click(object sender, EventArgs e)
        {
            EditSelectedCommand();
        }

        private void EditSelectedCommand()
        {
            if (_dgvCommands.SelectedRows.Count == 0) return;
            int idx = _dgvCommands.SelectedRows[0].Index;
            if (idx < 0 || idx >= _commandsCache.Count) return;

            using (var dlg = new CommandEditDialog(_commandsCache[idx]))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _commandsCache[idx] = dlg.ResultMapping;
                    _context.SaveCommands(_commandsCache);
                    ReloadCommandsTable();
                }
            }
        }

        private void BtnDeleteCommand_Click(object sender, EventArgs e)
        {
            if (_dgvCommands.SelectedRows.Count == 0) return;
            int idx = _dgvCommands.SelectedRows[0].Index;
            if (idx < 0 || idx >= _commandsCache.Count) return;

            string name = _commandsCache[idx].Name;
            if (MessageBox.Show("Opravdu chcete smazat příkaz \"" + name + "\"?", "PC-Ovladac",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _commandsCache.RemoveAt(idx);
                _context.SaveCommands(_commandsCache);
                ReloadCommandsTable();
            }
        }

        private void RefreshNetworkAddresses()
        {
            int port = _context.Config.Port;
            List<NetworkInterfaceInfoItem> items = NetworkHelper.GetLocalIPv4Addresses();
            string primaryIp = items.Count > 0 ? items[0].IpAddress : "127.0.0.1";

            _txtPrimaryUrl.Text = "http://" + primaryIp + ":" + port;
            _txtMdnsUrl.Text = "http://PC-Ovladac.local:" + port;

            _lstAddresses.Items.Clear();
            if (items.Count == 0)
            {
                _lstAddresses.Items.Add("Nebyla nalezena žádná aktivní lokální IPv4 adresa.");
            }
            else
            {
                var parts = new List<string>();
                foreach (var item in items)
                {
                    parts.Add(item.Name + ": " + item.IpAddress);
                }
                _lstAddresses.Items.Add("Síťová rozhraní: " + string.Join("  |  ", parts.ToArray()));
            }
        }

        private void RefreshFirewallStatus()
        {
            bool ok = NetworkHelper.IsFirewallRulePresent(_context.Config.Port);
            if (ok)
            {
                _lblFirewallStatus.Text = "Windows Firewall: Výjimka pro port " + _context.Config.Port + " je aktivní";
                _lblFirewallStatus.ForeColor = Color.FromArgb(22, 163, 74);
            }
            else
            {
                _lblFirewallStatus.Text = "Windows Firewall: Výjimka nenalezena (může blokovat mobil)";
                _lblFirewallStatus.ForeColor = Color.FromArgb(217, 119, 6);
            }
        }

        private void BtnApplyPort_Click(object sender, EventArgs e)
        {
            int newPort = (int)_numPort.Value;
            _context.RestartServer(newPort);
            RefreshState();
        }

        private void ChkAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi) return;

            bool enable = _chkAutoStart.Checked;
            string exePath = Application.ExecutablePath;
            if (NetworkHelper.SetAutoStart(enable, exePath))
            {
                _context.Config.AutoStart = enable;
                _context.Config.Save();
            }
            else
            {
                MessageBox.Show("Nepodařilo se změnit nastavení automatického spuštění v registru Windows.",
                    "PC-Ovladac", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _isUpdatingUi = true;
                _chkAutoStart.Checked = !enable;
                _isUpdatingUi = false;
            }
        }

        private void BtnFirewall_Click(object sender, EventArgs e)
        {
            string err;
            if (NetworkHelper.AddFirewallRule(_context.Config.Port, out err))
            {
                MessageBox.Show("Výjimka ve Windows Firewallu pro port " + _context.Config.Port + " byla úspěšně přidána.",
                    "PC-Ovladac", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Nepodařilo se přidat výjimku do Windows Firewallu:\n" + err,
                    "PC-Ovladac", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshFirewallStatus();
        }
    }

    public class CommandEditDialog : Form
    {
        private TextBox _txtName;
        private ComboBox _cmbAction;
        private TextBox _txtParam;
        private Label _lblHint;

        public CommandMapping ResultMapping { get; private set; }

        public CommandEditDialog(CommandMapping existing)
        {
            Text = existing == null ? "Přidat příkaz" : "Upravit příkaz";
            Size = new Size(440, 265);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);

            var lblName = new Label { Text = "Název příkazu (např. ztlumit):", Location = new Point(16, 16), Size = new Size(380, 20) };
            _txtName = new TextBox { Location = new Point(16, 38), Size = new Size(390, 25) };

            var lblAction = new Label { Text = "Typ akce ve Windows:", Location = new Point(16, 72), Size = new Size(380, 20) };
            _cmbAction = new ComboBox
            {
                Location = new Point(16, 94),
                Size = new Size(390, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var opt in ActionExecutor.SupportedActions)
            {
                _cmbAction.Items.Add(opt);
            }
            _cmbAction.SelectedIndexChanged += CmbAction_SelectedIndexChanged;

            var lblParam = new Label { Text = "Parametr:", Location = new Point(16, 128), Size = new Size(380, 20) };
            _txtParam = new TextBox { Location = new Point(16, 150), Size = new Size(390, 25) };
            _lblHint = new Label { Location = new Point(16, 178), Size = new Size(390, 18), ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("Segoe UI", 8.5f) };

            var btnSave = new Button { Text = "Uložit", Location = new Point(236, 198), Size = new Size(80, 28) };
            var btnCancel = new Button { Text = "Zrušit", Location = new Point(326, 198), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
            btnSave.Click += BtnSave_Click;

            Controls.AddRange(new Control[] { lblName, _txtName, lblAction, _cmbAction, lblParam, _txtParam, _lblHint, btnSave, btnCancel });
            AcceptButton = btnSave;
            CancelButton = btnCancel;

            if (existing != null)
            {
                _txtName.Text = existing.Name;
                _txtParam.Text = existing.Parameter ?? "";
                for (int i = 0; i < _cmbAction.Items.Count; i++)
                {
                    if (string.Equals(((ActionTypeOption)_cmbAction.Items[i]).Id, existing.ActionType, StringComparison.OrdinalIgnoreCase))
                    {
                        _cmbAction.SelectedIndex = i;
                        break;
                    }
                }
            }
            if (_cmbAction.SelectedIndex < 0 && _cmbAction.Items.Count > 0)
            {
                _cmbAction.SelectedIndex = 0;
            }
        }

        private void CmbAction_SelectedIndexChanged(object sender, EventArgs e)
        {
            var opt = _cmbAction.SelectedItem as ActionTypeOption;
            if (opt == null) return;
            _txtParam.Enabled = opt.UsesParameter;
            _lblHint.Text = opt.UsesParameter ? opt.ParameterHint : "Tato akce nevyžaduje parametr.";
            if (!opt.UsesParameter) _txtParam.Text = "";
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string name = _txtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Zadejte název příkazu.", "PC-Ovladac", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var opt = _cmbAction.SelectedItem as ActionTypeOption;
            if (opt == null) return;

            ResultMapping = new CommandMapping(name, opt.Id, _txtParam.Text.Trim());
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
