namespace DisplayProfileSwitcher;

internal sealed class MainForm : Form
{
    private const int WmHotkey = 0x0312;

    private readonly ConfigStore _store = new();
    private readonly DisplayController _controller = new();
    private readonly System.Windows.Forms.Timer _reapplyTimer = new();
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 1000 };
    private readonly NotifyIcon _tray = new();

    private AppConfig _config;
    private readonly ConfigLoadState _configLoadState;
    private readonly string? _configLoadMessage;
    private HotkeyManager? _hotkeys;
    private DisplayProfile? _activeProfile;
    private bool _updatingGlobalOptions;
    private bool _capturingHotkey;
    private ProfilePreview? _profilePreview;
    private DisplayProfile? _previewProfile;
    private DisplayPreviewSnapshot? _previewSnapshot;
    private DisplayProfile? _previewPreviousActiveProfile;
    private int _previewPreviousReapplyInterval;
    private bool _previewPreviousReapplyEnabled;

    private readonly ListBox _profiles = new() { Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Width = 220 };
    private readonly NumericUpDown _gamma = new() { DecimalPlaces = 2, Increment = 0.05m, Minimum = 0.30m, Maximum = 2.80m, Width = 120 };
    private readonly NumericUpDown _brightness = new() { Minimum = 0, Maximum = 100, Width = 120 };
    private readonly NumericUpDown _contrast = new() { Minimum = 0, Maximum = 100, Width = 120 };
    private readonly NumericUpDown _vibrance = new() { Minimum = 0, Maximum = 100, Width = 120 };
    private readonly TextBox _hotkey = new() { Width = 160, ReadOnly = true, PlaceholderText = "CTRL+ALT+1" };
    private readonly Button _captureHotkey = new() { Text = "Capturar", AutoSize = true };
    private readonly Button _clearHotkey = new() { Text = "Quitar", AutoSize = true };
    private readonly CheckBox _allDisplays = new() { Text = "Aplicar a todos los monitores", AutoSize = true };
    private readonly NumericUpDown _reapply = new() { Minimum = 0, Maximum = 60, Width = 120 };
    private readonly CheckBox _applyLast = new() { Text = "Aplicar el último perfil al iniciar", AutoSize = true };
    private readonly CheckBox _runStartup = new() { Text = "Iniciar con Windows", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Text = "Listo" };
    private readonly Label _previewCountdown = new() { AutoSize = true, Text = "" };
    private Button? _previewButton;
    private Button? _confirmPreviewButton;
    private Button? _revertPreviewButton;

    public MainForm()
    {
        var load = _store.Load();
        _config = load.Config;
        _configLoadState = load.State;
        _configLoadMessage = load.Message;
        Icon = LoadApplicationIcon();
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
            if (!IsPreviewActive && _activeProfile is not null && _activeProfile.ReapplySeconds > 0)
                ApplyProfile(_activeProfile, silent: true, persistLastProfile: false);
        };
        _previewTimer.Tick += (_, _) => PreviewTick();

        PopulateList();
        _applyLast.Checked = _config.ApplyLastProfileOnStartup;
        _runStartup.Checked = _config.RunAtWindowsStartup;
        _applyLast.CheckedChanged += (_, _) => { if (!_updatingGlobalOptions) SaveGlobalOptions(); };
        _runStartup.CheckedChanged += (_, _) => { if (!_updatingGlobalOptions) SaveGlobalOptions(); };
        if (_configLoadState == ConfigLoadState.Corrupt)
            _status.Text = "Configuración: el archivo está dañado; se conservó el original y una copia para revisión.";
        else if (_configLoadState == ConfigLoadState.RecoveredFromBackup)
            _status.Text = _configLoadMessage ?? "Configuración recuperada desde el backup.";
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var upperArea = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            Size = new Size(680, 300),
            SplitterDistance = 210,
            Panel1MinSize = 180,
            Panel2MinSize = 360
        };

        var profileListLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8, 6, 8, 6)
        };
        profileListLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profileListLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        profileListLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        profileListLayout.Controls.Add(_profiles, 0, 0);

        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        var add = new Button { Text = "Añadir", AutoSize = true };
        var del = new Button { Text = "Borrar", AutoSize = true };
        add.Click += (_, _) => AddProfile();
        del.Click += (_, _) => DeleteProfile();
        leftButtons.Controls.Add(add);
        leftButtons.Controls.Add(del);
        profileListLayout.Controls.Add(leftButtons, 0, 1);

        var profilesGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Perfiles"
        };
        profilesGroup.Controls.Add(profileListLayout);
        upperArea.Panel1.Controls.Add(profilesGroup);

        var profileEditorScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(8, 6, 8, 6)
        };

        var profileFields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 9
        };
        profileFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        profileFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < profileFields.RowCount; row++)
            profileFields.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddRow(profileFields, 0, "Nombre", _name);
        AddRow(profileFields, 1, "Gamma", _gamma);
        AddRow(profileFields, 2, "Brillo (50 = neutro)", _brightness);
        AddRow(profileFields, 3, "Contraste (50 = neutro)", _contrast);
        AddRow(profileFields, 4, "Digital Vibrance", _vibrance);
        var hotkeyPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        hotkeyPanel.Controls.Add(_hotkey);
        hotkeyPanel.Controls.Add(_captureHotkey);
        hotkeyPanel.Controls.Add(_clearHotkey);
        AddRow(profileFields, 5, "Atajo global", hotkeyPanel);
        AddRow(profileFields, 6, "Monitores", _allDisplays);

        var reapplyPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        reapplyPanel.Controls.Add(_reapply);
        reapplyPanel.Controls.Add(new Label { Text = "segundos (0 = desactivado)", AutoSize = true, Padding = new Padding(4, 7, 0, 0) });
        AddRow(profileFields, 7, "Reaplicar", reapplyPanel);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        var save = new Button { Text = "Guardar perfil", AutoSize = true };
        var apply = new Button { Text = "Aplicar ahora", AutoSize = true };
        var preview = new Button { Text = "Probar 10 s", AutoSize = true };
        var confirm = new Button { Text = "Confirmar", AutoSize = true, Enabled = false };
        var revert = new Button { Text = "Revertir", AutoSize = true, Enabled = false };
        _previewButton = preview;
        _confirmPreviewButton = confirm;
        _revertPreviewButton = revert;
        save.Click += (_, _) => SaveCurrentProfile();
        apply.Click += (_, _) =>
        {
            if (IsPreviewActive)
                return;
            SaveCurrentProfile();
            if (_profiles.SelectedItem is DisplayProfile p)
                ApplyProfile(p);
        };
        preview.Click += (_, _) => StartPreview(confirm, revert, preview);
        confirm.Click += (_, _) => ConfirmPreview(confirm, revert, preview);
        revert.Click += (_, _) => RevertPreview(confirm, revert, preview);
        actions.Controls.Add(save);
        actions.Controls.Add(apply);
        actions.Controls.Add(preview);
        actions.Controls.Add(confirm);
        actions.Controls.Add(revert);
        profileFields.Controls.Add(actions, 0, 8);
        profileFields.SetColumnSpan(actions, 2);

        profileEditorScroll.Controls.Add(profileFields);
        var profileEditorGroup = new GroupBox
        {
            Text = "Configuración del perfil",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        profileEditorGroup.Controls.Add(profileEditorScroll);
        upperArea.Panel2.Padding = new Padding(10, 0, 0, 0);
        upperArea.Panel2.Controls.Add(profileEditorGroup);

        var applicationOptions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(8, 4, 8, 4)
        };
        applicationOptions.Controls.Add(_applyLast);
        applicationOptions.Controls.Add(_runStartup);
        var applicationOptionsGroup = new GroupBox
        {
            Text = "Opciones de la aplicación",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 8, 0, 0)
        };
        applicationOptionsGroup.Controls.Add(applicationOptions);

        var messagesLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8, 4, 8, 4)
        };
        messagesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        messagesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        messagesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        messagesLayout.Controls.Add(_status, 0, 0);
        messagesLayout.Controls.Add(_previewCountdown, 0, 1);
        var messagesGroup = new GroupBox
        {
            Text = "Mensajes",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 8, 0, 0)
        };
        messagesGroup.Controls.Add(messagesLayout);

        root.Controls.Add(upperArea, 0, 0);
        root.Controls.Add(applicationOptionsGroup, 0, 1);
        root.Controls.Add(messagesGroup, 0, 2);
        Controls.Add(root);

        _profiles.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
        _hotkey.KeyDown += CaptureHotkey;
        _captureHotkey.Click += (_, _) => SetHotkeyCaptureMode(!_capturingHotkey);
        _clearHotkey.Click += (_, _) => ClearHotkey();
    }

    private void CaptureHotkey(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (!_capturingHotkey)
            return;

        if (e.KeyCode == Keys.Escape)
        {
            SetHotkeyCaptureMode(false);
            _status.Text = "Captura cancelada";
            return;
        }

        if (HotkeyManager.IsModifierKey(e.KeyCode))
            return;

        var modifiers = e.Modifiers | (Control.ModifierKeys & (Keys.LWin | Keys.RWin));
        if (!HotkeyManager.TryFormat(modifiers, e.KeyCode, out var hotkey))
        {
            _status.Text = "Tecla no compatible; use letras, números o teclas de función.";
            return;
        }

        if (SaveCurrentProfile(hotkey))
            SetHotkeyCaptureMode(false);
    }

    private void SetHotkeyCaptureMode(bool enabled)
    {
        _capturingHotkey = enabled;
        _captureHotkey.Text = enabled ? "Cancelar" : "Capturar";
        _hotkey.BackColor = enabled ? Color.LightYellow : SystemColors.Window;
        _hotkey.PlaceholderText = enabled ? "Pulsa una combinación..." : "CTRL+ALT+1";
        if (enabled)
        {
            _hotkey.Focus();
            _status.Text = "Captura activa: pulsa una combinación o Escape para cancelar.";
        }
    }

    private void ClearHotkey()
    {
        SetHotkeyCaptureMode(false);
        SaveCurrentProfile(string.Empty);
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        while (panel.RowStyles.Count <= row)
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 7, 0, 0)
        }, 0, row);
        panel.Controls.Add(control, 1, row);
        control.Anchor |= AnchorStyles.Left | AnchorStyles.Right;
    }

    private void BuildTray()
    {
        _tray.Icon = LoadApplicationIcon();
        _tray.Text = "Display Profile Switcher";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowMainWindow();
        RebuildTrayMenu();
    }

    private static System.Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null)
                return icon;
        }
        catch
        {
            // Use the system icon if the executable has no readable associated icon.
        }

        return new System.Drawing.Icon(SystemIcons.Application, SystemIcons.Application.Size);
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
        _status.Text = Diagnostics.FormatHotkeyStatus(errors);
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
        SetHotkeyCaptureMode(false);
        _allDisplays.Checked = p.ApplyToAllDisplays;
        _reapply.Value = Math.Clamp(p.ReapplySeconds, (int)_reapply.Minimum, (int)_reapply.Maximum);
    }

    private bool SaveCurrentProfile(string? hotkeyOverride = null)
    {
        if (_profiles.SelectedItem is not DisplayProfile p)
            return false;

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
            return false;
        }
        var candidateHotkey = (hotkeyOverride ?? _hotkey.Text).Trim();
        if (!string.IsNullOrEmpty(candidateHotkey))
        {
            if (!HotkeyManager.TryCanonicalize(candidateHotkey, out var canonicalHotkey))
            {
                _status.Text = $"El atajo '{candidateHotkey}' no es válido.";
                return false;
            }
            if (_config.Profiles.Any(other => other != p &&
                HotkeyManager.TryCanonicalize(other.Hotkey, out var otherCanonical) &&
                otherCanonical.Equals(canonicalHotkey, StringComparison.OrdinalIgnoreCase)))
            {
                _status.Text = $"El atajo '{canonicalHotkey}' ya está asignado a otro perfil.";
                return false;
            }
            candidateHotkey = canonicalHotkey;
        }
        p.Name = newName;
        p.Gamma = _gamma.Value;
        p.Brightness = (int)_brightness.Value;
        p.Contrast = (int)_contrast.Value;
        p.Vibrance = (int)_vibrance.Value;
        p.Hotkey = candidateHotkey;
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
            Diagnostics.Log(DiagnosticCategory.Configuration, "actualizar el inicio con Windows", ex);
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
                ? "Perfil guardado, pero no se pudo actualizar el inicio con Windows."
                : Diagnostics.FormatException(DiagnosticCategory.Configuration, "guardar el perfil");
            return false;
        }

        RegisterHotkeys();
        RebuildTrayMenu();
        PopulateList(p.Name);
        _status.Text = oldName == p.Name ? "Perfil guardado" : $"Perfil renombrado a {p.Name}";
        return true;
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
            Diagnostics.Log(DiagnosticCategory.Configuration, "guardar las opciones", ex);
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
                ? "Opciones guardadas, pero no se pudo actualizar el inicio con Windows."
                : Diagnostics.FormatException(DiagnosticCategory.Configuration, "guardar las opciones");
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
            Diagnostics.Log(DiagnosticCategory.Configuration, "crear un perfil", ex);
            _config.Profiles.Remove(p);
            _status.Text = Diagnostics.FormatException(DiagnosticCategory.Configuration, "guardar el nuevo perfil");
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
            Diagnostics.Log(DiagnosticCategory.Configuration, "eliminar un perfil", ex);
            _config.Profiles.Insert(originalIndex, p);
            _config.LastProfileId = oldLastProfileId;
            _config.LastProfileName = oldLastProfileName;
            _status.Text = Diagnostics.FormatException(DiagnosticCategory.Configuration, "eliminar el perfil");
            return;
        }
        PopulateList();
        RegisterHotkeys();
        RebuildTrayMenu();
    }

    private void ApplyProfile(DisplayProfile profile, bool silent = false, bool persistLastProfile = true)
    {
        if (IsPreviewActive)
            return;

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
                Diagnostics.Log(DiagnosticCategory.Configuration, "guardar el último perfil", ex);
                _config.LastProfileId = oldLastProfileId;
                _config.LastProfileName = oldLastProfileName;
                persistenceError = "no se pudo guardar el último perfil; comprueba los permisos de AppData";
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

        var details = FormatApplyResult(result);
        _status.Text = result.Success
            ? $"Aplicado: {profile.Name}. {details}"
            : result.HasPartialFailure
                ? $"Aplicación parcial: {profile.Name}. {details}"
                : $"Aplicación fallida: {profile.Name}. {details}";
        if (persistenceError is not null)
            _status.Text += $" {persistenceError}.";

        _tray.Text = TruncateTrayText($"Display Profile Switcher - {profile.Name}");
        if (!silent)
            _tray.ShowBalloonTip(1000, "Perfil de pantalla", Diagnostics.Limit(_status.Text), result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private void StartPreview(Button confirm, Button revert, Button preview)
    {
        if (_profilePreview is not null && _profilePreview.IsActive)
            return;
        if (_profiles.SelectedItem is not DisplayProfile profile)
            return;
        if (!SaveCurrentProfile())
            return;
        if (!_controller.TryCaptureSnapshot(profile, out var snapshot, out var error))
        {
            _status.Text = error;
            return;
        }

        _previewPreviousActiveProfile = _activeProfile;
        _previewPreviousReapplyInterval = _reapplyTimer.Interval;
        _previewPreviousReapplyEnabled = _reapplyTimer.Enabled;
        var applied = _controller.Apply(profile);
        if (!applied.Success)
        {
            var restored = _controller.Restore(snapshot!);
            _status.Text = restored.Success
                ? $"Prueba cancelada: aplicación incompleta. Estado anterior restaurado. {FormatApplyResult(applied)}"
                : $"Prueba cancelada: aplicación incompleta y reversión incompleta. Aplicación: {FormatApplyResult(applied)} Reversión: {FormatApplyResult(restored)}";
            ClearPreviewPreviousState();
            return;
        }

        _previewSnapshot = snapshot;
        _previewProfile = profile;
        _profilePreview = new ProfilePreview(10);
        _reapplyTimer.Stop();
        SetPreviewButtons(confirm, revert, preview, active: true);
        UpdatePreviewCountdown();
    }

    private void PreviewTick()
    {
        if (_profilePreview is null || !_profilePreview.Tick())
            return;
        if (_profilePreview.IsActive)
        {
            UpdatePreviewCountdown();
            return;
        }
        SetPreviewButtons(_confirmPreviewButton!, _revertPreviewButton!, _previewButton!, active: false);
        RestorePreview();
    }

    private void ConfirmPreview(Button confirm, Button revert, Button preview)
    {
        if (_profilePreview is null || !_profilePreview.Confirm())
            return;
        _previewTimer.Stop();
        _previewSnapshot = null;
        if (_previewProfile is not null)
        {
            _activeProfile = _previewProfile;
            PersistLastProfile(_previewProfile);
            ConfigureReapplyTimer(_activeProfile);
        }
        _previewProfile = null;
        _profilePreview = null;
        ClearPreviewPreviousState();
        SetPreviewButtons(confirm, revert, preview, active: false);
        _previewCountdown.Text = "Prueba confirmada";
    }

    private void RevertPreview(Button confirm, Button revert, Button preview)
    {
        if (_profilePreview is null || !_profilePreview.Revert())
            return;
        RestorePreview();
        SetPreviewButtons(confirm, revert, preview, active: false);
    }

    private void RestorePreview()
    {
        _previewTimer.Stop();
        if (_previewSnapshot is not null)
        {
            var result = _controller.Restore(_previewSnapshot);
            _status.Text = result.Success
                ? "Prueba revertida; se restauró el estado anterior."
                : $"Reversión incompleta: no se pudo restaurar todo el estado anterior. {FormatApplyResult(result)}";
        }
        _activeProfile = _previewPreviousActiveProfile;
        RestorePreviousReapplyTimer();
        _previewSnapshot = null;
        _previewProfile = null;
        _profilePreview = null;
        ClearPreviewPreviousState();
        _previewCountdown.Text = "";
    }

    private void UpdatePreviewCountdown()
    {
        _previewCountdown.Text = $"Quedan {_profilePreview!.RemainingSeconds} s";
        _previewTimer.Start();
    }

    private void SetPreviewButtons(Button confirm, Button revert, Button preview, bool active)
    {
        confirm.Enabled = active;
        revert.Enabled = active;
        preview.Enabled = !active;
        _profiles.Enabled = !active;
    }

    private bool IsPreviewActive => _profilePreview?.IsActive == true;

    private void ConfigureReapplyTimer(DisplayProfile? profile)
    {
        if (profile?.ReapplySeconds > 0)
        {
            _reapplyTimer.Interval = Math.Clamp(profile.ReapplySeconds, 1, 60) * 1000;
            _reapplyTimer.Start();
        }
        else
        {
            _reapplyTimer.Stop();
        }
    }

    private void RestorePreviousReapplyTimer()
    {
        _reapplyTimer.Interval = _previewPreviousReapplyInterval;
        if (_previewPreviousReapplyEnabled)
            _reapplyTimer.Start();
        else
            _reapplyTimer.Stop();
    }

    private void ClearPreviewPreviousState()
    {
        _previewPreviousActiveProfile = null;
        _previewPreviousReapplyInterval = 0;
        _previewPreviousReapplyEnabled = false;
    }

    private static string FormatApplyResult(ApplyResult result) => string.Join(" | ", result.Components.Select(component =>
        component.Success
            ? $"{component.Component}: correcto ({component.AffectedMonitors} monitor(es))"
            : $"{component.Component}: fallo ({string.Join("; ", component.Errors)})"));

    private void PersistLastProfile(DisplayProfile profile)
    {
        var oldLastProfileId = _config.LastProfileId;
        var oldLastProfileName = _config.LastProfileName;
        _config.LastProfileId = profile.Id;
        _config.LastProfileName = profile.Name;
        try
        {
            SaveConfig();
        }
        catch (Exception ex)
        {
            Diagnostics.Log(DiagnosticCategory.Configuration, "guardar el último perfil", ex);
            _config.LastProfileId = oldLastProfileId;
            _config.LastProfileName = oldLastProfileName;
            _status.Text = "Perfil aplicado, pero no se pudo guardar como último perfil; comprueba los permisos de AppData.";
        }
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
        if (_profilePreview?.IsActive == true)
            RestorePreview();
        _reapplyTimer.Stop();
        _previewTimer.Stop();
        _hotkeys?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }
}
