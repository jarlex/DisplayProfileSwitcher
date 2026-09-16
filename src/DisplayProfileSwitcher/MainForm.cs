namespace DisplayProfileSwitcher;

internal sealed class MainForm : Form
{
    private const int WmHotkey = 0x0312;

    private readonly ConfigStore _store = new();
    private readonly DisplayController _controller = new();
    private readonly System.Windows.Forms.Timer _reapplyTimer = new();
    private readonly NotifyIcon _tray = new();

    private AppConfig _config;
    private readonly ConfigLoadState _configLoadState;
    private readonly string? _configLoadMessage;
    private HotkeyManager? _hotkeys;
    private DisplayProfile? _activeProfile;
    private bool _updatingGlobalOptions;

    private readonly ListBox _profiles = new() { Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Width = 220 };
    private readonly NumericUpDown _gamma = new() { DecimalPlaces = 2, Increment = 0.05m, Minimum = 0.30m, Maximum = 2.80m, Width = 120 };
    private readonly NumericUpDown _brightness = new() { Minimum = 0, Maximum = 100, Width = 120 };
    private readonly NumericUpDown _contrast = new() { Minimum = 0, Maximum = 100, Width = 120 };
    private readonly NumericUpDown _vibrance = new() { Minimum = 0, Maximum = 100, Width = 120 };
    private readonly TextBox _hotkey = new() { Width = 160, PlaceholderText = "CTRL+ALT+1" };
    private readonly CheckBox _allDisplays = new() { Text = "Aplicar a todos los monitores", AutoSize = true };
    private readonly NumericUpDown _reapply = new() { Minimum = 0, Maximum = 60, Width = 120 };
    private readonly CheckBox _applyLast = new() { Text = "Aplicar el último perfil al iniciar", AutoSize = true };
    private readonly CheckBox _runStartup = new() { Text = "Iniciar con Windows", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Text = "Listo" };

    public MainForm()
    {
        var load = _store.Load();
        _config = load.Config;
        _configLoadState = load.State;
        _configLoadMessage = load.Message;
        Text = "Display Profile Switcher";
        Width = 760;
        Height = 500;
        MinimumSize = new Size(700, 460);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        BuildTray();

        Shown += (_, _) => InitializeAfterHandleCreated();
        FormClosing += OnFormClosing;

        _reapplyTimer.Tick += (_, _) =>
        {
            if (_activeProfile is not null && _activeProfile.ReapplySeconds > 0)
                ApplyProfile(_activeProfile, silent: true, persistLastProfile: false);
        };

        PopulateList();
        _applyLast.Checked = _config.ApplyLastProfileOnStartup;
        _runStartup.Checked = _config.RunAtWindowsStartup;
        _applyLast.CheckedChanged += (_, _) => { if (!_updatingGlobalOptions) SaveGlobalOptions(); };
        _runStartup.CheckedChanged += (_, _) => { if (!_updatingGlobalOptions) SaveGlobalOptions(); };
        if (_configLoadState == ConfigLoadState.Corrupt)
            _status.Text = "Configuración corrupta: el archivo original no se reemplazó.";
        else if (_configLoadState == ConfigLoadState.RecoveredFromBackup)
            _status.Text = _configLoadMessage ?? "Configuración recuperada desde el backup.";
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        left.Controls.Add(_profiles, 0, 0);

        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var add = new Button { Text = "Añadir", AutoSize = true };
        var del = new Button { Text = "Borrar", AutoSize = true };
        add.Click += (_, _) => AddProfile();
        del.Click += (_, _) => DeleteProfile();
        leftButtons.Controls.Add(add);
        leftButtons.Controls.Add(del);
        left.Controls.Add(leftButtons, 0, 1);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 11,
            Padding = new Padding(14, 0, 0, 0),
            AutoScroll = true
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(editor, 0, "Nombre", _name);
        AddRow(editor, 1, "Gamma", _gamma);
        AddRow(editor, 2, "Brillo (50 = neutro)", _brightness);
        AddRow(editor, 3, "Contraste (50 = neutro)", _contrast);
        AddRow(editor, 4, "Digital Vibrance", _vibrance);
        AddRow(editor, 5, "Atajo global", _hotkey);
        AddRow(editor, 6, "Monitores", _allDisplays);

        var reapplyPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        reapplyPanel.Controls.Add(_reapply);
        reapplyPanel.Controls.Add(new Label { Text = "segundos (0 = desactivado)", AutoSize = true, Padding = new Padding(4, 7, 0, 0) });
        AddRow(editor, 7, "Reaplicar", reapplyPanel);

        var options = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        options.Controls.Add(_applyLast);
        options.Controls.Add(_runStartup);
        AddRow(editor, 8, "Opciones", options);

        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var save = new Button { Text = "Guardar perfil", AutoSize = true };
        var apply = new Button { Text = "Aplicar ahora", AutoSize = true };
        save.Click += (_, _) => SaveCurrentProfile();
        apply.Click += (_, _) =>
        {
            SaveCurrentProfile();
            if (_profiles.SelectedItem is DisplayProfile p)
                ApplyProfile(p);
        };
        actions.Controls.Add(save);
        actions.Controls.Add(apply);
        AddRow(editor, 9, string.Empty, actions);
        AddRow(editor, 10, "Estado", _status);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(editor, 1, 0);
        Controls.Add(root);

        _profiles.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
        _hotkey.KeyDown += CaptureHotkey;
    }

