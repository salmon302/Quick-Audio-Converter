// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Ui;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

/// <summary>
/// Lightweight ribbon-style navigation control: a top tab strip (Home / Convert / Effects /
/// Options), a toolbar row beneath it, and a content host that swaps the active tab's control.
/// Built from native WinForms controls to avoid third-party ribbon dependencies.
/// </summary>
public sealed class Ribbon : UserControl
{
    private readonly FlowLayoutPanel _tabStrip;
    private readonly ToolStrip _toolbar;
    private readonly Panel _contentHost;
    private readonly Dictionary<string, Control> _tabs = new();

    public event EventHandler<string>? TabSelected;

    public Ribbon()
    {
        Dock = DockStyle.Fill;
        BackColor = SystemColors.Control;

        _tabStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.FromArgb(0x2D, 0x2D, 0x2D)
        };

        _toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = SystemColors.Control,
            RenderMode = ToolStripRenderMode.System
        };

        _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Window };

        Controls.Add(_contentHost);
        Controls.Add(_toolbar);
        Controls.Add(_tabStrip);
    }

    public void AddTab(string name, Control content)
    {
        var btn = new Button
        {
            Text = name,
            Tag = name,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(0x2D, 0x2D, 0x2D),
            Width = 96,
            Height = 26,
            Margin = new Padding(4, 2, 0, 2)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += (s, e) => SelectTab(name);
        _tabStrip.Controls.Add(btn);
        _tabs[name] = content;
    }

    public void AddToolbarButton(string text, EventHandler handler)
    {
        var btn = new ToolStripButton(text);
        btn.Click += handler;
        _toolbar.Items.Add(btn);
    }

    public void SelectTab(string name)
    {
        if (!_tabs.TryGetValue(name, out var content))
        {
            return;
        }

        _contentHost.Controls.Clear();
        _contentHost.Controls.Add(content);

        foreach (Button b in _tabStrip.Controls)
        {
            bool selected = string.Equals((string?)b.Tag, name, StringComparison.Ordinal);
            b.BackColor = selected ? Color.FromArgb(0x1B, 0x1B, 0x1B) : Color.FromArgb(0x2D, 0x2D, 0x2D);
        }

        TabSelected?.Invoke(this, name);
    }
}
