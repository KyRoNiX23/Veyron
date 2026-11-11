
using System.ComponentModel;
using Veyron.Client.Themes;
using Veyron.Core.Enums;
using Veyron.Core.Models;
using Veyron.Infrastructure.Storage;
using Veyron.Network.Diagnostics;
using Veyron.Network.Selection;

namespace Veyron.Client.Forms;

public sealed class ServersForm : Form
{
    private readonly ProfileRepository repository = new();
    private readonly NetworkDiagnostics diagnostics = new();

    private readonly BindingList<ConnectionProfile> profiles = new();

    private readonly DataGridView grid = new();
    private readonly Label statusLabel = new();

    private readonly Button refreshButton = new();
    private readonly Button pingButton = new();
    private readonly Button connectButton = new();

    private readonly Veyron.Network.ConnectionManager connectionManager;

    private readonly ServerSelectionService selectionService = new();

    private readonly Button bestButton = new();
    private readonly Button testAllButton = new();

    public ServersForm(
        Veyron.Network.ConnectionManager connectionManager)
    {
        this.connectionManager = connectionManager;

        Text = "Veyron - Servers";
        Size = new Size(950, 600);
        MinimumSize = new Size(750, 450);

        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        Font = AppTheme.Font;

        BuildInterface();

        Load += async (_, _) => await LoadProfilesAsync();
    }

    private void BuildInterface()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 85,
            BackColor = AppTheme.Sidebar,
            Padding = new Padding(22)
        };

        var title = new Label
        {
            Text = "VPN Servers",
            Font = new Font("Segoe UI Semibold", 20),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(22, 15)
        };

        var subtitle = new Label
        {
            Text = "Manage your imported connection profiles",
            ForeColor = AppTheme.Secondary,
            AutoSize = true,
            Location = new Point(25, 52)
        };

        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = AppTheme.Card;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = AppTheme.Border;
        grid.ForeColor = AppTheme.Text;

        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.Sidebar;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = AppTheme.Text;
        grid.DefaultCellStyle.BackColor = AppTheme.Card;
        grid.DefaultCellStyle.ForeColor = AppTheme.Text;
        grid.DefaultCellStyle.SelectionBackColor = AppTheme.Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;

        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.ReadOnly = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        AddColumn("Name", "Name", "Server", 25);
        AddColumn("Country", "Country", "Country", 15);
        AddColumn("Host", "Host", "Address", 25);
        AddColumn("Type", "Type", "Protocol", 15);
        AddColumn("Ping", "Ping", "Ping (ms)", 10);

        grid.DataSource = profiles;

        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.Height = 32;
        statusLabel.Text = "Ready";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.ForeColor = AppTheme.Secondary;
        statusLabel.Padding = new Padding(10, 0, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 65,
            Padding = new Padding(12),
            BackColor = AppTheme.Sidebar
        };

        ConfigureButton(refreshButton, "Refresh");
        ConfigureButton(pingButton, "Test ping");
        ConfigureButton(connectButton, "Connect");
        ConfigureButton(testAllButton, "Test all");
        ConfigureButton(bestButton, "Best server");

        refreshButton.Click += async (_, _) => await LoadProfilesAsync();
        pingButton.Click += async (_, _) => await TestSelectedPingAsync();
        connectButton.Click += async (_, _) => await ConnectSelectedAsync();
        testAllButton.Click += async (_, _) => await TestAllServersAsync();
        bestButton.Click += (_, _) => SelectBestServer();

        toolbar.Controls.Add(refreshButton);
        toolbar.Controls.Add(pingButton);
        toolbar.Controls.Add(connectButton);
        toolbar.Controls.Add(testAllButton);
        toolbar.Controls.Add(bestButton);

        Controls.Add(grid);
        Controls.Add(statusLabel);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    private void AddColumn(
        string property,
        string header,
        string title,
        int width)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = property,
            HeaderText = title,
            DataPropertyName = property,
            FillWeight = width
        });
    }

    private void ConfigureButton(Button button, string text)
    {
        button.Text = text;
        button.Width = 125;
        button.Height = 38;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = AppTheme.Accent;
        button.ForeColor = Color.White;
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(5);
    }

    private async Task LoadProfilesAsync()
    {
        refreshButton.Enabled = false;
        statusLabel.Text = "Loading profiles...";

        try
        {
            var items = await repository.GetAllAsync();

            profiles.Clear();

            foreach (var profile in items)
                profiles.Add(profile);

            statusLabel.Text = $"Loaded profiles: {profiles.Count}";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Failed to load profiles.";

            MessageBox.Show(
                ex.Message,
                "Veyron",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            refreshButton.Enabled = true;
        }
    }

    private ConnectionProfile? GetSelectedProfile()
    {
        if (grid.CurrentRow?.DataBoundItem is ConnectionProfile profile)
            return profile;

        return null;
    }

    private async Task TestSelectedPingAsync()
    {
        var profile = GetSelectedProfile();

        if (profile == null)
        {
            MessageBox.Show("Select a server first.");
            return;
        }

        pingButton.Enabled = false;
        statusLabel.Text = $"Testing {profile.Host}...";

        try
        {
            long? ping = await diagnostics.PingAsync(profile.Host);

            profile.Ping = ping.HasValue ? (int)ping.Value : -1;

            await repository.UpdateAsync(profile);

            grid.Refresh();

            statusLabel.Text = ping.HasValue
                ? $"Response time: {ping.Value} ms"
                : "Host unavailable or ICMP blocked.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Ping failed.";
            MessageBox.Show(ex.Message);
        }
        finally
        {
            pingButton.Enabled = true;
        }
    }

    private async Task ConnectSelectedAsync()
    {
        var profile = GetSelectedProfile();

        if (profile == null)
        {
            MessageBox.Show("Select a server first.");
            return;
        }

        connectButton.Enabled = false;
        statusLabel.Text = $"Connecting to {profile.Name}...";

        try
        {
            await connectionManager.ConnectAsync(profile);

            statusLabel.Text = $"Connected: {profile.Name}";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Connection failed.";
            MessageBox.Show(
                ex.Message,
                "Veyron",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            connectButton.Enabled = true;
        }
    }

    private async Task TestAllServersAsync()
    {
        testAllButton.Enabled = false;
        bestButton.Enabled = false;

        statusLabel.Text = "Testing all servers...";

        try
        {
            var measured = await selectionService.MeasureAsync(profiles);

            profiles.Clear();

            foreach (var profile in measured)
            {
                profiles.Add(profile);
                await repository.UpdateAsync(profile);
            }

            statusLabel.Text = $"Test completed: {profiles.Count} profiles.";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Testing cancelled.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Testing failed.";
            MessageBox.Show(ex.Message, "Veyron");
        }
        finally
        {
            testAllButton.Enabled = true;
            bestButton.Enabled = true;
        }
    }

    private void SelectBestServer()
    {
        var best = selectionService.FindBest(profiles);

        if (best == null)
        {
            MessageBox.Show(
                "No available WireGuard server with a successful ping.",
                "Veyron",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is ConnectionProfile profile &&
                profile.Id == best.Id)
            {
                row.Selected = true;
                grid.CurrentCell = row.Cells[0];
                break;
            }
        }

        statusLabel.Text =
            $"Best server: {best.Name} ({best.Ping} ms)";
    }
}