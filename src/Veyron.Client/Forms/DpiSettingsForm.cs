
using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Veyron.Core.Models;
using Veyron.Infrastructure.Storage;

namespace Veyron.Client.Forms;

public sealed class DpiSettingsForm : Form
{
    private readonly BrowserSettingsRepository repository = new();

    private CheckBox enabledCheck = null!;
    private CheckBox dnsCheck = null!;
    private CheckBox localProxyCheck = null!;
    private CheckBox tcpCheck = null!;
    private CheckBox tlsCheck = null!;

    private TextBox dnsBox = null!;
    private TextBox portBox = null!;
    private TextBox includedBox = null!;
    private TextBox excludedBox = null!;

    private Button saveButton = null!;

    public DpiSettingsForm()
    {
        Text = "Veyron - DPI Bypass";

        Size = new Size(650, 720);
        MinimumSize = new Size(600, 650);

        StartPosition = FormStartPosition.CenterParent;

        BackColor = Color.FromArgb(15, 16, 24);
        ForeColor = Color.White;

        BuildInterface();

        Load += async (_, _) => await LoadSettingsAsync();
    }

    private void BuildInterface()
    {
        var main = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(20),
            BackColor = BackColor
        };

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = BackColor
        };

        layout.Controls.Add(CreateTitle("DPI Bypass"));

        layout.Controls.Add(CreateDescription(
            "Network processing settings for Veyron Browser"));

        enabledCheck = CreateCheckBox(
            "Enable network processing");

        tcpCheck = CreateCheckBox(
            "Enable TCP stream handling");

        tlsCheck = CreateCheckBox(
            "Enable TLS connection handling");

        dnsCheck = CreateCheckBox(
            "Use custom DNS settings");

        localProxyCheck = CreateCheckBox(
            "Use local proxy");

        layout.Controls.Add(CreateSection("General"));
        layout.Controls.Add(enabledCheck);
        layout.Controls.Add(tcpCheck);
        layout.Controls.Add(tlsCheck);

        layout.Controls.Add(CreateSection("DNS"));

        dnsBox = CreateTextBox("DNS server address");

        layout.Controls.Add(dnsCheck);
        layout.Controls.Add(dnsBox);

        layout.Controls.Add(CreateSection("Local proxy"));

        portBox = CreateTextBox("Local proxy port");
        portBox.Text = "2080";

        layout.Controls.Add(localProxyCheck);
        layout.Controls.Add(portBox);

        layout.Controls.Add(CreateSection("Traffic profiles"));

        layout.Controls.Add(CreateSection("Domain rules"));

        includedBox = CreateTextBox(
            "Included domains (one per line)");

        includedBox.Multiline = true;
        includedBox.Height = 100;
        includedBox.ScrollBars = ScrollBars.Vertical;

        excludedBox = CreateTextBox(
            "Excluded domains (one per line)");

        excludedBox.Multiline = true;
        excludedBox.Height = 100;
        excludedBox.ScrollBars = ScrollBars.Vertical;

        layout.Controls.Add(includedBox);
        layout.Controls.Add(excludedBox);

        saveButton = new Button
        {
            Text = "Save settings",
            Width = 550,
            Height = 42,
            BackColor = Color.FromArgb(110, 80, 200),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 20, 0, 10)
        };

        saveButton.Click += async (_, _) =>
            await SaveSettingsAsync();

        layout.Controls.Add(saveButton);

        main.Controls.Add(layout);

        Controls.Add(main);
    }

    private Label CreateTitle(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 19, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 5)
        };
    }

    private Label CreateDescription(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 20)
        };
    }

    private Label CreateSection(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = Color.FromArgb(180, 155, 255),
            AutoSize = true,
            Margin = new Padding(0, 18, 0, 8)
        };
    }

    private CheckBox CreateCheckBox(string text)
    {
        return new CheckBox
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = BackColor,
            Margin = new Padding(0, 5, 0, 8)
        };
    }

    private TextBox CreateTextBox(string placeholder)
    {
        return new TextBox
        {
            Width = 550,
            Height = 30,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(30, 31, 45),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10),
            PlaceholderText = placeholder,
            Margin = new Padding(0, 5, 0, 8)
        };
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await repository.LoadAsync();

            enabledCheck.Checked = settings.Enabled;
            tcpCheck.Checked = settings.ProcessTcpStreams;
            tlsCheck.Checked = settings.ProcessTlsConnections;

            dnsCheck.Checked = settings.EnableDnsConfiguration;
            dnsBox.Text = settings.DnsServer;

            localProxyCheck.Checked = settings.UseLocalProxy;
            portBox.Text = settings.LocalProxyPort.ToString();

            includedBox.Text = string.Join(
                Environment.NewLine,
                settings.IncludedDomains);

            excludedBox.Text = string.Join(
                Environment.NewLine,
                settings.ExcludedDomains);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Settings error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (!int.TryParse(portBox.Text, out int port) ||
            port < 1 || port > 65535)
        {
            MessageBox.Show("Invalid local proxy port.");
            return;
        }

        var settings = new BrowserNetworkSettings
        {
            Enabled = enabledCheck.Checked,

            ProcessTcpStreams = tcpCheck.Checked,
            ProcessTlsConnections = tlsCheck.Checked,

            EnableDnsConfiguration = dnsCheck.Checked,
            DnsServer = dnsBox.Text.Trim(),

            UseLocalProxy = localProxyCheck.Checked,
            LocalProxyPort = port,

            IncludedDomains = includedBox.Lines
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),

            ExcludedDomains = excludedBox.Lines
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        try
        {
            saveButton.Enabled = false;

            await repository.SaveAsync(settings);

            MessageBox.Show(
                "Settings saved successfully.",
                "Veyron",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Save error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            saveButton.Enabled = true;
        }
    }
}
