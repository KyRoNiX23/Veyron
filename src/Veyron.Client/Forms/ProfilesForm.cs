
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Veyron.Core.Enums;
using Veyron.Core.Models;
using Veyron.Infrastructure.Import;
using Veyron.Infrastructure.Storage;
using Veyron.Network.Diagnostics;

namespace Veyron.Client.Forms;

public class ProfilesForm : Form
{
    private readonly ProfileRepository repository = new();
    private readonly DataGridView grid = new();

    private readonly Button importButton = new();
    private readonly Button removeButton = new();
    private readonly Button refreshButton = new();
    private readonly Button editButton = new();
    private readonly Button testButton = new();
    private readonly Button testAllButton = new();
    private readonly Button proxyListButton = new();
    private readonly Button deleteProfilesButton = new();

    private readonly NetworkDiagnostics diagnostics = new();
    private readonly ProfileImporter importer = new();
    private readonly ProxyListImporter proxyImporter = new();

    private readonly HttpProxyDiagnostics httpDiagnostics = new();
    private readonly SocksProxyDiagnostics socksDiagnostics = new();

    private const int MaxConcurrentPings = 1000;

    private readonly SemaphoreSlim pingSemaphore =
        new(MaxConcurrentPings);

    private readonly List<ProfileRow> rows = new();

    private readonly Dictionary<string, int> rowIndexes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Color> pingColors =
        new(StringComparer.OrdinalIgnoreCase);

    public ProfilesForm()
    {
        Text = "Veyron - Profiles";
        Size = new Size(1010, 520);
        MinimumSize = new Size(750, 400);

        StartPosition = FormStartPosition.CenterParent;

        BackColor = Color.FromArgb(15, 16, 24);
        ForeColor = Color.White;

        InitializeInterface();

        Load += async (_, _) => await RefreshDataAsync();
    }

    private void InitializeInterface()
    {
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 65,
            BackColor = Color.FromArgb(20, 21, 31)
        };

        ConfigureButton(importButton, "Import profile", 15, 15, 130);
        ConfigureButton(removeButton, "Remove", 155, 15, 100);
        ConfigureButton(refreshButton, "Refresh", 265, 15, 100);
        ConfigureButton(editButton, "Edit", 375, 15, 100);
        ConfigureButton(testButton, "Test ping", 485, 15, 110);
        ConfigureButton(testAllButton, "Test all", 605, 15, 110);
        ConfigureButton(proxyListButton, "Import proxy list", 725, 15, 120);
        ConfigureButton(deleteProfilesButton, "Delete profiles", 855, 15, 120);

        importButton.Click += async (_, _) =>
            await OpenImportFormAsync();

        removeButton.Click += async (_, _) =>
            await RemoveProfileAsync();

        refreshButton.Click += async (_, _) =>
            await RefreshDataAsync();

        editButton.Click += async (_, _) =>
            await EditProfileAsync();

        testButton.Click += async (_, _) =>
            await TestSelectedPingAsync();

        testAllButton.Click += async (_, _) =>
            await TestAllPingsAsync();

        proxyListButton.Click += async (_, _) =>
            await ImportProxyListAsync();

        deleteProfilesButton.Click += async (_, _) =>
        {
            using var form = new DeleteProfilesForm();

            form.ShowDialog(this);

            await RefreshDataAsync();
        };

        toolbar.Controls.AddRange(new Control[]
        {
            importButton,
            removeButton,
            refreshButton,
            editButton,
            testButton,
            testAllButton,
            proxyListButton,
            deleteProfilesButton
        });

        ConfigureGrid();

        Controls.Add(grid);
        Controls.Add(toolbar);
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;

        grid.BackgroundColor = Color.FromArgb(25, 27, 40);
        grid.ForeColor = Color.White;

        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;

        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;

        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;

        grid.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;

        grid.ScrollBars = ScrollBars.Both;
        grid.RowTemplate.Height = 30;

        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 36;

        grid.ColumnHeadersDefaultCellStyle.BackColor =
            Color.FromArgb(35, 36, 52);

        grid.ColumnHeadersDefaultCellStyle.ForeColor =
            Color.White;

        grid.DefaultCellStyle.BackColor =
            Color.FromArgb(25, 27, 40);

        grid.DefaultCellStyle.ForeColor = Color.White;

        grid.DefaultCellStyle.SelectionBackColor =
            Color.FromArgb(90, 65, 160);

