// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Ui;

using System;
using System.Collections.Generic;
using System.Windows.Forms;

/// <summary>
/// Central drag-and-drop ingestion zone. Accepts files or directories dropped from the shell
/// and raises <see cref="FilesDropped"/> with the absolute paths.
/// </summary>
public sealed class DropZone : Panel
{
    private readonly Label _hint;

    public event EventHandler<IReadOnlyList<string>>? FilesDropped;

    public DropZone()
    {
        Dock = DockStyle.Fill;
        AllowDrop = true;
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Color.WhiteSmoke;

        _hint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray,
            Text = "Drag and drop audio files or folders here"
        };
        Controls.Add(_hint);

        DragEnter += (s, e) =>
        {
            if (e.Data is not null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                BackColor = Color.LightBlue;
            }
        };
        DragLeave += (s, e) => BackColor = Color.WhiteSmoke;
        DragDrop += (s, e) =>
        {
            BackColor = Color.WhiteSmoke;
            if (e.Data is not null && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                FilesDropped?.Invoke(this, paths);
            }
        };
    }

    public void SetStatus(int fileCount)
    {
        _hint.Text = fileCount == 0
            ? "Drag and drop audio files or folders here"
            : $"{fileCount} file(s) queued \u2014 ready to convert";
    }
}
