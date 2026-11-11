
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Veyron.Client.Themes;
using Veyron.Core.Enums;
using Veyron.Core.Models;
using Veyron.Infrastructure.Import;
using Veyron.Infrastructure.Storage;

namespace Veyron.Client.Forms;

public sealed class ImportProfileForm : Form
{
    private readonly ProfileRepository repository = new();
    private readonly ProfileImporter importer = new();

    private TextBox nameBox = null!;
    private TextBox pathBox = null!;
    private ComboBox typeBox = null!;
    private TextBox hostBox = null!;
    private TextBox portBox = null!;

    private string selectedPath = "";

    public ImportProfileForm()
    {
        Text = "Veyron - Import Profile";
        Size = new Size(520, 470);
        MinimumSize = new Size(520, 470);
        MaximumSize = new Size(520, 470);

        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Text;
        Font = AppTheme.Font;

        BuildInterface();
    }

    private void BuildInterface()
    {
        var title = new Label
        {
            Text = "Import connection profile",
            Font = new Font("Segoe UI Semibold", 19),
            AutoSize = true,
            Location = new Point(25, 20),
            ForeColor = AppTheme.Text
        };

        Controls.Add(title);

        AddLabel("Profile name", 28, 85);
        nameBox = AddTextBox(28, 110, 440);

        AddLabel("Configuration file", 28, 155);

        pathBox = AddTextBox(28, 180, 340);
        pathBox.ReadOnly = true;

        var browseButton = CreateButton("Browse", 378, 180, 90);

        browseButton.Click += async (_, _) =>
            await BrowseFileAsync();

        Controls.Add(browseButton);

        AddLabel("Protocol", 28, 225);

        typeBox = new ComboBox
        {
            Location = new Point(28, 250),
            Width = 210,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AppTheme.Card,
            ForeColor = AppTheme.Text
        };

        typeBox.Items.AddRange(Enum.GetValues(typeof(ConnectionType))
            .Cast<object>().ToArray());

        typeBox.SelectedIndex = 0;
        Controls.Add(typeBox);

        AddLabel("Server host", 255, 225);
        hostBox = AddTextBox(255, 250, 213);

        AddLabel("Port", 28, 295);
        portBox = AddTextBox(28, 320, 210);

        var importButton = CreateButton(
            "Import profile", 28, 375, 140);

        importButton.Click += async (_, _) =>
            await ImportAsync(importButton);

        var proxyListButton = CreateButton(
            "Import proxy list", 178, 375, 160);

        proxyListButton.Click += async (_, _) =>
            await ImportProxyListAsync(proxyListButton);

        var cancelButton = CreateButton(
            "Cancel", 348, 375, 120);

        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.Add(importButton);
        Controls.Add(proxyListButton);
        Controls.Add(cancelButton);
    }

    private async Task ImportAsync(Button button)
    {
        if (string.IsNullOrWhiteSpace(nameBox.Text))
        {
            MessageBox.Show("Enter profile name.");
            return;
        }

        if (!File.Exists(selectedPath))
        {
            MessageBox.Show("Select a valid configuration file.");
            return;
        }

        if (!int.TryParse(portBox.Text, out int port) ||
            port < 1 || port > 65535)
        {
            MessageBox.Show("Enter a valid server port.");
            return;
        }

        if (string.IsNullOrWhiteSpace(hostBox.Text))
        {
            MessageBox.Show("Server host is required.");
            return;
        }

        if (typeBox.SelectedItem is not ConnectionType type)
            return;

        button.Enabled = false;

        try
        {
            var profile = new ConnectionProfile
            {
                Name = nameBox.Text.Trim(),
                Host = hostBox.Text.Trim(),
                Port = port,
                Type = type,
                SourcePath = Path.GetFullPath(selectedPath),
                Country = "",
                Ping = -1,
                IsAvailable = false
            };

            await repository.AddAsync(profile);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Import error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task ImportProxyListAsync(Button button)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Proxy lists|*.txt;*.csv",
            Title = "Import proxy list"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        button.Enabled = false;
        button.Text = "Importing...";

        try
        {
            var result = await importer.ImportProxyListAsync(
                dialog.FileName,
                ConnectionType.SOCKS5);

            // Получаем существующие профили.
            var existing = await repository.GetAllAsync();

            var knownProfiles = new HashSet<string>(
                existing.Select(p => $"{p.Type}|{p.Host}|{p.Port}"),
                StringComparer.OrdinalIgnoreCase);

            // Убираем дубликаты внутри импортируемого списка
            // и среди уже сохранённых профилей.
            var uniqueProfiles = new List<ConnectionProfile>();

            int duplicates = 0;

            foreach (var profile in result.Profiles)
            {
                string key =
                    $"{profile.Type}|{profile.Host}|{profile.Port}";

                if (!knownProfiles.Add(key))
                {
                    duplicates++;
                    continue;
                }

                uniqueProfiles.Add(profile);
            }

            // Одно массовое сохранение вместо тысяч AddAsync.
            int imported = await repository.AddRangeAsync(uniqueProfiles);

            string message =
                $"Imported: {imported:N0}\n" +
                $"Duplicates skipped: {duplicates:N0}\n" +
                $"Invalid lines: {result.Errors.Count:N0}";

            if (result.Errors.Count > 0)
            {
                message += "\n\nErrors:\n" +
                    string.Join("\n", result.Errors.Take(8));
            }

            MessageBox.Show(
                message,
                "Proxy list import",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            if (imported > 0)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Proxy import error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
            button.Text = "Import proxy list";
        }
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = AppTheme.Secondary
        });
    }

    private TextBox AddTextBox(int x, int y, int width)
    {
        var box = new TextBox
        {
            Location = new Point(x, y),
            Width = width,
            Height = 28,
            BackColor = AppTheme.Card,
            ForeColor = AppTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(box);
        return box;
    }

    private Button CreateButton(
        string text, int x, int y, int width)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 38),
            BackColor = AppTheme.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
    }

    private async Task BrowseFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "All supported files|*.conf;*.ovpn;*.txt;*.csv|" +
             "VPN configurations|*.conf;*.ovpn|" +
             "Proxy lists|*.txt;*.csv|" +
             "All files|*.*",
            Title = "Import configuration or proxy profile"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        selectedPath = dialog.FileName;

        try
        {
            var profile = await importer.ImportAsync(selectedPath);

            nameBox.Text = profile.Name;
            pathBox.Text = profile.SourcePath;
            hostBox.Text = profile.Host;
            portBox.Text = profile.Port.ToString();
            typeBox.SelectedItem = profile.Type;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Veyron - Import",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