        grid.DefaultCellStyle.SelectionForeColor = Color.White;

        grid.GridColor = Color.FromArgb(45, 47, 65);

        // VirtualMode
        grid.VirtualMode = true;
        grid.CellValueNeeded += Grid_CellValueNeeded;
        grid.CellFormatting += Grid_CellFormatting;

        grid.Columns.Add(CreateColumn("Id", "Id", false));
        grid.Columns.Add(CreateColumn("Name", "Name"));
        grid.Columns.Add(CreateColumn("Protocol", "Protocol"));
        grid.Columns.Add(CreateColumn("Host", "Host"));
        grid.Columns.Add(CreateColumn("Port", "Port"));
        grid.Columns.Add(CreateColumn("Ping", "Ping"));
        grid.Columns.Add(CreateColumn("Imported", "Imported"));

        grid.Columns["Name"]!.Width = 190;
        grid.Columns["Protocol"]!.Width = 100;
        grid.Columns["Host"]!.Width = 170;
        grid.Columns["Port"]!.Width = 75;
        grid.Columns["Ping"]!.Width = 110;

        grid.Columns["Imported"]!.AutoSizeMode =
            DataGridViewAutoSizeColumnMode.Fill;

        grid.RowCount = 0;
    }

    private DataGridViewTextBoxColumn CreateColumn(
        string header,
        string property,
        bool visible = true)
    {
        return new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Name = property,
            Visible = visible,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
    }

    private void Grid_CellValueNeeded(
        object? sender,
        DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= rows.Count)
            return;

        var item = rows[e.RowIndex];

        e.Value = grid.Columns[e.ColumnIndex].Name switch
        {
            "Id" => item.Id,
            "Name" => item.Name,
            "Protocol" => item.Protocol,
            "Host" => item.Host,
            "Port" => item.Port,
            "Ping" => item.Ping,
            "Imported" => item.Imported,
            _ => null
        };
    }

    private void Grid_CellFormatting(
        object? sender,
        DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= rows.Count)
            return;

        if (grid.Columns[e.ColumnIndex].Name != "Ping")
            return;

        string id = rows[e.RowIndex].Id;

        if (pingColors.TryGetValue(id, out var color))
        {
            e.CellStyle.BackColor = color;
            e.CellStyle.ForeColor = Color.White;
        }
    }

    private void ConfigureButton(
        Button button,
        string text,
        int x,
        int y,
        int width)
    {
        button.Text = text;
        button.Location = new Point(x, y);
        button.Size = new Size(width, 35);

        button.BackColor = Color.FromArgb(110, 80, 200);
        button.ForeColor = Color.White;

        button.FlatStyle = FlatStyle.Flat;
        button.Cursor = Cursors.Hand;
    }

    private ProfileRow? GetSelectedProfile()
    {
        int index = grid.CurrentRow?.Index ?? -1;

        if (index < 0 || index >= rows.Count)
            return null;

        return rows[index];
    }

    private async Task OpenImportFormAsync()
    {
        using var form = new ImportProfileForm();

        if (form.ShowDialog(this) == DialogResult.OK)
            await RefreshDataAsync();
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            importButton.Enabled = false;
            refreshButton.Enabled = false;

            var profiles = await repository.GetAllAsync();

            var newRows = profiles.Select(profile => new ProfileRow
            {
                Id = profile.Id,
                Name = profile.Name,
                Protocol = profile.Type.ToString(),
                Host = profile.Host,
                Port = profile.Port,

                Ping = profile.Ping < 0
                    ? "Not tested"
                    : $"{profile.Ping} ms",

                Imported = profile.ImportedAt
                    .ToLocalTime()
                    .ToString("g")
            }).ToList();

            grid.SuspendLayout();

            try
            {
                rows.Clear();
                rows.AddRange(newRows);

                rowIndexes.Clear();
                pingColors.Clear();

                for (int i = 0; i < rows.Count; i++)
                {
                    rowIndexes[rows[i].Id] = i;

                    pingColors[rows[i].Id] =
                        rows[i].Ping == "Not tested"
                            ? Color.FromArgb(25, 27, 40)
                            : Color.FromArgb(25, 27, 40);
                }

                grid.RowCount = rows.Count;
                grid.ClearSelection();
                grid.Invalidate();
            }
            finally
            {
                grid.ResumeLayout();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Veyron - Profiles",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            importButton.Enabled = true;
            refreshButton.Enabled = true;
        }
    }

    private async Task RemoveProfileAsync()
    {
        var selected = GetSelectedProfile();

        if (selected == null)
            return;

        var result = MessageBox.Show(
            $"Remove profile '{selected.Name}'?",
            "Veyron",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        try
        {
            await repository.RemoveAsync(selected.Id);
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Veyron",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task EditProfileAsync()
    {
        var selected = GetSelectedProfile();

        if (selected == null)
            return;

        try
        {
            var profiles = await repository.GetAllAsync();

            var profile = profiles.FirstOrDefault(
                x => x.Id == selected.Id);

            if (profile == null)
            {
                MessageBox.Show("Profile not found.");
                await RefreshDataAsync();
                return;
            }

            using var form = new EditProfileForm(profile);

            if (form.ShowDialog(this) == DialogResult.OK)
                await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Veyron",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task TestSelectedPingAsync()
    {
        var selected = GetSelectedProfile();

        if (selected == null)
        {
            MessageBox.Show("Select a server first.");
            return;
        }

        testButton.Enabled = false;

        try
        {
            var profiles = await repository.GetAllAsync();

            var profile = profiles.FirstOrDefault(
                x => x.Id == selected.Id);

            if (profile == null)
                return;

            switch (profile.Type)
            {
                case ConnectionType.HTTP:
                case ConnectionType.HTTPS:
                    await TestHttpProxyAsync(profile);
                    return;

                case ConnectionType.SOCKS4:
                case ConnectionType.SOCKS5:
                    await TestSocksProxyAsync(profile);
                    return;

                default:
                    SetPingCellColor(
                        profile.Id,
                        Color.DarkGoldenrod);

                    long? ping = await diagnostics.PingAsync(
                        profile.Host);

                    profile.Ping = ping.HasValue
                        ? (int)ping.Value
                        : -1;

                    await repository.UpdateAsync(profile);

                    UpdatePingInGrid(profile.Id, profile.Ping);

                    SetPingCellColor(
                        profile.Id,
                        ping.HasValue
                            ? Color.ForestGreen
                            : Color.Firebrick);

                    return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Ping error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            testButton.Enabled = true;
        }
    }

    private async Task TestAllPingsAsync()
    {
        testAllButton.Enabled = false;
        testButton.Enabled = false;

        try
        {
            var profiles = await repository.GetAllAsync();

            if (profiles.Count == 0)
            {
                MessageBox.Show("No profiles available.");
                return;
            }

            int completed = 0;
            int successful = 0;
            int failed = 0;

            int total = profiles.Count;

            var tasks = profiles.Select(async profile =>
            {
                await pingSemaphore.WaitAsync();

                try
                {
                    SetPingCellColor(
                        profile.Id,
                        Color.DarkGoldenrod);

                    switch (profile.Type)
                    {
                        case ConnectionType.HTTP:
                        case ConnectionType.HTTPS:
                            {
                                var result = await httpDiagnostics.TestAsync(
                                    profile,
                                    "https://example.com/");

                                profile.Ping = result.Success
                                    ? (int)result.ResponseTime
                                    : -1;

                                break;
                            }

                        case ConnectionType.SOCKS4:
                        case ConnectionType.SOCKS5:
                            {
                                var result = await socksDiagnostics.TestAsync(
                                    profile,
                                    "youtube.com",
                                    443);

                                profile.Ping = result.Success
                                    ? (int)result.ResponseTime
                                    : -1;

                                break;
                            }

                        default:
                            {
                                long? ping = await diagnostics.PingAsync(
                                    profile.Host);

                                profile.Ping = ping.HasValue
                                    ? (int)ping.Value
                                    : -1;

                                break;
                            }
                    }

                    if (profile.Ping >= 0)
                        Interlocked.Increment(ref successful);
                    else
                        Interlocked.Increment(ref failed);
                }
                catch
                {
                    profile.Ping = -1;
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    UpdatePingInGrid(
                        profile.Id,
                        profile.Ping);

                    SetPingCellColor(
                        profile.Id,
                        profile.Ping >= 0
                            ? Color.ForestGreen
                            : Color.Firebrick);

                    Interlocked.Increment(ref completed);

                    pingSemaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            foreach (var profile in profiles)
                await repository.UpdateAsync(profile);

            await RefreshDataAsync();

            MessageBox.Show(
                $"Ping test completed!\n\n" +
                $"Total: {total}\n" +
                $"Completed: {completed}\n" +
                $"Successful: {successful}\n" +
                $"Failed: {failed}",
                "Veyron diagnostics",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Ping error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            testAllButton.Enabled = true;
            testButton.Enabled = true;
        }
    }

    private async Task ImportProxyListAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "CSV proxy list (*.csv)|*.csv",
            Title = "Import proxy list"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var profiles = await proxyImporter.ImportAsync(
                dialog.FileName);

            int imported = 0;

            foreach (var profile in profiles)
            {
                await repository.AddAsync(profile);
                imported++;
            }

            await RefreshDataAsync();

            MessageBox.Show(
                $"Successfully imported {imported} proxy profiles.",
                "Veyron",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Proxy import error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void UpdatePingInGrid(string profileId, int ping)
    {
        if (grid.IsDisposed || !grid.IsHandleCreated)
            return;

        if (grid.InvokeRequired)
        {
            grid.BeginInvoke(new Action(() =>
                UpdatePingInGrid(profileId, ping)));

            return;
        }

        if (!rowIndexes.TryGetValue(profileId, out int index))
            return;

        if (index < 0 || index >= rows.Count)
            return;

        rows[index].Ping = ping < 0
            ? "Timeout"
            : $"{ping} ms";

        grid.InvalidateCell(
            grid.Columns["Ping"]!.Index,
            index);
    }

    private void SetPingCellColor(string profileId, Color color)
    {
        if (grid.IsDisposed || !grid.IsHandleCreated)
            return;

        if (grid.InvokeRequired)
        {
            grid.BeginInvoke(new Action(() =>
                SetPingCellColor(profileId, color)));

            return;
        }

        if (!rowIndexes.TryGetValue(profileId, out int index))
            return;

        if (index < 0 || index >= rows.Count)
            return;

        pingColors[profileId] = color;

        grid.InvalidateCell(
            grid.Columns["Ping"]!.Index,
            index);
    }

    private async Task TestHttpProxyAsync(ConnectionProfile profile)
    {
        SetPingCellColor(profile.Id, Color.DarkGoldenrod);

        var result = await httpDiagnostics.TestAsync(
            profile,
            "https://example.com/");

        profile.Ping = result.Success
            ? (int)result.ResponseTime
            : -1;

        await repository.UpdateAsync(profile);

        UpdatePingInGrid(profile.Id, profile.Ping);

        SetPingCellColor(
            profile.Id,
            result.Success
                ? Color.ForestGreen
                : Color.Firebrick);

        if (!result.Success)
        {
            MessageBox.Show(
                $"HTTP proxy test failed.\n\n" +
                $"Error: {result.Error}\n\n" +
                $"Response:\n{result.ProxyResponse}",
                "Proxy diagnostics",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else
        {
            MessageBox.Show(
                $"HTTPS tunnel established!\n\n" +
                $"Status: {result.StatusCode}\n" +
                $"Response: {result.StatusDescription}\n" +
                $"Time: {result.ResponseTime} ms",
                "Proxy diagnostics",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private async Task TestSocksProxyAsync(ConnectionProfile profile)
    {
        SetPingCellColor(profile.Id, Color.DarkGoldenrod);

        var result = await socksDiagnostics.TestAsync(
            profile,
            "example.com",
            443);

        profile.Ping = result.Success
            ? (int)result.ResponseTime
            : -1;

        await repository.UpdateAsync(profile);

        UpdatePingInGrid(profile.Id, profile.Ping);

        SetPingCellColor(
            profile.Id,
            result.Success
                ? Color.ForestGreen
                : Color.Firebrick);

        if (result.Success)
        {
            MessageBox.Show(
                $"SOCKS proxy works!\n\n" +
                $"Protocol: {profile.Type}\n" +
                $"HTTP: {result.HttpStatusDescription}\n" +
                $"Response time: {result.ResponseTime} ms",
                "SOCKS diagnostics",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(
                $"SOCKS proxy test failed.\n\n" +
                $"Stage: {result.Stage}\n" +
                $"Error: {result.Error}",
                "SOCKS diagnostics",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private sealed class ProfileRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Imported { get; set; } = "";
        public string Ping { get; set; } = "Not tested";
    }
}