    private void CaptureHotkey(object? sender, KeyEventArgs e)
    {
        if (HotkeyManager.IsModifierKey(e.KeyCode))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        var modifiers = e.Modifiers | (Control.ModifierKeys & (Keys.LWin | Keys.RWin));
        if (!HotkeyManager.TryFormat(modifiers, e.KeyCode, out var hotkey))
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        _hotkey.Text = hotkey;
        _hotkey.SelectionStart = _hotkey.TextLength;
        SaveCurrentProfile();
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 7, 0, 0)
        }, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private void BuildTray()
    {
        _tray.Icon = SystemIcons.Application;
        _tray.Text = "Display Profile Switcher";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowMainWindow();
        RebuildTrayMenu();
    }

    private void RebuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        foreach (var profile in _config.Profiles)
        {
            var item = new ToolStripMenuItem(profile.Name);
            if (!string.IsNullOrWhiteSpace(profile.Hotkey))
                item.ShortcutKeyDisplayString = profile.Hotkey;
            item.Click += (_, _) => ApplyProfile(profile);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var open = new ToolStripMenuItem("Abrir");
        open.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(open);

        var exit = new ToolStripMenuItem("Salir");
        exit.Click += (_, _) =>
        {
            _allowExit = true;
            Close();
        };
        menu.Items.Add(exit);
        _tray.ContextMenuStrip = menu;
    }

    private bool _allowExit;

    private void InitializeAfterHandleCreated()
    {
        _hotkeys ??= new HotkeyManager(Handle);
        RegisterHotkeys();

        if (_config.ApplyLastProfileOnStartup &&
            (_config.LastProfileId.HasValue || !string.IsNullOrWhiteSpace(_config.LastProfileName)))
        {
            var p = _config.LastProfileId.HasValue
                ? _config.Profiles.FirstOrDefault(x => x.Id == _config.LastProfileId.Value)
                : null;
            p ??= _config.Profiles.FirstOrDefault(x =>
                x.Name.Equals(_config.LastProfileName, StringComparison.OrdinalIgnoreCase));
            if (p is not null)
                ApplyProfile(p, silent: true);
        }
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null)
            return;

        var errors = _hotkeys.RegisterProfiles(_config.Profiles);
        _status.Text = errors.Count == 0 ? "Atajos registrados" : string.Join(" | ", errors);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && _hotkeys is not null)
        {
            var id = m.WParam.ToInt32();
            var profile = _hotkeys.Resolve(id);
            if (profile is not null)
            {
                ApplyProfile(profile);
                return;
            }
        }

        base.WndProc(ref m);
    }

    private void PopulateList(string? selectName = null)
    {
        _profiles.BeginUpdate();
        _profiles.Items.Clear();
        foreach (var profile in _config.Profiles)
            _profiles.Items.Add(profile);
        _profiles.DisplayMember = nameof(DisplayProfile.Name);
        _profiles.EndUpdate();

        if (_profiles.Items.Count == 0)
            return;

        var idx = 0;
        if (!string.IsNullOrWhiteSpace(selectName))
        {
            for (var i = 0; i < _profiles.Items.Count; i++)
            {
                if ((_profiles.Items[i] as DisplayProfile)?.Name == selectName)
                {
                    idx = i;
                    break;
                }
            }
        }
        _profiles.SelectedIndex = idx;
    }

    private void LoadSelectedProfile()
    {
        if (_profiles.SelectedItem is not DisplayProfile p)
            return;

        _name.Text = p.Name;
        _gamma.Value = Math.Clamp(p.Gamma, _gamma.Minimum, _gamma.Maximum);
        _brightness.Value = Math.Clamp(p.Brightness, (int)_brightness.Minimum, (int)_brightness.Maximum);
        _contrast.Value = Math.Clamp(p.Contrast, (int)_contrast.Minimum, (int)_contrast.Maximum);
        _vibrance.Value = Math.Clamp(p.Vibrance, (int)_vibrance.Minimum, (int)_vibrance.Maximum);
        _hotkey.Text = p.Hotkey;
        _allDisplays.Checked = p.ApplyToAllDisplays;
        _reapply.Value = Math.Clamp(p.ReapplySeconds, (int)_reapply.Minimum, (int)_reapply.Maximum);
    }

    private void SaveCurrentProfile()
    {
        if (_profiles.SelectedItem is not DisplayProfile p)
            return;

        var oldName = p.Name;
        var oldGamma = p.Gamma;
        var oldBrightness = p.Brightness;
        var oldContrast = p.Contrast;
        var oldVibrance = p.Vibrance;
        var oldHotkey = p.Hotkey;
        var oldApplyToAllDisplays = p.ApplyToAllDisplays;
        var oldReapplySeconds = p.ReapplySeconds;
        var configSaved = false;
        var newName = string.IsNullOrWhiteSpace(_name.Text) ? "Perfil" : _name.Text.Trim();
        if (_config.Profiles.Any(other => other != p && other.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            _status.Text = $"Ya existe un perfil llamado {newName}.";
            return;
        }
        p.Name = newName;
        p.Gamma = _gamma.Value;
        p.Brightness = (int)_brightness.Value;
        p.Contrast = (int)_contrast.Value;
        p.Vibrance = (int)_vibrance.Value;
        p.Hotkey = _hotkey.Text.Trim();
        p.ApplyToAllDisplays = _allDisplays.Checked;
        p.ReapplySeconds = (int)_reapply.Value;

        try
        {
            SaveConfig();
            configSaved = true;
            StartupManager.SetEnabled(_runStartup.Checked);
        }
        catch (Exception ex)
        {
            if (!configSaved)
            {
                p.Name = oldName;
                p.Gamma = oldGamma;
                p.Brightness = oldBrightness;
                p.Contrast = oldContrast;
                p.Vibrance = oldVibrance;
                p.Hotkey = oldHotkey;
                p.ApplyToAllDisplays = oldApplyToAllDisplays;
                p.ReapplySeconds = oldReapplySeconds;
                PopulateList(oldName);
                LoadSelectedProfile();
            }
            _status.Text = configSaved
                ? $"Perfil guardado, pero no se pudo actualizar el inicio con Windows: {ex.Message}"
                : $"No se pudo guardar el perfil: {ex.Message}";
            return;
        }

        RegisterHotkeys();
        RebuildTrayMenu();
        PopulateList(p.Name);
        _status.Text = oldName == p.Name ? "Perfil guardado" : $"Perfil renombrado a {p.Name}";
    }

    private void SaveGlobalOptions()
    {
        var oldApplyLastProfileOnStartup = _config.ApplyLastProfileOnStartup;
        var oldRunAtWindowsStartup = _config.RunAtWindowsStartup;
        var configSaved = false;
        _config.ApplyLastProfileOnStartup = _applyLast.Checked;
        _config.RunAtWindowsStartup = _runStartup.Checked;
        try
        {
            SaveConfig();
            configSaved = true;
            StartupManager.SetEnabled(_config.RunAtWindowsStartup);
            _status.Text = "Opciones guardadas";
        }
        catch (Exception ex)
        {
            if (!configSaved)
            {
                _config.ApplyLastProfileOnStartup = oldApplyLastProfileOnStartup;
                _config.RunAtWindowsStartup = oldRunAtWindowsStartup;
                _updatingGlobalOptions = true;
                try
                {
                    _applyLast.Checked = oldApplyLastProfileOnStartup;
                    _runStartup.Checked = oldRunAtWindowsStartup;
                }
                finally
                {
                    _updatingGlobalOptions = false;
                }
            }
            _status.Text = configSaved
                ? $"Opciones guardadas, pero no se pudo actualizar el inicio con Windows: {ex.Message}"
                : $"No se pudieron guardar las opciones: {ex.Message}";
        }
    }

    private void SaveConfig() => _store.Save(_config);

    private void AddProfile()
    {
        var p = new DisplayProfile { Name = UniqueName("Nuevo perfil") };
        _config.Profiles.Add(p);
        try
        {
            SaveConfig();
        }
        catch (Exception ex)
        {
            _config.Profiles.Remove(p);
            _status.Text = $"No se pudo guardar el nuevo perfil: {ex.Message}";
            return;
        }
        PopulateList(p.Name);
        RegisterHotkeys();
        RebuildTrayMenu();
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        var n = 2;
        while (_config.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {n++}";
        return name;
    }

    private void DeleteProfile()
    {
        if (_profiles.SelectedItem is not DisplayProfile p)
            return;
        if (_config.Profiles.Count <= 1)
        {
            _status.Text = "Debe quedar al menos un perfil.";
            return;
        }

        var originalIndex = _config.Profiles.IndexOf(p);
        var oldLastProfileId = _config.LastProfileId;
        var oldLastProfileName = _config.LastProfileName;
        _config.Profiles.RemoveAt(originalIndex);
        if (_config.LastProfileId == p.Id)
        {
            _config.LastProfileId = null;
            _config.LastProfileName = null;
        }
        try
        {
            SaveConfig();
        }
        catch (Exception ex)
        {
            _config.Profiles.Insert(originalIndex, p);
            _config.LastProfileId = oldLastProfileId;
            _config.LastProfileName = oldLastProfileName;
            _status.Text = $"No se pudo eliminar el perfil: {ex.Message}";
            return;
        }
        PopulateList();
        RegisterHotkeys();
        RebuildTrayMenu();
    }

    private void ApplyProfile(DisplayProfile profile, bool silent = false, bool persistLastProfile = true)
    {
        var result = _controller.Apply(profile);
        _activeProfile = profile;
        string? persistenceError = null;
        if (persistLastProfile)
        {
            var oldLastProfileId = _config.LastProfileId;
            var oldLastProfileName = _config.LastProfileName;
            _config.LastProfileId = profile.Id;
            _config.LastProfileName = profile.Name;
            try { SaveConfig(); }
            catch (Exception ex)
            {
                _config.LastProfileId = oldLastProfileId;
                _config.LastProfileName = oldLastProfileName;
                persistenceError = ex.Message;
            }
        }

        if (profile.ReapplySeconds > 0)
        {
            _reapplyTimer.Interval = Math.Clamp(profile.ReapplySeconds, 1, 60) * 1000;
            _reapplyTimer.Start();
        }
        else
        {
            _reapplyTimer.Stop();
        }

        var details = string.Join(" | ", result.Components.Select(component =>
            component.Success
                ? $"{component.Component}: correcto ({component.AffectedMonitors} monitor(es))"
                : $"{component.Component}: fallo ({string.Join("; ", component.Errors)})"));
        _status.Text = result.Success
            ? $"Aplicado: {profile.Name}. {details}"
            : result.HasPartialFailure
                ? $"Aplicación parcial: {profile.Name}. {details}"
                : $"Aplicación fallida: {profile.Name}. {details}";
        if (persistenceError is not null)
            _status.Text += $" No se pudo guardar el último perfil: {persistenceError}";

        _tray.Text = TruncateTrayText($"Display Profile Switcher - {profile.Name}");
        if (!silent)
            _tray.ShowBalloonTip(1000, "Perfil de pantalla", _status.Text, result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private static string TruncateTrayText(string text) => text.Length <= 63 ? text : text[..63];

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _reapplyTimer.Stop();
        _hotkeys?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }
}
