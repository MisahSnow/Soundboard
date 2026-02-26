using System.Text.Json;
using NAudio.Wave;

namespace SoundboardNet;

public sealed class MainForm : Form
{
    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
    { ".wav", ".mp3", ".flac", ".ogg", ".aiff", ".aif" };
    private static readonly Color[] Palette =
    [
        Color.Red, Color.LimeGreen, Color.DodgerBlue, Color.Magenta, Color.Gold,
        Color.MediumPurple, Color.Orange, Color.White, Color.Cyan, Color.Black
    ];

    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "soundboardnet.settings.json");
    private readonly List<Tile> _tiles = [];
    private readonly List<Playback> _active = [];
    private readonly object _playbackLock = new();
    private Settings _settings = new();
    private bool _loading;
    private Tile? _menuTile;

    private readonly TextBox _folderBox = new() { ReadOnly = true };
    private readonly Button _chooseBtn = new() { Text = "Choose Sound Folder" };
    private readonly Button _reloadBtn = new() { Text = "Reload Sounds" };
    private readonly ComboBox _output1 = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _output2 = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _refreshDevicesBtn = new() { Text = "Refresh Devices" };
    private readonly Button _stopAllBtn = new() { Text = "Stop All" };
    private readonly FlowLayoutPanel _grid = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ContextMenuStrip _tileMenu = new();
    private readonly ToolStripMenuItem _renameMenuItem = new("Rename File");
    private readonly ToolStripMenuItem _setHotkeyMenuItem = new("Set Hotkey");
    private readonly ToolStripMenuItem _clearHotkeyMenuItem = new("Clear Hotkey");
    private readonly ToolStripMenuItem _changeColorMenuItem = new("Change Tile Color...");
    private readonly ToolStripMenuItem _resetColorMenuItem = new("Reset Tile Color");
    private readonly List<DeviceOption> _devices = [];

    public MainForm()
    {
        Text = ".NET Soundboard";
        Width = 1150;
        Height = 780;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        AllowDrop = true;
        KeyPreview = true;
        BuildUi();
        HookEvents();
        ApplyDarkTheme(this);

        _loading = true;
        LoadSettings();
        RefreshDevices();
        RestoreOutputs();
        LoadFolderIfValid(_settings.LastFolder);
        _loading = false;
        SaveSettings();
        SetStatus("Ready.");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopAll();
        SaveSettings();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(top, 0, 0);
        _folderBox.Dock = DockStyle.Fill;
        top.Controls.Add(_chooseBtn, 0, 0);
        top.Controls.Add(_folderBox, 1, 0);
        top.Controls.Add(_reloadBtn, 2, 0);

        var outputs = new GroupBox { Text = "Audio Outputs (up to 2)", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        root.Controls.Add(outputs, 0, 1);
        var ol = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, AutoSize = true };
        ol.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ol.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ol.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        outputs.Controls.Add(ol);
        ol.Controls.Add(new Label { Text = "Output 1", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        ol.Controls.Add(_output1, 1, 0); ol.SetColumnSpan(_output1, 3);
        ol.Controls.Add(new Label { Text = "Output 2", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        ol.Controls.Add(_output2, 1, 1); ol.SetColumnSpan(_output2, 3);
        ol.Controls.Add(_refreshDevicesBtn, 2, 2);
        ol.Controls.Add(_stopAllBtn, 3, 2);
        _output1.Width = _output2.Width = 420;

        var soundsBox = new GroupBox { Text = "Sounds", Dock = DockStyle.Fill, Padding = new Padding(8) };
        soundsBox.Controls.Add(_grid);
        root.Controls.Add(soundsBox, 0, 2);

        _status.Padding = new Padding(8, 0, 0, 0);
        root.Controls.Add(_status, 0, 3);

        _grid.Resize += (_, _) => ReflowGrid();

        _renameMenuItem.Click += (_, _) => RenameTile(_menuTile);
        _setHotkeyMenuItem.Click += (_, _) => SetTileHotkey(_menuTile);
        _clearHotkeyMenuItem.Click += (_, _) => ClearTileHotkey(_menuTile);
        _changeColorMenuItem.Click += (_, _) => ChangeTileColor(_menuTile);
        _resetColorMenuItem.Click += (_, _) => ResetTileColor(_menuTile);
        _tileMenu.Items.AddRange([_renameMenuItem, _setHotkeyMenuItem, _clearHotkeyMenuItem, _changeColorMenuItem, _resetColorMenuItem]);
    }

    private void HookEvents()
    {
        _chooseBtn.Click += (_, _) => ChooseFolder();
        _reloadBtn.Click += (_, _) => LoadFolderIfValid(_settings.LastFolder);
        _refreshDevicesBtn.Click += (_, _) => RefreshDevices();
        _stopAllBtn.Click += (_, _) => StopAll();
        _output1.SelectedIndexChanged += (_, _) => OutputsChanged();
        _output2.SelectedIndexChanged += (_, _) => OutputsChanged();

        DragEnter += HandleDragEnter;
        DragDrop += HandleDragDrop;
        _grid.AllowDrop = true;
        _grid.DragEnter += HandleDragEnter;
        _grid.DragDrop += HandleDragDrop;
        KeyDown += MainForm_KeyDown;
    }

    private void ApplyDarkTheme(Control root)
    {
        BackColor = Color.FromArgb(18, 22, 28);
        ForeColor = Color.White;
        _status.BackColor = Color.FromArgb(42, 47, 54);
        _status.ForeColor = Color.White;
        foreach (Control c in GetAllControls(root))
        {
            switch (c)
            {
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Color.FromArgb(90, 96, 104);
                    b.BackColor = Color.FromArgb(58, 63, 70);
                    b.ForeColor = Color.White;
                    break;
                case TextBox tb:
                    tb.BackColor = Color.FromArgb(58, 63, 70);
                    tb.ForeColor = Color.White;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox cb:
                    cb.BackColor = Color.FromArgb(58, 63, 70);
                    cb.ForeColor = Color.White;
                    cb.FlatStyle = FlatStyle.Flat;
                    break;
                case GroupBox gb:
                    gb.ForeColor = Color.White;
                    gb.BackColor = BackColor;
                    break;
                case FlowLayoutPanel fp:
                    fp.BackColor = BackColor;
                    break;
                case Label l when l != _status:
                    l.ForeColor = Color.White;
                    l.BackColor = Color.Transparent;
                    break;
            }
        }
    }

    private static IEnumerable<Control> GetAllControls(Control c)
    {
        foreach (Control child in c.Controls)
        {
            yield return child;
            foreach (var nested in GetAllControls(child)) yield return nested;
        }
    }

    private void ChooseFolder()
    {
        using var dlg = new FolderBrowserDialog { InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : null };
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath)) return;
        _settings.LastFolder = dlg.SelectedPath;
        SaveSettings();
        LoadFolderIfValid(dlg.SelectedPath);
    }

    private void LoadFolderIfValid(string? folder)
    {
        ClearTiles();
        if (string.IsNullOrWhiteSpace(folder))
        {
            _folderBox.Text = "";
            SetStatus("Choose a sound folder.");
            return;
        }

        _folderBox.Text = folder;
        if (!Directory.Exists(folder))
        {
            SetStatus("Saved folder not found. Choose a new folder.");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder)
                     .Where(f => AudioExts.Contains(Path.GetExtension(f)))
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            var volume = _settings.SoundVolumes.TryGetValue(fileName, out var v) ? Math.Clamp(v, 0, 100) : 100;
            var color = Palette[_tiles.Count % Palette.Length];
            if (_settings.SoundColors.TryGetValue(fileName, out var html))
            {
                try { color = ColorTranslator.FromHtml(html); } catch { }
            }

            var tile = new Tile(file, volume, color);
            if (_settings.SoundHotkeys.TryGetValue(fileName, out var hotkeyValue))
                tile.Hotkey = (Keys)hotkeyValue;
            tile.PlayRequested += (_, _) => PlayTile(tile);
            tile.StopRequested += (_, _) => StopSound(tile.FilePath);
            tile.VolumeCommitted += (_, value) =>
            {
                _settings.SoundVolumes[Path.GetFileName(tile.FilePath)] = value;
                SaveSettings();
            };
            tile.MenuRequested += (_, p) =>
            {
                _menuTile = tile;
                _clearHotkeyMenuItem.Visible = tile.Hotkey != Keys.None;
                _tileMenu.Show(tile, p);
            };
            _tiles.Add(tile);
            _grid.Controls.Add(tile);
        }

        ReflowGrid();
        SetStatus($"Loaded {_tiles.Count} sound(s).");
        SaveSettings();
    }

    private void ClearTiles()
    {
        foreach (var t in _tiles) t.Dispose();
        _tiles.Clear();
        _grid.Controls.Clear();
    }

    private void ReflowGrid()
    {
        if (_grid.ClientSize.Width <= 0) return;
        const int tileW = 170, gap = 12;
        // ClientSize already reflects the visible viewport for the FlowLayoutPanel.
        var viewportW = _grid.ClientSize.Width;
        var cols = Math.Max(1, viewportW / (tileW + gap));
        var total = cols * tileW + (cols - 1) * gap;
        var left = Math.Max(8, (viewportW - total) / 2);
        _grid.Padding = new Padding(left, 8, 8, 8);

        for (int i = 0; i < _tiles.Count; i++)
        {
            bool rowEnd = (i % cols) == cols - 1;
            _tiles[i].Margin = new Padding(0, 0, rowEnd ? 0 : gap, gap);
        }
    }

    private void RefreshDevices()
    {
        var prev1 = _settings.Output1Device;
        var prev2 = _settings.Output2Device;
        _devices.Clear();
        _devices.Add(new DeviceOption("(None)", null));
        _devices.Add(new DeviceOption("(Default Windows Output)", -1));
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            _devices.Add(new DeviceOption($"[{i}] {caps.ProductName}", i));
        }

        _output1.Items.Clear();
        _output2.Items.Clear();
        foreach (var d in _devices)
        {
            _output1.Items.Add(d);
            _output2.Items.Add(d);
        }

        SelectDevice(_output1, prev1);
        SelectDevice(_output2, prev2);
        EnsureDistinctOutputs();
        SaveOutputSelection();
    }

    private void RestoreOutputs()
    {
        SelectDevice(_output1, _settings.Output1Device);
        SelectDevice(_output2, _settings.Output2Device);
        EnsureDistinctOutputs();
        SaveOutputSelection();
    }

    private void SelectDevice(ComboBox combo, int? device)
    {
        var idx = _devices.FindIndex(d => d.DeviceNumber == device);
        combo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void OutputsChanged()
    {
        EnsureDistinctOutputs();
        SaveOutputSelection();
        var count = SelectedOutputs().Count;
        SetStatus(count == 0 ? "Select at least one output device." : $"Active outputs: {count}");
    }

    private void EnsureDistinctOutputs()
    {
        var d1 = (_output1.SelectedItem as DeviceOption)?.DeviceNumber;
        var d2 = (_output2.SelectedItem as DeviceOption)?.DeviceNumber;
        if (d1 is not null && d1 == d2) _output2.SelectedIndex = 0;
    }

    private void SaveOutputSelection()
    {
        _settings.Output1Device = (_output1.SelectedItem as DeviceOption)?.DeviceNumber;
        _settings.Output2Device = (_output2.SelectedItem as DeviceOption)?.DeviceNumber;
        SaveSettings();
    }

    private List<int> SelectedOutputs()
    {
        var list = new List<int>();
        foreach (var c in new[] { _output1, _output2 })
        {
            if ((c.SelectedItem as DeviceOption)?.DeviceNumber is int d && !list.Contains(d))
                list.Add(d);
        }
        return list;
    }

    private void PlayTile(Tile tile)
    {
        var outs = SelectedOutputs();
        if (outs.Count == 0) { SetStatus("Select at least one output device."); return; }
        foreach (var d in outs) StartPlayback(tile.FilePath, tile.VolumePercent / 100f, d);
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (ActiveControl is TextBoxBase || ActiveControl is ComboBox)
            return;

        var pressed = NormalizeHotkey(e.KeyData);
        if (pressed == Keys.None)
            return;

        var tile = _tiles.FirstOrDefault(t => NormalizeHotkey(t.Hotkey) == pressed);
        if (tile is null)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        PlayTile(tile);
        SetStatus($"Played {Path.GetFileNameWithoutExtension(tile.FilePath)} ({FormatHotkey(pressed)})");
    }

    private void StartPlayback(string path, float vol, int device)
    {
        try
        {
            var reader = new AudioFileReader(path) { Volume = vol };
            var output = new WaveOutEvent { DeviceNumber = device, DesiredLatency = 120 };
            var p = new Playback(path, output, reader);
            output.PlaybackStopped += (_, _) =>
            {
                if (!IsDisposed) BeginInvoke(new Action(() => RemovePlayback(p)));
            };
            lock (_playbackLock) _active.Add(p);
            output.Init(reader);
            output.Play();
        }
        catch (Exception ex)
        {
            SetStatus($"Playback failed: {ex.Message}");
        }
    }

    private void StopSound(string filePath)
    {
        List<Playback> matches;
        lock (_playbackLock)
            matches = _active.Where(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var p in matches)
        {
            try { p.Output.Stop(); } catch { }
        }

        if (matches.Count > 0)
            SetStatus($"Stopped {Path.GetFileNameWithoutExtension(filePath)}");
    }

    private void RemovePlayback(Playback p)
    {
        lock (_playbackLock) _active.Remove(p);
        p.Dispose();
    }

    private void StopAll()
    {
        List<Playback> snapshot;
        lock (_playbackLock) snapshot = _active.ToList();
        foreach (var p in snapshot) { try { p.Output.Stop(); } catch { } }
        lock (_playbackLock)
        {
            foreach (var p in _active.ToList()) p.Dispose();
            _active.Clear();
        }
        SetStatus("Stopped all sounds.");
    }

    private void RenameTile(Tile? tile)
    {
        if (tile is null) return;
        using var prompt = new RenamePrompt(Path.GetFileNameWithoutExtension(tile.FilePath));
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        var input = prompt.NewName.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;

        var oldPath = tile.FilePath;
        var oldFile = Path.GetFileName(oldPath);
        var ext = Path.GetExtension(oldPath);
        var newFile = Path.HasExtension(input) ? input : input + ext;
        var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newFile);

        try
        {
            if (!oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(newPath))
                {
                    MessageBox.Show(this, "A file with that name already exists.", "Rename Failed");
                    return;
                }
                File.Move(oldPath, newPath);
            }

            tile.SetFilePath(newPath);
            tile.SetDisplayName(Path.GetFileNameWithoutExtension(newPath));
            if (_settings.SoundVolumes.Remove(oldFile, out var vol)) _settings.SoundVolumes[newFile] = vol;
            if (_settings.SoundColors.Remove(oldFile, out var col)) _settings.SoundColors[newFile] = col;
            if (_settings.SoundHotkeys.Remove(oldFile, out var hotkey)) _settings.SoundHotkeys[newFile] = hotkey;
            SaveSettings();
            SetStatus($"Renamed to {newFile}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Rename Failed");
        }
    }

    private void ChangeTileColor(Tile? tile)
    {
        if (tile is null) return;
        using var dlg = new ColorDialog { Color = tile.TileColor, FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        tile.TileColor = dlg.Color;
        _settings.SoundColors[Path.GetFileName(tile.FilePath)] = ColorTranslator.ToHtml(dlg.Color);
        SaveSettings();
    }

    private void ResetTileColor(Tile? tile)
    {
        if (tile is null) return;
        var idx = _tiles.IndexOf(tile);
        if (idx < 0) return;
        tile.TileColor = Palette[idx % Palette.Length];
        _settings.SoundColors[Path.GetFileName(tile.FilePath)] = ColorTranslator.ToHtml(tile.TileColor);
        SaveSettings();
    }

    private void SetTileHotkey(Tile? tile)
    {
        if (tile is null) return;
        using var dlg = new HotkeyPrompt(tile.Hotkey);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var key = NormalizeHotkey(dlg.SelectedHotkey);
        if (key == Keys.None)
        {
            MessageBox.Show(this, "Choose a key (not just Ctrl/Alt/Shift).", "Invalid Hotkey");
            return;
        }

        var conflict = _tiles.FirstOrDefault(t => t != tile && NormalizeHotkey(t.Hotkey) == key);
        if (conflict is not null)
        {
            var result = MessageBox.Show(
                this,
                $"'{conflict.DisplayName}' is already using {FormatHotkey(key)}.\nReassign it to this sound?",
                "Hotkey Conflict",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;
            conflict.Hotkey = Keys.None;
            _settings.SoundHotkeys.Remove(Path.GetFileName(conflict.FilePath));
        }

        tile.Hotkey = key;
        _settings.SoundHotkeys[Path.GetFileName(tile.FilePath)] = (int)key;
        SaveSettings();
        SetStatus($"Hotkey set: {tile.DisplayName} = {FormatHotkey(key)}");
    }

    private void ClearTileHotkey(Tile? tile)
    {
        if (tile is null) return;
        tile.Hotkey = Keys.None;
        _settings.SoundHotkeys.Remove(Path.GetFileName(tile.FilePath));
        SaveSettings();
        SetStatus($"Hotkey cleared for {tile.DisplayName}");
    }

    private static Keys NormalizeHotkey(Keys keyData)
    {
        var keyCode = keyData & Keys.KeyCode;
        var mods = keyData & Keys.Modifiers;
        if (keyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.None)
            return Keys.None;
        return keyCode | mods;
    }

    private static string FormatHotkey(Keys key)
    {
        if (key == Keys.None) return "(None)";
        return new KeysConverter().ConvertToString(key) ?? key.ToString();
    }

    private void HandleDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void HandleDragDrop(object? sender, DragEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.LastFolder) || !Directory.Exists(_settings.LastFolder))
        {
            SetStatus("Choose a sound folder first, then drag files in.");
            return;
        }
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;

        int imported = 0, skipped = 0;
        foreach (var src in files)
        {
            if (!File.Exists(src) || !AudioExts.Contains(Path.GetExtension(src))) { skipped++; continue; }
            try
            {
                File.Copy(src, UniqueImportPath(_settings.LastFolder!, Path.GetFileName(src)));
                imported++;
            }
            catch { skipped++; }
        }

        if (imported > 0) LoadFolderIfValid(_settings.LastFolder);
        SetStatus(imported > 0 ? $"Imported {imported} file(s){(skipped > 0 ? $", skipped {skipped}" : "")}." : "No supported files imported.");
    }

    private static string UniqueImportPath(string folder, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var path = Path.Combine(folder, fileName);
        int i = 1;
        while (File.Exists(path)) path = Path.Combine(folder, $"{stem} ({i++}){ext}");
        return path;
    }

    private void SetStatus(string text) => _status.Text = text;

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                _settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath)) ?? new Settings();
        }
        catch { _settings = new Settings(); }
    }

    private void SaveSettings()
    {
        if (_loading) return;
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private sealed record DeviceOption(string Name, int? DeviceNumber)
    {
        public override string ToString() => Name;
    }

    private sealed class Playback(string filePath, WaveOutEvent output, AudioFileReader reader) : IDisposable
    {
        public string FilePath { get; } = filePath;
        public WaveOutEvent Output { get; } = output;
        public AudioFileReader Reader { get; } = reader;
        public void Dispose() { try { Output.Dispose(); } catch { } try { Reader.Dispose(); } catch { } }
    }

    private sealed class Settings
    {
        public string LastFolder { get; set; } = "";
        public int? Output1Device { get; set; }
        public int? Output2Device { get; set; }
        public Dictionary<string, int> SoundVolumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> SoundColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> SoundHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class Tile : Panel
{
    private readonly GoofyButton _play = new();
    private readonly Label _name = new();
    private readonly TrackBar _vol = new();
    private readonly Label _volLabel = new();
    public event EventHandler? PlayRequested;
    public event EventHandler? StopRequested;
    public event EventHandler<int>? VolumeCommitted;
    public event EventHandler<Point>? MenuRequested;

    public string FilePath { get; private set; }
    public int VolumePercent => _vol.Value;
    public string DisplayName => _name.Text;
    public Color TileColor { get => _play.ButtonColor; set => _play.ButtonColor = value; }
    public Keys Hotkey { get; set; }

    public Tile(string filePath, int volume, Color color)
    {
        FilePath = filePath;
        Width = 170;
        Height = 170;
        BackColor = Color.FromArgb(27, 34, 44);
        BorderStyle = BorderStyle.FixedSingle;

        _play.SetBounds(40, 8, 86, 86);
        _play.ButtonColor = color;

        _name.SetBounds(6, 96, 156, 30);
        _name.TextAlign = ContentAlignment.TopCenter;
        _name.ForeColor = Color.White;
        _name.Text = Path.GetFileNameWithoutExtension(filePath);

        _vol.SetBounds(4, 124, 160, 24);
        _vol.Minimum = 0;
        _vol.Maximum = 100;
        _vol.Value = Math.Clamp(volume, 0, 100);
        _vol.TickStyle = TickStyle.None;
        _vol.BackColor = BackColor;

        _volLabel.SetBounds(6, 148, 156, 18);
        _volLabel.TextAlign = ContentAlignment.MiddleCenter;
        _volLabel.ForeColor = Color.LightGray;
        _volLabel.Text = $"{_vol.Value}%";

        Controls.AddRange([_play, _name, _vol, _volLabel]);
        _play.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) PlayRequested?.Invoke(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Middle) StopRequested?.Invoke(this, EventArgs.Empty);
        };
        _name.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) PlayRequested?.Invoke(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Middle) StopRequested?.Invoke(this, EventArgs.Empty);
        };
        _vol.ValueChanged += (_, _) => _volLabel.Text = $"{_vol.Value}%";
        _vol.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) VolumeCommitted?.Invoke(this, _vol.Value); };

        foreach (Control c in Controls) c.MouseUp += ChildMouseUp;
        MouseUp += ChildMouseUp;
    }

    public void SetFilePath(string path) => FilePath = path;
    public void SetDisplayName(string name) => _name.Text = name;

    private void ChildMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var src = sender as Control ?? this;
        MenuRequested?.Invoke(this, PointToClient(src.PointToScreen(e.Location)));
    }
}

