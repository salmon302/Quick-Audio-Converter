// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Ui;

using System;
using System.Windows.Forms;

/// <summary>
/// Output routing and configuration section: "Save to folder", "Output Format", "Browse",
/// "Open Output Folder", "Mode" (Mono/Stereo), plus the boolean toggles
/// "Save to source file folder" and "Copy source folder structure". Also exposes the encoding
/// profile (bitrate + LAME quality) so the user can configure global output parameters (REQ-GUI-01).
/// </summary>
public sealed class OutputPanel : UserControl
{
    private readonly TextBox _saveToFolder;
    private readonly ComboBox _outputFormat;
    private readonly ComboBox _mode;
    private readonly ComboBox _bitrate;
    private readonly ComboBox _quality;
    private readonly CheckBox _saveToSource;
    private readonly CheckBox _copyStructure;

    public event EventHandler? BrowseClicked;
    public event EventHandler? OpenFolderClicked;

    public string SaveToFolder => _saveToSource.Checked ? string.Empty : _saveToFolder.Text.Trim();
    public string OutputFormat => _outputFormat.Text;
    public bool SaveToSource => _saveToSource.Checked;
    public bool CopySourceStructure => _copyStructure.Checked;
    public int Channels => _mode.SelectedIndex == 1 ? 1 : 2; // 1 = mono, 2 = stereo
    public int BitrateKbps => int.Parse(_bitrate.Text.Replace(" kbps", ""), System.Globalization.CultureInfo.InvariantCulture);
    public int Quality => _quality.SelectedIndex switch { 0 => 2, 1 => 5, 2 => 7, _ => 2 }; // LAME quality

    public OutputPanel()
    {
        Dock = DockStyle.Bottom;
        Height = 210;
        Padding = new Padding(10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int r = 0; r < 6; r++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "Save to folder:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 0);
        _saveToFolder = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 320 };
        layout.Controls.Add(_saveToFolder, 1, 0);
        var browse = new Button { Text = "Browse...", AutoSize = true, Anchor = AnchorStyles.Left };
        browse.Click += (s, e) => BrowseClicked?.Invoke(this, e);
        layout.Controls.Add(browse, 2, 0);

        layout.Controls.Add(new Label { Text = "Output Format:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 1);
        _outputFormat = new ComboBox { Anchor = AnchorStyles.Left, DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _outputFormat.Items.AddRange(new object[] { "mp3", "m4a", "wav", "flac", "aac" });
        _outputFormat.SelectedIndex = 0;
        layout.Controls.Add(_outputFormat, 1, 1);
        var openFolder = new Button { Text = "Open Output Folder", AutoSize = true, Anchor = AnchorStyles.Left };
        openFolder.Click += (s, e) => OpenFolderClicked?.Invoke(this, e);
        layout.Controls.Add(openFolder, 2, 1);

        layout.Controls.Add(new Label { Text = "Mode:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 2);
        _mode = new ComboBox { Anchor = AnchorStyles.Left, DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _mode.Items.AddRange(new object[] { "Stereo", "Mono" });
        _mode.SelectedIndex = 0;
        layout.Controls.Add(_mode, 1, 2);

        layout.Controls.Add(new Label { Text = "Bitrate:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 3);
        _bitrate = new ComboBox { Anchor = AnchorStyles.Left, DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _bitrate.Items.AddRange(new object[] { "128 kbps", "192 kbps", "224 kbps", "256 kbps", "320 kbps" });
        _bitrate.SelectedIndex = 2;
        layout.Controls.Add(_bitrate, 1, 3);

        layout.Controls.Add(new Label { Text = "Quality:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 4);
        _quality = new ComboBox { Anchor = AnchorStyles.Left, DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _quality.Items.AddRange(new object[] { "High", "Standard", "Fast" });
        _quality.SelectedIndex = 0;
        layout.Controls.Add(_quality, 1, 4);

        _saveToSource = new CheckBox { Text = "Save to source file folder", AutoSize = true, Anchor = AnchorStyles.Left };
        _saveToSource.CheckedChanged += (s, e) => _saveToFolder.Enabled = !_saveToSource.Checked;
        layout.Controls.Add(_saveToSource, 1, 5);
        _copyStructure = new CheckBox { Text = "Copy source folder structure", AutoSize = true, Anchor = AnchorStyles.Left };
        layout.Controls.Add(_copyStructure, 2, 5);

        Controls.Add(layout);
    }

    public void SetSaveToFolder(string path) => _saveToFolder.Text = path;
}
