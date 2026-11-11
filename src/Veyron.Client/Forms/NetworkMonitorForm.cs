
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Veyron.Client.Themes;
using Veyron.Network.Proxy;

namespace Veyron.Client.Forms;

public sealed class NetworkMonitorForm : Form
{
    private readonly LocalProxyServer proxyServer;
    private readonly System.Windows.Forms.Timer refreshTimer;

    private readonly Label activeLabel;
    private readonly Label uploadedLabel;
    private readonly Label downloadedLabel;
    private readonly Label uploadSpeedLabel;
    private readonly Label downloadSpeedLabel;

    private readonly DataGridView grid;

    private long previousUploaded;
    private long previousDownloaded;

    public NetworkMonitorForm(LocalProxyServer proxyServer)
    {
        this.proxyServer = proxyServer;

        Text = "Veyron — Network Monitor";
        Size = new Size(1050, 650);
        MinimumSize = new Size(800, 500);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        Font = AppTheme.MainFont;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AppTheme.Background
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Controls.Add(root);

        var title = new Label
        {
            Text = "NETWORK MONITOR",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 17F),
            ForeColor = AppTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };

        root.Controls.Add(title, 0, 0);

        var stats = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = AppTheme.Background
        };

        for (int i = 0; i < 3; i++)
            stats.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 33.33F));

        for (int i = 0; i < 2; i++)
            stats.RowStyles.Add(
                new RowStyle(SizeType.Percent, 50));

        root.Controls.Add(stats, 0, 1);

        activeLabel = CreateStatLabel("Active connections: 0");
        uploadedLabel = CreateStatLabel("Uploaded: 0 B");
        downloadedLabel = CreateStatLabel("Downloaded: 0 B");
        uploadSpeedLabel = CreateStatLabel("Upload speed: 0 B/s");
        downloadSpeedLabel = CreateStatLabel("Download speed: 0 B/s");

        stats.Controls.Add(activeLabel, 0, 0);
        stats.Controls.Add(uploadedLabel, 1, 0);
        stats.Controls.Add(downloadedLabel, 2, 0);
        stats.Controls.Add(uploadSpeedLabel, 0, 1);
        stats.Controls.Add(downloadSpeedLabel, 1, 1);

        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = AppTheme.Card,
            ForeColor = AppTheme.Text,
            GridColor = AppTheme.Border,
            BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false
        };

        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.Sidebar;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = AppTheme.Text;
        grid.DefaultCellStyle.BackColor = AppTheme.Card;
        grid.DefaultCellStyle.ForeColor = AppTheme.Text;
        grid.DefaultCellStyle.SelectionBackColor = AppTheme.Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;

        grid.Columns.Add("Destination", "Destination");
        grid.Columns.Add("Status", "Status");
        grid.Columns.Add("Started", "Started");
        grid.Columns.Add("Duration", "Duration");
        grid.Columns.Add("Uploaded", "Uploaded");
        grid.Columns.Add("Downloaded", "Downloaded");

        root.Controls.Add(grid, 0, 2);

        previousUploaded = proxyServer.TotalBytesSent;
        previousDownloaded = proxyServer.TotalBytesReceived;

        refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };

        refreshTimer.Tick += (_, _) => RefreshStatistics();

        Shown += (_, _) =>
        {
            RefreshStatistics();
            refreshTimer.Start();
        };

        FormClosed += (_, _) => refreshTimer.Stop();
    }

    private static Label CreateStatLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI Semibold", 10F),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8)
        };
    }

    private void RefreshStatistics()
    {
        if (IsDisposed)
            return;

        long uploaded = proxyServer.TotalBytesSent;
        long downloaded = proxyServer.TotalBytesReceived;

        long uploadSpeed = Math.Max(0, uploaded - previousUploaded);
        long downloadSpeed = Math.Max(0, downloaded - previousDownloaded);

        previousUploaded = uploaded;
        previousDownloaded = downloaded;

        activeLabel.Text =
            $"Active connections: {proxyServer.ActiveConnections}";

        uploadedLabel.Text =
            $"Uploaded: {TrafficFormatter.FormatBytes(uploaded)}";

        downloadedLabel.Text =
            $"Downloaded: {TrafficFormatter.FormatBytes(downloaded)}";

        uploadSpeedLabel.Text =
            $"Upload speed: {TrafficFormatter.FormatBytes(uploadSpeed)}/s";

        downloadSpeedLabel.Text =
            $"Download speed: {TrafficFormatter.FormatBytes(downloadSpeed)}/s";

        var connections = proxyServer.Connections
            .OrderByDescending(x => x.StartedAt)
            .ToArray();

        grid.Rows.Clear();

        foreach (var connection in connections)
        {
            grid.Rows.Add(
                connection.Destination,
                connection.Status,
                connection.StartedAt.ToLocalTime().ToString("HH:mm:ss"),
                connection.Duration.ToString(@"hh\:mm\:ss"),
                TrafficFormatter.FormatBytes(connection.BytesSent),
                TrafficFormatter.FormatBytes(connection.BytesReceived));
        }
    }
}
