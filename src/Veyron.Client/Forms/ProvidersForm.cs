
using System.Drawing;
using System.Windows.Forms;
using Veyron.Core.Enums;
using Veyron.Core.Models;
using Veyron.Infrastructure.Storage;

namespace Veyron.Client.Forms;

public class ProvidersForm : Form
{
    private readonly ProviderRepository repository = new();

    private readonly DataGridView grid = new();

    public ProvidersForm()
    {
        Text = "Veyron - Providers";
        Size = new Size(850, 550);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(15, 16, 24);
        ForeColor = Color.White;

        InitializeInterface();

        Load += async (s, e) => await RefreshDataAsync();
    }

    private void InitializeInterface()
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = Color.FromArgb(25, 27, 40);
        grid.ForeColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;

        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor =
            Color.FromArgb(35, 37, 53);
        grid.ColumnHeadersDefaultCellStyle.ForeColor =
            Color.White;

        grid.DefaultCellStyle.BackColor =
            Color.FromArgb(25, 27, 40);
        grid.DefaultCellStyle.ForeColor = Color.White;
        grid.DefaultCellStyle.SelectionBackColor =
            Color.FromArgb(105, 75, 190);
        grid.DefaultCellStyle.SelectionForeColor =
            Color.White;

        Controls.Add(grid);

        Panel toolbar = new()
        {
            Dock = DockStyle.Top,
            Height = 65,
            Padding = new Padding(15),
            BackColor = Color.FromArgb(20, 21, 31)
        };

        Controls.Add(toolbar);
        toolbar.BringToFront();

        Button add = CreateButton("Add provider");
        add.Click += async (s, e) => await AddProviderAsync();

        Button remove = CreateButton("Remove");
        remove.Left = 135;
        remove.Click += async (s, e) => await RemoveProviderAsync();

        toolbar.Controls.Add(add);
        toolbar.Controls.Add(remove);
    }

    private Button CreateButton(string text)
    {
        return new Button
        {
            Text = text,
            Size = new Size(115, 35),
            BackColor = Color.FromArgb(110, 80, 200),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
    }

    private async Task RefreshDataAsync()
    {
        var items = await repository.GetAllAsync();

        grid.DataSource = items.Select(x => new
        {
            x.Id,
            x.Name,
            Protocol = x.Type.ToString(),
            x.Endpoint,
            Enabled = x.IsEnabled
        }).ToList();

        if (grid.Columns["Id"] != null)
            grid.Columns["Id"].Visible = false;
    }

    private async Task AddProviderAsync()
    {
        using Form dialog = new()
        {
            Text = "Add provider",
            Size = new Size(390, 300),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(25, 27, 40),
            ForeColor = Color.White,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false
        };

        TextBox name = new()
        {
            Left = 20,
            Top = 35,
            Width = 320,
            PlaceholderText = "Provider name"
        };

        TextBox endpoint = new()
        {
            Left = 20,
            Top = 85,
            Width = 320,
            PlaceholderText = "Provider endpoint"
        };

        ComboBox type = new()
        {
            Left = 20,
            Top = 135,
            Width = 320,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DataSource = Enum.GetValues(typeof(ConnectionType))
        };

        Button save = CreateButton("Save");
        save.Left = 20;
        save.Top = 190;

        save.Click += async (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                MessageBox.Show("Enter provider name.");
                return;
            }

            await repository.AddAsync(new ProviderInfo
            {
                Name = name.Text.Trim(),
                Endpoint = endpoint.Text.Trim(),
                Type = (ConnectionType)type.SelectedItem!
            });

            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };

        dialog.Controls.AddRange(new Control[]
        {
            name, endpoint, type, save
        });

        if (dialog.ShowDialog(this) == DialogResult.OK)
            await RefreshDataAsync();
    }

    private async Task RemoveProviderAsync()
    {
        if (grid.SelectedRows.Count == 0)
            return;

        string id = grid.SelectedRows[0]
            .Cells["Id"].Value?.ToString() ?? "";

        if (string.IsNullOrEmpty(id))
            return;

        if (MessageBox.Show(
            "Remove selected provider?",
            "Veyron",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        await repository.RemoveAsync(id);

        await RefreshDataAsync();
    }
}