
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Veyron.Client.Themes;
using Veyron.Core.Enums;
using Veyron.Core.Models;
using Veyron.Infrastructure.Storage;
using Veyron.Network;
using Veyron.Network.Browsers;
using Veyron.Network.Proxy;

namespace Veyron.Client.Forms;

public class MainForm : Form
{
    private Panel sidebar = null!;
    private Panel content = null!;
    private Label pageTitle = null!;
    private Label connectionStatus = null!;
    private Label selectedServer = null!;

    private ConnectionStatus currentStatus =
        ConnectionStatus.Disconnected;

    private Panel dashboardBody = null!;


    private readonly ConnectionManager connectionManager;
    private readonly ProfileRepository profileRepository = new();

    private Button connectButton = null!;
    private Button disconnectButton = null!;
    private ConnectionProfile? selectedProfile;

    private readonly BrowserLauncher browserLauncher = new();

    private Veyron.Network.Proxy.LocalProxyServer? localProxyServer;
    private Process? browserProcess;

    private RoutingProfile routingProfile = new()
    {
        Name = "Veyron Proxy",
        Mode = RoutingMode.GlobalProxy,
        DefaultAction = RoutingAction.Proxy
    };

    private void OpenTrafficRouting()
    {
        using var form = new TrafficRoutingForm(routingProfile);

        if (form.ShowDialog(this) != DialogResult.OK)
            return;

        MessageBox.Show(
            $"Routing profile updated.\nRules: {routingProfile.Rules.Count}",
            "Veyron Traffic Routing",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    public MainForm(ConnectionManager connectionManager)
    {
        this.connectionManager = connectionManager;

        InitializeInterface();

        connectionManager.StatusChanged += OnConnectionStatusChanged;
    }

    private void OnConnectionStatusChanged(ConnectionStatus status)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(() => OnConnectionStatusChanged(status));
            return;
        }

        // Здесь обновим индикатор состояния на Dashboard.
        UpdateConnectionStatus(status);
    }

    private void UpdateConnectionStatus(ConnectionStatus status)
    {
        currentStatus = status;

        connectionStatus.Text = status switch
        {
            ConnectionStatus.Disconnected => "Disconnected",
            ConnectionStatus.Connecting => "Connecting...",
            ConnectionStatus.Connected => "Connected",
            ConnectionStatus.Disconnecting => "Disconnecting...",
            ConnectionStatus.Reconnecting => "Reconnecting...",
            ConnectionStatus.Failed => "Connection failed",
            _ => "Unknown"
        };

        connectionStatus.ForeColor = status switch
        {
            ConnectionStatus.Connected => AppTheme.Success,
            ConnectionStatus.Connecting => AppTheme.Accent,
            ConnectionStatus.Disconnected => AppTheme.Secondary,
            ConnectionStatus.Failed => Color.IndianRed,
            _ => AppTheme.Text
        };

        connectButton.Enabled =
    (status == ConnectionStatus.Disconnected ||
     status == ConnectionStatus.Failed) &&
    selectedProfile != null;

        disconnectButton.Enabled =
            status == ConnectionStatus.Connected ||
            status == ConnectionStatus.Failed;

        if (status == ConnectionStatus.Disconnected &&
            selectedServer != null)
        {
            selectedServer.Text = "No server selected";
        }
    }

    private void InitializeInterface()
    {
        Text = "Veyron VPN";
        Size = new Size(1180, 760);
        MinimumSize = new Size(950, 650);

        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        Font = AppTheme.MainFont;

        FormBorderStyle = FormBorderStyle.Sizable;

        BuildContent();
        BuildSidebar();

    }

    private void BuildSidebar()
    {
        sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 220,
            BackColor = AppTheme.Sidebar,
            Padding = new Padding(18, 20, 18, 15)
        };

        Controls.Add(sidebar);

        Label logo = new Label
        {
            Text = "VEYRON",
            Font = new Font("Segoe UI Semibold", 22F),
            ForeColor = AppTheme.Accent,
            AutoSize = true,
            Location = new Point(18, 25)
        };

