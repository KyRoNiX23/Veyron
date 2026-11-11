
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Veyron.Core.Enums;
using Veyron.Core.Models;
using Veyron.Infrastructure.Storage;

namespace Veyron.Client.Forms;

public sealed class EditProfileForm : Form
{
    private readonly ProfileRepository repository = new();
    private readonly ConnectionProfile profile;

    private TextBox nameBox = null!;
    private TextBox hostBox = null!;
    private TextBox portBox = null!;
    private TextBox countryBox = null!;
    private ComboBox typeBox = null!;

    public EditProfileForm(ConnectionProfile profile)
    {
        this.profile = profile;

        Text = "Veyron - Edit Profile";
        Size = new Size(480, 430);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        BackColor = Color.FromArgb(15, 16, 24);
        ForeColor = Color.White;

        BuildInterface();
        LoadProfile();
    }

    private void BuildInterface()
    {
        AddLabel("Profile name", 25, 20);
        nameBox = AddTextBox(25, 45, 410);

        AddLabel("Protocol", 25, 90);

        typeBox = new ComboBox
        {
            Location = new Point(25, 115),
            Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(25, 27, 40),
            ForeColor = Color.White
        };

        foreach (ConnectionType type in Enum.GetValues(typeof(ConnectionType)))
            typeBox.Items.Add(type);

        Controls.Add(typeBox);

        AddLabel("Server host", 25, 165);
        hostBox = AddTextBox(25, 190, 410);

        AddLabel("Port", 25, 235);
        portBox = AddTextBox(25, 260, 190);

        AddLabel("Country", 235, 235);
        countryBox = AddTextBox(235, 260, 200);

        var saveButton = CreateButton("Save changes", 25, 325, 190);
        var cancelButton = CreateButton("Cancel", 235, 325, 100);

        saveButton.Click += async (_, _) =>
            await SaveAsync(saveButton);

        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.Add(saveButton);
        Controls.Add(cancelButton);
    }

    private void LoadProfile()
    {
        nameBox.Text = profile.Name;
        hostBox.Text = profile.Host;
        portBox.Text = profile.Port.ToString();
        countryBox.Text = profile.Country;
        typeBox.SelectedItem = profile.Type;
    }

    private async Task SaveAsync(Button button)
    {
        if (string.IsNullOrWhiteSpace(nameBox.Text))
        {
            MessageBox.Show("Enter profile name.");
            return;
        }

        if (string.IsNullOrWhiteSpace(hostBox.Text))
        {
            MessageBox.Show("Enter server host.");
            return;
        }

        if (!int.TryParse(portBox.Text, out int port) ||
            port < 1 || port > 65535)
        {
            MessageBox.Show("Invalid port.");
            return;
        }

        if (typeBox.SelectedItem is not ConnectionType type)
            return;

        button.Enabled = false;

        try
        {
            profile.Name = nameBox.Text.Trim();
            profile.Host = hostBox.Text.Trim();
            profile.Port = port;
            profile.Country = countryBox.Text.Trim();
            profile.Type = type;

            await repository.UpdateAsync(profile);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Veyron",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(145, 148, 167)
        });
    }

    private TextBox AddTextBox(int x, int y, int width)
    {
        var box = new TextBox
        {
            Location = new Point(x, y),
            Width = width,
            BackColor = Color.FromArgb(25, 27, 40),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(box);
        return box;
    }

    private Button CreateButton(string text, int x, int y, int width)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 38),
            BackColor = Color.FromArgb(110, 80, 200),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
    }
}