// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Ui;

using System;
using System.Windows.Forms;
using QuickAudioConverter.Engine;

/// <summary>
/// Options tab: configures the default encoding profile used by the Explorer context-menu conversion
/// (REQ-SHELL-01 reads these "last-used" settings) and toggles the shell integration on/off.
/// </summary>
public sealed class OptionsPanel : UserControl
{
    private readonly AppSettings _settings;
    private readonly ComboBox _format;
    private readonly ComboBox _bitrate;
    private readonly ComboBox _quality;
    private readonly ComboBox _mode;
    private readonly TextBox _saveFolder;
    private readonly CheckBox _copyStructure;
    private readonly CheckBox _shell;
    private readonly Button _installAll;
    private readonly Button _uninstallAll;
    private readonly Label _modernStatus;
    private readonly Label _status;

    public event EventHandler<bool>? ShellIntegrationToggled;
    public event EventHandler? InstallAllUsersClicked;
    public event EventHandler? UninstallAllUsersClicked;

    public OptionsPanel(AppSettings settings)
    {
        _settings = settings;
        Dock = DockStyle.Fill;
        Padding = new Padding(12);
        BackColor = SystemColors.Window;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 7,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowCount = 10;

        void Row(int r, string label, Control c)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right }, 0, r);
            layout.Controls.Add(c, 1, r);
        }

        _format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        _format.Items.AddRange(new object[] { "mp3", "m4a", "wav", "flac", "aac" });
        Row(0, "Default output format:", _format);

        _bitrate = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        _bitrate.Items.AddRange(new object[] { "128 kbps", "192 kbps", "224 kbps", "256 kbps", "320 kbps" });
        Row(1, "Bitrate:", _bitrate);

        _quality = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        _quality.Items.AddRange(new object[] { "High", "Standard", "Fast" });
        Row(2, "Quality:", _quality);

        _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        _mode.Items.AddRange(new object[] { "Stereo", "Mono" });
        Row(3, "Mode:", _mode);

        _saveFolder = new TextBox { Width = 280 };
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (s, e) => Browse();
        var folderRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        folderRow.Controls.Add(_saveFolder);
        folderRow.Controls.Add(browse);
        Row(4, "Default save folder:", folderRow);

        _copyStructure = new CheckBox { Text = "Copy source folder structure", AutoSize = true };
        Row(5, "", _copyStructure);

        _shell = new CheckBox { Text = "Enable Explorer context-menu integration (Convert to <format>)", AutoSize = true };
        _shell.CheckedChanged += (s, e) => ShellIntegrationToggled?.Invoke(this, _shell.Checked);
        Row(6, "", _shell);

        _installAll = new Button { Text = "Install Windows 11 top-level menu (all users, administrator)…", AutoSize = true };
        _installAll.Click += (s, e) => InstallAllUsersClicked?.Invoke(this, EventArgs.Empty);
        Row(7, "", _installAll);

        _uninstallAll = new Button { Text = "Uninstall all-users integration", AutoSize = true };
        _uninstallAll.Click += (s, e) => UninstallAllUsersClicked?.Invoke(this, EventArgs.Empty);
        Row(8, "", _uninstallAll);

        _modernStatus = new Label { AutoSize = true, ForeColor = Color.Gray };
        Row(9, "Top-level menu:", _modernStatus);

        Controls.Add(layout);

        _status = new Label { Dock = DockStyle.Bottom, Height = 40, ForeColor = Color.Gray };
        Controls.Add(_status);

        LoadFromSettings();
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog { Description = "Default output folder for context-menu conversions" };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _saveFolder.Text = dlg.SelectedPath;
            Persist();
        }
    }

    private void LoadFromSettings()
    {
        _format.Text = _settings.OutputFormat;
        _bitrate.SelectedIndex = IndexOfBitrate(_settings.BitrateKbps);
        _quality.SelectedIndex = _settings.Quality switch { 2 => 0, 5 => 1, 7 => 2, _ => 0 };
        _mode.SelectedIndex = _settings.Channels == 1 ? 1 : 0;
        _saveFolder.Text = _settings.DefaultSaveFolder ?? "";
        _copyStructure.Checked = _settings.CopySourceStructure;
        _shell.Checked = _settings.ShellIntegrationEnabled;
        RefreshModernStatus();
    }

    /// <summary>Writes the current panel values into the shared settings object and saves to disk.</summary>
    public void Persist()
    {
        _settings.OutputFormat = _format.Text;
        _settings.BitrateKbps = int.Parse(_bitrate.Text.Replace(" kbps", ""), System.Globalization.CultureInfo.InvariantCulture);
        _settings.Quality = _quality.SelectedIndex switch { 0 => 2, 1 => 5, 2 => 7, _ => 2 };
        _settings.Channels = _mode.SelectedIndex == 1 ? 1 : 2;
        _settings.DefaultSaveFolder = string.IsNullOrWhiteSpace(_saveFolder.Text) ? null : _saveFolder.Text.Trim();
        _settings.CopySourceStructure = _copyStructure.Checked;
        _settings.Save();
    }

    public void SetStatus(string text) => _status.Text = text;

    public void SetModernStatus(string text) => _modernStatus.Text = text;

    private void RefreshModernStatus()
    {
        if (_settings.ModernShellInstalled)
            _modernStatus.Text = "Installed (" +
                (_settings.ShellInstallScope == "AllUsers" ? "all users + sparse package" : "per user") + ").";
        else
            _modernStatus.Text = "Not installed.";
    }

    public void RefreshFromSettings()
    {
        LoadFromSettings();
        _shell.Checked = _settings.ShellIntegrationEnabled;
    }

    private static int IndexOfBitrate(int kbps) => kbps switch
    {
        128 => 0, 192 => 1, 224 => 2, 256 => 3, 320 => 4, _ => 2
    };
}