        sidebar.Controls.Add(logo);

        Label subtitle = CreateLabel(
            "SECURE NETWORK",
            18, 62, 9,
            AppTheme.Secondary);

        sidebar.Controls.Add(subtitle);

        Panel separator = new Panel
        {
            BackColor = AppTheme.Border,
            Location = new Point(18, 100),
            Size = new Size(184, 1)
        };

        sidebar.Controls.Add(separator);

        string[] items =
        {
            "Dashboard",
            "Servers",
            "Profiles",
            "Providers",
            "Diagnostics",
            "Network Monitor",
            "Traffic Routing",
            "Settings",
        };

        int y = 125;

        foreach (string item in items)
        {
            Button button = CreateNavigationButton(item, y);


            button.Click += (s, e) =>
            {
                pageTitle.Text = item;

                switch (item)
                {
                    case "Dashboard":
                        BuildDashboard();
                        break;

                    case "Providers":
                        using (var form = new ProvidersForm())
                            form.ShowDialog(this);
                        break;

                    case "Profiles":
                        using (var form = new ProfilesForm())
                            form.ShowDialog(this);
                        break;
                    case "Diagnostics":
                        using (var form = new DiagnosticsForm())
                            form.ShowDialog(this);
                        break;
                    case "Servers":
                        OpenServersPage();
                        break;
                    case "Network Monitor":
                        OpenNetworkMonitor();
                        break;
                    case "Traffic Routing":
                        OpenTrafficRouting();
                        break;
                    default:
                        ShowPlaceholder(item);
                        break;
                }
            };

            sidebar.Controls.Add(button);

            y += 52;
        }

        Label version = CreateLabel(
            "VEYRON VPN  •  v0.1.0",
            18, 690, 8,
            AppTheme.Secondary);

