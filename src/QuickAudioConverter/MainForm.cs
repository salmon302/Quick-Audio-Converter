// SPDX-License-Identifier: MIT
namespace QuickAudioConverter;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuickAudioConverter.Engine;
using QuickAudioConverter.Ui;

/// <summary>
/// Primary application window. Hosts the ribbon-style navigation (Home / Convert / Effects /
/// Options), the action toolbar, the drag-and-drop ingestion zone, the output routing panel,
/// and the dynamic status bar. Conversion is performed by the WMF engine via AudioConversionService.
/// </summary>
public sealed class MainForm : Form
{
    private readonly Ribbon _ribbon = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly DropZone _dropZone = new();
    private readonly OutputPanel _outputPanel = new();
    private readonly List<string> _queue = new();

    public MainForm()
    {
        Text = "Quick Audio Converter";
        Width = 920;
        Height = 660;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 480);

        // Convert tab: drop zone fills, output panel pinned to the bottom.
        var convertTab = new Panel { Dock = DockStyle.Fill };
        _outputPanel.Dock = DockStyle.Bottom;
        _dropZone.Dock = DockStyle.Fill;
        convertTab.Controls.Add(_outputPanel);
        convertTab.Controls.Add(_dropZone);

        _ribbon.AddTab("Home", Placeholder("Home - dashboard (coming soon)"));
        _ribbon.AddTab("Convert", convertTab);
        _ribbon.AddTab("Effects", Placeholder("Effects - normalize, trim, fade (coming soon)"));
        _ribbon.AddTab("Options", Placeholder("Options - encoding profiles & shell integration (coming soon)"));

        _ribbon.AddToolbarButton("Add File(s)", (s, e) => AddFiles());
        _ribbon.AddToolbarButton("Remove", (s, e) => ClearQueue());
        _ribbon.AddToolbarButton("Play", (s, e) => Preview());
        _ribbon.AddToolbarButton("Convert", (s, e) => Convert());
        _ribbon.AddToolbarButton("Options", (s, e) => _ribbon.SelectTab("Options"));
        _ribbon.AddToolbarButton("Tags", (s, e) => ShowTags());

        _ribbon.TabSelected += (s, name) => UpdateStatus();
        _dropZone.FilesDropped += (s, paths) => Enqueue(paths);
        _outputPanel.BrowseClicked += (s, e) => BrowseFolder();
        _outputPanel.OpenFolderClicked += (s, e) => OpenFolder();

        _statusStrip.Dock = DockStyle.Bottom;
        _statusLabel.Text = "Ready";
        _statusStrip.Items.Add(_statusLabel);

        Controls.Add(_ribbon);
        Controls.Add(_statusStrip);

        _ribbon.SelectTab("Convert");
        UpdateStatus();
    }

    private static Panel Placeholder(string text)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
        p.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray
        });
        return p;
    }

    private void AddFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Audio files|*.m4a;*.mp3;*.wav;*.flac;*.aac;*.wma;*.ogg|All files|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            Enqueue(dlg.FileNames);
        }
    }

    private void Enqueue(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p) || File.Exists(p))
            {
                _queue.Add(p);
            }
        }

        _dropZone.SetStatus(_queue.Count);
        UpdateStatus();
    }

    private void ClearQueue()
    {
        _queue.Clear();
        _dropZone.SetStatus(0);
        UpdateStatus();
    }

    private void Preview()
    {
        if (_queue.Count == 0)
        {
            MessageBox.Show(this, "Add or drop files first.", "Quick Audio Converter");
            return;
        }

        MessageBox.Show(this, "Preview / playback is not wired in this scaffold iteration.", "Quick Audio Converter");
    }

    private async void Convert()
    {
        if (_queue.Count == 0)
        {
            MessageBox.Show(this, "Nothing to convert. Add files first.", "Quick Audio Converter");
            return;
        }

        var settings = new ConversionSettings
        {
            OutputFormat = _outputPanel.OutputFormat,
            BitrateKbps = 224,
            Channels = _outputPanel.Channels,
            SampleRate = 44100
        };
        var routing = new OutputRouting
        {
            SaveToSource = _outputPanel.SaveToSource,
            SaveToFolder = _outputPanel.SaveToFolder,
            CopySourceStructure = _outputPanel.CopySourceStructure
        };

        _statusLabel.Text = "Converting...";
        var service = new AudioConversionService(new WmfAudioEngine());
        var progress = new Progress<ConversionProgress>(p =>
            _statusLabel.Text = p.Total == 0 ? "Converting..." : $"Converting {p.Completed + 1}/{p.Total}: {p.CurrentFile}");

        ConversionReport report;
        try
        {
            report = await service.ConvertAsync(_queue.ToArray(), settings, routing, progress);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Conversion failed: {ex.Message}", "Quick Audio Converter");
            UpdateStatus();
            return;
        }

        var msg = $"Conversion complete.\nSucceeded: {report.Succeeded}\nFailed: {report.Failed}";
        if (report.Errors.Count > 0)
        {
            msg += "\n\n" + string.Join("\n", report.Errors.Take(8));
        }
        MessageBox.Show(this, msg, "Quick Audio Converter");
        UpdateStatus();
    }

    private void ShowTags()
    {
        MessageBox.Show(this, "Tag editor is not wired in this scaffold iteration.", "Quick Audio Converter");
    }

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Select output folder" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _outputPanel.SetSaveToFolder(dlg.SelectedPath);
            UpdateStatus();
        }
    }

    private void OpenFolder()
    {
        var path = _outputPanel.SaveToFolder;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            MessageBox.Show(this, "Output folder is not set or does not exist.", "Quick Audio Converter");
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private void UpdateStatus()
    {
        var format = string.IsNullOrWhiteSpace(_outputPanel.OutputFormat) ? "mp3" : _outputPanel.OutputFormat;
        var mode = _outputPanel.Channels == 1 ? "Mono" : "Stereo";
        var target = _outputPanel.SaveToSource
            ? "source folder"
            : (string.IsNullOrWhiteSpace(_outputPanel.SaveToFolder) ? "source folder" : _outputPanel.SaveToFolder);
        var queue = _queue.Count == 0 ? "no files" : $"{_queue.Count} file(s)";
        _statusLabel.Text = $"Convert to .{format} | CBR(224kbps)|High Quality|Mode({mode}) | -> {target} | {queue}";
    }
}
