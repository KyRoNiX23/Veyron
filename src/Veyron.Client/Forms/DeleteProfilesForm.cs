
using Veyron.Core.Models;
using Veyron.Infrastructure.Storage;

namespace Veyron.Client.Forms;

public sealed class DeleteProfilesForm : Form
{
    private readonly ProfileRepository repository = new();

    private readonly ComboBox fieldBox = new();
    private readonly ComboBox valueBox = new();

    private readonly Button deleteButton = new();
    private readonly Button deleteAllButton = new();

    private List<ConnectionProfile> profiles = new();

    private readonly Dictionary<string, Func<ConnectionProfile, string>> fields = new()
    {
        ["Name"] = p => p.Name,
        ["Country"] = p => p.Country,
        ["Host"] = p => p.Host,
        ["Port"] = p => p.Port.ToString(),
        ["Protocol"] = p => p.Type.ToString(),
        ["Ping"] = p => p.Ping < 0 ? "Unknown" : p.Ping.ToString(),
        ["Source path"] = p => p.SourcePath
    };

    public DeleteProfilesForm()
    {
        Text = "Delete Profiles";
        Size = new Size(430, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        BuildInterface();

        Load += async (_, _) => await LoadProfilesAsync();
    }

    private void BuildInterface()
    {
        var title = new Label
        {
            Text = "Delete profiles by field",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 20)
        };

        var fieldLabel = new Label
        {
            Text = "Field:",
            AutoSize = true,
            Location = new Point(20, 75)
        };

        fieldBox.Location = new Point(20, 100);
        fieldBox.Size = new Size(370, 30);
        fieldBox.DropDownStyle = ComboBoxStyle.DropDownList;

        var valueLabel = new Label
        {
            Text = "Value:",
            AutoSize = true,
            Location = new Point(20, 145)
        };

        valueBox.Location = new Point(20, 170);
        valueBox.Size = new Size(370, 30);
        valueBox.DropDownStyle = ComboBoxStyle.DropDownList;

        deleteButton.Text = "Delete matching";
        deleteButton.Location = new Point(20, 220);
        deleteButton.Size = new Size(175, 35);
        deleteButton.Click += async (_, _) => await DeleteMatchingAsync();

        deleteAllButton.Text = "Delete all profiles";
        deleteAllButton.Location = new Point(215, 220);
        deleteAllButton.Size = new Size(175, 35);
        deleteAllButton.BackColor = Color.Firebrick;
        deleteAllButton.ForeColor = Color.White;
        deleteAllButton.FlatStyle = FlatStyle.Flat;
        deleteAllButton.Click += async (_, _) => await DeleteAllAsync();

        fieldBox.SelectedIndexChanged += (_, _) => UpdateValues();

        Controls.AddRange(new Control[]
        {
            title,
            fieldLabel,
            fieldBox,
            valueLabel,
            valueBox,
            deleteButton,
            deleteAllButton
        });
    }

    private async Task LoadProfilesAsync()
    {
        profiles = await repository.GetAllAsync();

        fieldBox.Items.Clear();

        foreach (var field in fields.Keys)
            fieldBox.Items.Add(field);

        if (fieldBox.Items.Count > 0)
            fieldBox.SelectedIndex = 0;

        UpdateValues();
    }

    private void UpdateValues()
    {
        valueBox.Items.Clear();

        if (fieldBox.SelectedItem is not string field)
            return;

        var selector = fields[field];

        var values = profiles
            .Select(selector)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        foreach (var value in values)
            valueBox.Items.Add(value);

        if (valueBox.Items.Count > 0)
            valueBox.SelectedIndex = 0;
    }

    private async Task DeleteMatchingAsync()
    {
        if (fieldBox.SelectedItem is not string field ||
            valueBox.SelectedItem is not string value)
        {
            MessageBox.Show("Select a field and value.");
            return;
        }

        var selector = fields[field];

        var matching = profiles
            .Where(p => string.Equals(
                selector(p),
                value,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matching.Count == 0)
            return;

        var result = MessageBox.Show(
            $"Delete {matching.Count} profiles?\n\n{field}: {value}",
            "Confirm deletion",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        await repository.RemoveRangeAsync(
            matching.Select(p => p.Id));

        await LoadProfilesAsync();

        MessageBox.Show(
            $"Deleted {matching.Count} profiles.",
            "Veyron",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task DeleteAllAsync()
    {
        if (profiles.Count == 0)
        {
            MessageBox.Show("There are no profiles to delete.");
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to delete ALL {profiles.Count} profiles?\n\nThis action cannot be undone.",
            "Delete all profiles",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        await repository.RemoveAllAsync();

        profiles.Clear();
        UpdateValues();

        MessageBox.Show(
            "All profiles have been deleted.",
            "Veyron",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