        sidebar.Controls.Add(version);
    }

    private void BuildContent()
    {
        content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            Padding = new Padding(30)
        };

        Controls.Add(content);

        Panel header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 65
        };

        content.Controls.Add(header);

        pageTitle = new Label
        {
            Text = "Dashboard",
            Font = AppTheme.TitleFont,
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(0, 5)
        };

        header.Controls.Add(pageTitle);

        Label profile = new Label
        {
            Text = "LOCAL USER",
            ForeColor = AppTheme.Secondary,
            Font = new Font("Segoe UI", 9F),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(700, 15)
        };

        header.Controls.Add(profile);

        dashboardBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            Padding = new Padding(0, 15, 0, 0)
        };

        content.Controls.Add(dashboardBody);

        BuildDashboard();
    }

    private void BuildDashboard()
    {
        Panel body = dashboardBody;

        if (body == null)
            return;

        body.Controls.Clear();

        TableLayoutPanel layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent
        };

        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 58));

        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 42));

        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 52));

        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 48));

        body.Controls.Add(layout);

        Panel connectionCard = CreateCard();

        connectionCard.Margin = new Padding(0, 0, 12, 12);

        layout.Controls.Add(connectionCard, 0, 0);
        layout.SetColumnSpan(connectionCard, 2);

        BuildConnectionCard(connectionCard);

        Panel serverCard = CreateCard();

        serverCard.Margin = new Padding(0, 0, 12, 0);

        layout.Controls.Add(serverCard, 0, 1);

        BuildServerCard(serverCard);

        Panel providerCard = CreateCard();

        providerCard.Margin = new Padding(0, 0, 0, 0);

        layout.Controls.Add(providerCard, 1, 1);

        BuildProviderCard(providerCard);
    }

    private void BuildConnectionCard(Panel card)
    {
        card.Controls.Add(CreateLabel(
            "CONNECTION STATUS",
            22, 20, 9,
            AppTheme.Secondary));

        connectionStatus = new Label
        {
            Text = "Disconnected",
            Font = new Font("Segoe UI Semibold", 26F),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(22, 58)
        };

        card.Controls.Add(connectionStatus);

        Label description = CreateLabel(
            "Manage your secure network connection.",
            24, 105, 10,
            AppTheme.Secondary);

        card.Controls.Add(description);

        connectButton = CreateButton(
            "Connect",
            22, 165, 150, 45,
            AppTheme.Accent);

        connectButton.Click += async (s, e) =>
        {
            await ConnectSelectedProfileAsync();

        };



        card.Controls.Add(connectButton);

        disconnectButton = CreateButton(
            "Disconnect",
            185, 165, 150, 45,
            AppTheme.CardHover);

        disconnectButton.Enabled = false;

        disconnectButton.Click += async (s, e) =>
        {
            await DisconnectAsync();
        };

        card.Controls.Add(disconnectButton);

        Button browserButton = CreateButton(
    "Open browser",
    348, 165, 150, 45,
    AppTheme.CardHover);

        browserButton.Click += (_, _) =>
        {
            OpenBrowser();
        };

        card.Controls.Add(browserButton);

        UpdateConnectionStatus(connectionManager.Status);
    }

    private void BuildServerCard(Panel card)
    {
        card.Controls.Add(CreateLabel(
            "SELECTED SERVER",
            20, 20, 9,
            AppTheme.Secondary));

        selectedServer = new Label
        {
            Text = "No server selected",
            Font = new Font("Segoe UI Semibold", 16F),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(20, 55)
        };

        card.Controls.Add(selectedServer);

        Label location = CreateLabel(
            "Choose a connection profile",
            20, 90, 9,
            AppTheme.Secondary);

        card.Controls.Add(location);

        Button selectButton = CreateButton(
            "Browse servers",
            20, 130, 145, 38,
            AppTheme.CardHover);

        selectButton.Click += (s, e) =>
        {
            pageTitle.Text = "Servers";
            OpenServersPage();
        };

        card.Controls.Add(selectButton);
    }

    private void BuildProviderCard(Panel card)
    {
        card.Controls.Add(CreateLabel(
            "PROVIDERS",
            20, 20, 9,
            AppTheme.Secondary));

        AddProviderRow(
            card, "WireGuard", "Not configured", 60);

        AddProviderRow(
            card, "OpenVPN", "Not configured", 105);

        AddProviderRow(
            card, "SOCKS5", "Not configured", 150);

        AddProviderRow(
            card, "Traffic Routing", $"{routingProfile.Mode} • {routingProfile.Rules.Count} rules", 195);
    }

    private void AddProviderRow(
        Panel parent,
        string name,
        string status,
        int y)
    {
        Label title = CreateLabel(
            name, 20, y, 11, AppTheme.Text);

        Label description = CreateLabel(
            status, 20, y + 20, 8,
            AppTheme.Secondary);

        parent.Controls.Add(title);
        parent.Controls.Add(description);
    }

    private void ShowPlaceholder(string name)
    {
        Panel body = dashboardBody;

        if (body == null)
            return;

        body.Controls.Clear();

        Label message = new Label
        {
            Text = name + "\n\nThis module is under development.",
            Font = AppTheme.MainFont,
            ForeColor = AppTheme.Secondary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        body.Controls.Add(message);
    }

    private Panel CreateCard()
    {
        return new Panel
        {
            BackColor = AppTheme.Card,
            Dock = DockStyle.Fill,
            Padding = new Padding(15)
        };
    }

    private Label CreateLabel(
        string text,
        int x,
        int y,
        float size,
        Color color)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = color,
            Font = new Font("Segoe UI", size)
        };
    }

    private Button CreateButton(
        string text,
        int x,
        int y,
        int width,
        int height,
        Color color)
    {
        Button button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = color,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9F),
            Cursor = Cursors.Hand
        };

        button.FlatAppearance.BorderSize = 0;

        return button;
    }

    private Button CreateNavigationButton(
        string text,
        int y)
    {
        Button button = CreateButton(
            text,
            10, y, 180, 42,
            AppTheme.Sidebar);

        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(12, 0, 0, 0);

        button.FlatAppearance.MouseOverBackColor =
            AppTheme.CardHover;

        return button;
    }

    private async Task ConnectSelectedProfileAsync()
    {
        try
        {
            ConnectionProfile? profile = selectedProfile;

            if (profile == null)
            {
                var profiles = await profileRepository.GetAllAsync();

                profile = profiles.FirstOrDefault(p =>
    p.Type == ConnectionType.WireGuard ||
    p.Type == ConnectionType.HTTP ||
    p.Type == ConnectionType.SOCKS5);
            }

            if (profile == null)
            {
                MessageBox.Show("No supported connection profiles found.");
                return;
            }

            selectedProfile = profile;

            await connectionManager.ConnectAsync(profile);

            selectedServer.Text =
                $"{profile.Name} ({profile.Type})";
            selectedProfile = profile;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Connection error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UpdateConnectionStatus(connectionManager.Status);
        }
    }


    private async Task DisconnectAsync()
    {
        disconnectButton.Enabled = false;

        try
        {
            await connectionManager.DisconnectAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Disconnect error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UpdateConnectionStatus(connectionManager.Status);
        }
    }

    protected override async void OnFormClosed(FormClosedEventArgs e)
    {
        connectionManager.StatusChanged -= OnConnectionStatusChanged;

        await StopLocalProxyAsync();
        base.OnFormClosed(e);
    }



    private async void OpenServersPage()
    {
        pageTitle.Text = "Servers";
        dashboardBody.Controls.Clear();
        selectedProfile = null;

        List<ConnectionProfile> allProfiles;

        try
        {
            allProfiles = await profileRepository.GetAllAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Loading error");
            return;
        }

        // Main layout
        TableLayoutPanel root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = AppTheme.Background
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));

        dashboardBody.Controls.Add(root);

        // Filters
        FlowLayoutPanel filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 5),
            BackColor = AppTheme.Background
        };

        TextBox searchBox = new TextBox
        {
            Width = 190,
            Height = 30,
            Font = AppTheme.MainFont,
            BackColor = AppTheme.Card,
            ForeColor = AppTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Search servers..."
        };

        ComboBox protocolBox = new ComboBox
        {
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AppTheme.Card,
            ForeColor = AppTheme.Text
        };

        protocolBox.Items.Add("All protocols");

        foreach (var protocol in Enum.GetNames(typeof(ConnectionType)))
            protocolBox.Items.Add(protocol);

        protocolBox.SelectedIndex = 0;

        ComboBox countryBox = new ComboBox
        {
            Width = 130,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AppTheme.Card,
            ForeColor = AppTheme.Text
        };

        countryBox.Items.Add("All countries");

        foreach (string country in allProfiles
            .Select(p => p.Country)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c))
        {
            countryBox.Items.Add(country);
        }

        countryBox.SelectedIndex = 0;

        ComboBox sortBox = new ComboBox
        {
            Width = 110,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AppTheme.Card,
            ForeColor = AppTheme.Text
        };

        sortBox.Items.AddRange(new object[]
        {
        "Name",
        "Ping"
        });

        sortBox.SelectedIndex = 0;

        CheckBox favoritesOnly = new CheckBox
        {
            Text = "Favorites",
            AutoSize = true,
            ForeColor = AppTheme.Text,
            Margin = new Padding(8, 8, 0, 0)
        };

        filters.Controls.Add(searchBox);
        filters.Controls.Add(protocolBox);
        filters.Controls.Add(countryBox);
        filters.Controls.Add(sortBox);
        filters.Controls.Add(favoritesOnly);

        root.Controls.Add(filters, 0, 0);

        // Servers grid
        DataGridView grid = new DataGridView
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

        grid.Columns.Add("Id", "ID");
        grid.Columns.Add("Favorite", "Favorite");
        grid.Columns.Add("Name", "Server");
        grid.Columns.Add("Country", "Country");
        grid.Columns.Add("Protocol", "Protocol");
        grid.Columns.Add("Host", "Host");
        grid.Columns.Add("Port", "Port");
        grid.Columns.Add("Ping", "Ping");

        grid.Columns["Id"]!.Visible = false;

        root.Controls.Add(grid, 0, 1);

        // Footer
        Panel footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background
        };

        root.Controls.Add(footer, 0, 2);

        Label selectedLabel = new Label
        {
            Text = "No server selected",
            Location = new Point(0, 20),
            AutoSize = true,
            ForeColor = AppTheme.Secondary
        };

        footer.Controls.Add(selectedLabel);

        Button favoriteButton = CreateButton(
            "Toggle favorite",
            300, 10, 130, 40,
            AppTheme.CardHover);

        Button connectButton = CreateButton(
            "Connect",
            445, 10, 130, 40,
            AppTheme.Accent);

        Button testAllButton = CreateButton(
            "Test all",
            445 + 155, 10, 130, 40,
            AppTheme.CardHover);

        connectButton.Enabled = false;

        footer.Controls.Add(favoriteButton);
        footer.Controls.Add(connectButton);
        footer.Controls.Add(testAllButton);

        // Selection
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.CurrentRow == null)
                return;

            string? id = grid.CurrentRow.Cells["Id"].Value?.ToString();

            selectedProfile = allProfiles.FirstOrDefault(
                p => p.Id == id);

            if (selectedProfile == null)
                return;

            selectedLabel.Text =
                $"{selectedProfile.Name} | " +
                $"{selectedProfile.Host}:{selectedProfile.Port}";

            favoriteButton.Enabled = true;

            connectButton.Enabled =
                (selectedProfile.Type == ConnectionType.WireGuard ||
    selectedProfile.Type == ConnectionType.HTTP ||
    selectedProfile.Type == ConnectionType.HTTPS ||
    selectedProfile.Type == ConnectionType.SOCKS4 ||
    selectedProfile.Type == ConnectionType.SOCKS5) &&
                connectionManager.Status == ConnectionStatus.Disconnected;
        };

        // Filtering and sorting
        void ApplyFilters()
        {
            string search = searchBox.Text.Trim().ToLowerInvariant();
            string protocol = protocolBox.SelectedItem?.ToString() ?? "All protocols";
            string country = countryBox.SelectedItem?.ToString() ?? "All countries";
            string sort = sortBox.SelectedItem?.ToString() ?? "Name";

            var query = allProfiles.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p =>
                    (p.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (p.Country ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (p.Host ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            if (protocol != "All protocols")
            {
                query = query.Where(p => p.Type.ToString() == protocol);
            }

            if (country != "All countries")
            {
                query = query.Where(p => p.Country == country);
            }

            if (favoritesOnly.Checked)
            {
                query = query.Where(p => p.IsFavorite);
            }

            query = sort == "Ping"
                ? query.OrderBy(p => p.Ping < 0 ? int.MaxValue : p.Ping)
                : query.OrderBy(p => p.Name);

            grid.Rows.Clear();
            selectedProfile = null;

            selectedLabel.Text = "No server selected";
            connectButton.Enabled = false;
            favoriteButton.Enabled = false;

            foreach (var profile in query)
            {
                grid.Rows.Add(
                    profile.Id,
                    profile.IsFavorite ? "★" : "☆",
                    profile.Name,
                    profile.Country,
                    profile.Type,
                    profile.Host,
                    profile.Port,
                    profile.Ping < 0
                        ? "Unknown"
                        : $"{profile.Ping} ms");
            }
        }

        searchBox.TextChanged += (_, _) => ApplyFilters();
        protocolBox.SelectedIndexChanged += (_, _) => ApplyFilters();
        countryBox.SelectedIndexChanged += (_, _) => ApplyFilters();
        sortBox.SelectedIndexChanged += (_, _) => ApplyFilters();
        favoritesOnly.CheckedChanged += (_, _) => ApplyFilters();

        // Favorite
        favoriteButton.Click += async (_, _) =>
        {
            if (selectedProfile == null)
                return;

            try
            {
                selectedProfile.IsFavorite = !selectedProfile.IsFavorite;

                await profileRepository.UpdateAsync(selectedProfile);






                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Favorite error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        // Connect
        connectButton.Click += async (_, _) =>
        {
            if (selectedProfile == null)
                return;

            try
            {
                await connectionManager.ConnectAsync(selectedProfile);

                selectedServer.Text =
                    $"{selectedProfile.Name} ({selectedProfile.Type})";

                BuildDashboard();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Connection error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                UpdateConnectionStatus(connectionManager.Status);
            }
        };

        testAllButton.Click += async (_, _) =>
        {
            if (allProfiles.Count == 0)
                return;

            testAllButton.Enabled = false;
            connectButton.Enabled = false;

            const int maxThreads = 30;

            using var semaphore = new SemaphoreSlim(maxThreads);
            using var cancellation = new CancellationTokenSource();

            int completed = 0;
            int successful = 0;
            int failed = 0;

            var results = new System.Collections.Concurrent.ConcurrentDictionary<
                string, int>();

            try
            {
                var tasks = allProfiles.Select(async profile =>
                {
                    await semaphore.WaitAsync(cancellation.Token);

                    try
                    {
                        int pingValue = -1;

                        if (!string.IsNullOrWhiteSpace(profile.Host))
                        {
                            using var ping = new System.Net.NetworkInformation.Ping();

                            try
                            {
                                var addresses = await System.Net.Dns.GetHostAddressesAsync(profile.Host);

                                if (addresses.Length > 0)
                                {
                                    var reply = await ping.SendPingAsync(
                                        addresses[0],
                                        4000);

                                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                                    {
                                        pingValue = (int)reply.RoundtripTime;
                                        Interlocked.Increment(ref successful);
                                    }
                                    else
                                    {
                                        Interlocked.Increment(ref failed);
                                    }
                                }
                                else
                                {
                                    Interlocked.Increment(ref failed);
                                }

                            }
                            catch
                            {
                                Interlocked.Increment(ref failed);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref failed);
                        }

                        results[profile.Id] = pingValue;
                    }
                    finally
                    {
                        semaphore.Release();

                        int count = System.Threading.Interlocked.Increment(ref completed);

                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke(() =>
                            {
                                testAllButton.Text =
                                    $"Testing {count}/{allProfiles.Count}";
                            });
                        }
                    }
                }).ToArray();

                await Task.WhenAll(tasks);

                foreach (var profile in allProfiles)
                {
                    if (results.TryGetValue(profile.Id, out int pingValue))
                    {
                        profile.Ping = pingValue;
                        await profileRepository.UpdateAsync(profile);
                    }
                }

                ApplyFilters();

                MessageBox.Show(
                    $"Testing completed!\n\n" +
                    $"Total: {completed}\n" +
                    $"Success: {successful}\n" +
                    $"Failed: {failed}",
                    "Veyron Diagnostics",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Testing error");
            }
            finally
            {
                testAllButton.Enabled = true;
                testAllButton.Text = "Test all";
                UpdateConnectionStatus(currentStatus);
            }
        };

        ApplyFilters();
    }



    private void OpenBrowser()
    {
        if (selectedProfile == null)
        {
            MessageBox.Show(
                "Select a connection profile first.",
                "Veyron Browser",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        if (selectedProfile.Type is not (
            ConnectionType.HTTP or
            ConnectionType.HTTPS or
            ConnectionType.SOCKS4 or
            ConnectionType.SOCKS5))
        {
            MessageBox.Show(
                $"Browser does not support {selectedProfile.Type} profiles.",
                "Veyron Browser",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        using Form browserForm = new Form
        {
            Text = "Veyron Browser",
            Size = new Size(400, 310),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = AppTheme.Background,
            ForeColor = AppTheme.Text,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        Label title = CreateLabel(
            "BROWSER CONFIGURATION",
            20, 20, 10, AppTheme.Secondary);

        browserForm.Controls.Add(title);

        Label server = CreateLabel(
            $"{selectedProfile.Host}:{selectedProfile.Port}",
            20, 55, 11, AppTheme.Text);

        browserForm.Controls.Add(server);

        ComboBox browserBox = new ComboBox
        {
            Location = new Point(20, 90),
            Width = 340,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AppTheme.Card,
            ForeColor = AppTheme.Text
        };

        browserBox.Items.Add("Chrome");
        browserBox.Items.Add("Edge");
        browserBox.SelectedIndex = 0;

        browserForm.Controls.Add(browserBox);

        TextBox urlBox = new TextBox
        {
            Location = new Point(20, 130),
            Width = 340,
            Text = "https://example.com",
            BackColor = AppTheme.Card,
            ForeColor = AppTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };

        browserForm.Controls.Add(urlBox);

        Button dpiButton = CreateButton(
            "DPI Bypass settings",
            20, 175, 165, 38,
            AppTheme.CardHover);

        dpiButton.Click += (_, _) =>
        {
            using var settingsForm = new DpiSettingsForm();
            settingsForm.ShowDialog(browserForm);
        };

        browserForm.Controls.Add(dpiButton);

        Button openButton = CreateButton(
            "Launch browser",
            195, 175, 165, 38,
            AppTheme.Accent);

        openButton.Click += async (_, _) =>
        {
            if (browserBox.SelectedItem == null)
                return;

            BrowserType browser = Enum.Parse<BrowserType>(
    browserBox.SelectedItem.ToString()!,
    ignoreCase: true);

            string url = urlBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(url))
                url = "https://example.com";

            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "For this version, use an HTTPS address.",
                    "Veyron Browser",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            openButton.Enabled = false;

            try
            {
                await LaunchBrowserWithProxyAsync(browser, url);
            }
            finally
            {
                openButton.Enabled = true;
            }
        };

        browserForm.Controls.Add(openButton);

        browserForm.ShowDialog(this);
    }

    private void BrowserProcess_Exited(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, browserProcess))
            return;

        _ = StopLocalProxyAsync();
    }

    private async Task StopLocalProxyAsync()
    {
        LocalProxyServer? server = localProxyServer;
        localProxyServer = null;

        Process? process = browserProcess;
        browserProcess = null;

        if (process != null)
        {
            process.Exited -= BrowserProcess_Exited;
            process.Dispose();
        }

        if (server != null)
            await server.DisposeAsync();
    }


    private async Task LaunchBrowserWithProxyAsync(
        BrowserType browser,
        string? url)
    {
        ConnectionProfile? profile = selectedProfile;

        if (profile == null)
        {
            MessageBox.Show("Select a profile first.");
            return;
        }

        if (browserProcess != null && !browserProcess.HasExited)
        {
            MessageBox.Show("A browser session is already running.");
            return;
        }

        await StopLocalProxyAsync();

        try
        {
            localProxyServer = new LocalProxyServer(
                profile,
                2080,
                routingProfile);

            await localProxyServer.StartAsync();

            string localAddress =
                $"http://127.0.0.1:{localProxyServer.Port}";

            browserProcess = browserLauncher.Start(
                browser,
                profile,
                url,
                localAddress);

            Debug.WriteLine(
                $"[Veyron] Browser launched: {localAddress}");

            Debug.WriteLine(
                $"[Veyron] Proxy running: {localProxyServer.IsRunning}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Veyron] Browser error: {ex}");

            await StopLocalProxyAsync();

            MessageBox.Show(
                ex.ToString(),
                "Veyron Browser",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }


    private void OpenNetworkMonitor()
    {
        if (localProxyServer == null ||
            !localProxyServer.IsRunning)
        {
            MessageBox.Show(
                "Start a browser session through Veyron first.",
                "Network Monitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        using var monitor = new NetworkMonitorForm(localProxyServer);
        monitor.ShowDialog(this);
    }

    private async Task WaitForLocalProxyAsync(int port)
    {
        Exception? lastError = null;

        for (int i = 0; i < 20; i++)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();

                await client.ConnectAsync(
                    System.Net.IPAddress.Loopback,
                    port);

                return;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                lastError = ex;
                await Task.Delay(100);
            }
        }

        throw new InvalidOperationException(
            $"Local proxy 127.0.0.1:{port} is not accepting connections.",
            lastError);
    }
}
