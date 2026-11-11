
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Veyron.Client.Themes;
using Veyron.Core.Enums;
using Veyron.Infrastructure.Storage;
using Veyron.Network.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace Veyron.Client.Forms;

public sealed class DiagnosticsForm : Form
{
    private readonly ProfileRepository repository = new();

    private Label interfaceStatus = null!;
    private Label handshakeStatus = null!;
    private Label pingStatus = null!;
    private Label profileStatus = null!;
    private Button runButton = null!;

    public DiagnosticsForm()
    {
        InitializeInterface();
    }

    private void InitializeInterface()
    {
        Text = "Veyron - Diagnostics";
        Size = new Size(650, 480);
        MinimumSize = new Size(550, 400);
        StartPosition = FormStartPosition.CenterParent;

        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        Font = AppTheme.MainFont;

        Label title = new Label
        {
            Text = "NETWORK DIAGNOSTICS",
            Font = new Font("Segoe UI Semibold", 17F),
            ForeColor = AppTheme.Text,
            Location = new Point(25, 25),
            AutoSize = true
        };

        Controls.Add(title);

        Panel card = new Panel
        {
            Location = new Point(25, 85),
            Size = new Size(580, 240),
            Anchor = AnchorStyles.Top |
                     AnchorStyles.Left |
                     AnchorStyles.Right,
            BackColor = AppTheme.Card,
            Padding = new Padding(20)
        };

        Controls.Add(card);

        interfaceStatus = AddStatus(
            card, "WireGuard interface", 25);

        handshakeStatus = AddStatus(
            card, "Latest handshake", 75);

        pingStatus = AddStatus(
            card, "Network latency", 125);

        profileStatus = AddStatus(
            card, "Selected profile", 175);

        runButton = new Button
        {
            Text = "Run diagnostics",
            Location = new Point(25, 350),
            Size = new Size(180, 45),
            BackColor = AppTheme.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9F)
        };

        runButton.FlatAppearance.BorderSize = 0;
        runButton.Click += async (_, _) => await RunDiagnosticsAsync();

        Controls.Add(runButton);
    }

    private Label AddStatus(
        Panel parent,
        string name,
        int y)
    {
        Label label = new Label
        {
            Text = name + ": Not checked",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = AppTheme.Secondary,
            Font = new Font("Segoe UI", 10F)
        };

        parent.Controls.Add(label);

        return label;
    }

    private async Task RunDiagnosticsAsync()
    {
        runButton.Enabled = false;
        runButton.Text = "Checking...";

        interfaceStatus.Text = "WireGuard interface: Checking...";
        handshakeStatus.Text = "Latest handshake: Checking...";
        pingStatus.Text = "Network latency: Checking...";
        profileStatus.Text = "Selected profile: Checking...";

        try
        {
            var profiles = await repository.GetAllAsync();

            var profile = profiles.FirstOrDefault(
                p => p.Type == ConnectionType.WireGuard);

            if (profile == null)
            {
                profileStatus.Text = "Selected profile: Not found";
                interfaceStatus.Text = "WireGuard interface: Unknown";
                handshakeStatus.Text = "Latest handshake: Unknown";
                pingStatus.Text = "Network latency: Unknown";
                return;
            }

            profileStatus.Text =
                $"Selected profile: {profile.Name}";

            if (string.IsNullOrWhiteSpace(profile.SourcePath))
            {
                interfaceStatus.Text =
                    "WireGuard interface: Missing configuration path";
                return;
            }

            string tunnelName =
                System.IO.Path.GetFileNameWithoutExtension(
                    profile.SourcePath);

            var diagnostics = new WireGuardDiagnostics();

            bool active = await diagnostics.IsInterfaceActiveAsync(
                tunnelName);

            interfaceStatus.Text = active
                ? "WireGuard interface: Active"
                : "WireGuard interface: Inactive";

            if (!active)
            {
                handshakeStatus.Text =
                    "Latest handshake: Interface inactive";

                pingStatus.Text =
                    "Network latency: Not checked";

                return;
            }

            DateTime? handshake =
                await diagnostics.GetLatestHandshakeAsync(
                    tunnelName);

            handshakeStatus.Text = handshake == null
                ? "Latest handshake: No handshake recorded"
                : $"Latest handshake: {handshake.Value.ToLocalTime():G}";

            using var ping = new Ping();

            try
            {
                var reply = await ping.SendPingAsync(
                    profile.Host, 4000);

                pingStatus.Text = reply.Status ==
                    IPStatus.Success
                    ? $"Network latency: {reply.RoundtripTime} ms"
                    : $"Network latency: {reply.Status}";
            }
            catch (Exception ex)
            {
                pingStatus.Text =
                    $"Network latency: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Diagnostics error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            runButton.Enabled = true;
            runButton.Text = "Run diagnostics";
        }
    }
}