internal sealed class GoofyButton : Control
{
    private bool _pressed;
    private Color _buttonColor = Color.Red;

    public Color ButtonColor
    {
        get => _buttonColor;
        set { _buttonColor = value; Invalidate(); }
    }

    public GoofyButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Size = new Size(86, 86);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_pressed)
        {
            _pressed = false;
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Color.Transparent);

        var topOffset = _pressed ? 3f : 0f;
        var shadowShift = _pressed ? 2f : 4f;
        using var shadowBrush = new SolidBrush(Color.FromArgb(35, 8, 19, 31));
        e.Graphics.FillEllipse(shadowBrush, new RectangleF(10, 10 + shadowShift, Width - 18, Height - 18));

        var ringRect = new RectangleF(7, 7 + topOffset, Width - 16, Height - 16);
        using var ringBrush = new SolidBrush(_buttonColor.ToArgb() == Color.White.ToArgb() ? Color.Gray : Color.FromArgb(26, 26, 26));
        using var ringPen = new Pen(Color.Black, 1f);
        e.Graphics.FillEllipse(ringBrush, ringRect);
        e.Graphics.DrawEllipse(ringPen, ringRect);

        var faceRect = RectangleF.Inflate(ringRect, -4, -4);
        using var faceBrush = new SolidBrush(Shade(_buttonColor, _pressed ? 0.75f : 0.95f));
        using var facePen = new Pen(Shade(_buttonColor, 0.5f), 2f);
        e.Graphics.FillEllipse(faceBrush, faceRect);
        e.Graphics.DrawEllipse(facePen, faceRect);

        var topRect = new RectangleF(faceRect.X + 6, faceRect.Y + 6, faceRect.Width - 12, faceRect.Height - 18);
        using var topBrush = new SolidBrush(Shade(_buttonColor, _pressed ? 0.9f : 1.15f));
        e.Graphics.FillEllipse(topBrush, topRect);

        using var glossBrush = new SolidBrush(Color.FromArgb(110, Color.White));
        e.Graphics.FillPie(glossBrush, topRect.X + 4, topRect.Y, topRect.Width - 14, topRect.Height - 10, 25, 140);
        using var glossBrush2 = new SolidBrush(Color.FromArgb(55, Color.White));
        e.Graphics.FillEllipse(glossBrush2, new RectangleF(topRect.X + 8, topRect.Y + 5, topRect.Width - 28, topRect.Height - 26));
    }

    private static Color Shade(Color c, float factor)
    {
        static int Clamp(float v) => (int)Math.Max(0, Math.Min(255, v));
        return Color.FromArgb(c.A, Clamp(c.R * factor), Clamp(c.G * factor), Clamp(c.B * factor));
    }
}

internal sealed class RenamePrompt : Form
{
    private readonly TextBox _tb = new();
    public string NewName => _tb.Text;

    public RenamePrompt(string initial)
    {
        Text = "Rename Sound";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        Width = 420;
        Height = 135;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(layout);

        layout.Controls.Add(new Label { Text = "Enter a new file name:", AutoSize = true }, 0, 0);
        layout.SetColumnSpan(layout.Controls[^1], 2);
        _tb.Text = initial;
        _tb.Dock = DockStyle.Top;
        layout.Controls.Add(_tb, 0, 1);
        layout.SetColumnSpan(_tb, 2);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

internal sealed class HotkeyPrompt : Form
{
    private readonly Label _display = new();
    public Keys SelectedHotkey { get; private set; }

    public HotkeyPrompt(Keys current)
    {
        SelectedHotkey = current;
        Text = "Set Hotkey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        Width = 360;
        Height = 150;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        Controls.Add(layout);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "Press a key combination...", AutoSize = true }, 0, 0);
        _display.Dock = DockStyle.Fill;
        _display.TextAlign = ContentAlignment.MiddleCenter;
        _display.BorderStyle = BorderStyle.FixedSingle;
        _display.Text = current == Keys.None ? "(None)" : (new KeysConverter().ConvertToString(current) ?? current.ToString());
        layout.Controls.Add(_display, 0, 1);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, 2);
        AcceptButton = ok;
        CancelButton = cancel;

        KeyDown += OnHotkeyKeyDown;
        _display.KeyDown += OnHotkeyKeyDown;
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        var keyCode = e.KeyData & Keys.KeyCode;
        if (keyCode is Keys.Escape)
            return;
        if (keyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        SelectedHotkey = e.KeyData;
        _display.Text = new KeysConverter().ConvertToString(SelectedHotkey) ?? SelectedHotkey.ToString();
        e.SuppressKeyPress = true;
        e.Handled = true;
    }
}
