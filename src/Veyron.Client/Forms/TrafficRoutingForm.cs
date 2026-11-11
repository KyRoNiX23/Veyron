using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Veyron.Core.Enums;
using Veyron.Core.Models;

namespace Veyron.Client.Forms;

public sealed class TrafficRoutingForm : Form
{
    private readonly RoutingProfile profile;
    private readonly BindingList<RoutingRule> rules;

    private readonly ComboBox modeBox = new();
    private readonly ComboBox defaultActionBox = new();
    private readonly DataGridView rulesGrid = new();

    public TrafficRoutingForm(RoutingProfile profile)
    {
        this.profile = profile ??
            throw new ArgumentNullException(nameof(profile));

        rules = new BindingList<RoutingRule>(
            profile.Rules.Select(CloneRule).ToList());

        InitializeInterface();
        LoadProfile();
    }

    private void InitializeInterface()
    {
        Text = "Veyron — Traffic Routing";
        Size = new Size(850, 570);
        MinimumSize = new Size(700, 450);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(22, 22, 30);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = BackColor
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));

        Controls.Add(root);

        var title = new Label
        {
            Text = "Traffic Routing",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 17F),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };

        root.Controls.Add(title, 0, 0);

        var settings = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Color.FromArgb(30, 30, 41),
            Padding = new Padding(12)
        };

        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        settings.Controls.Add(CreateLabel("Routing mode"), 0, 0);
        settings.Controls.Add(CreateLabel("Default action"), 2, 0);

        ConfigureCombo(modeBox, Enum.GetValues(typeof(RoutingMode)));
        ConfigureCombo(defaultActionBox, Enum.GetValues(typeof(RoutingAction)));

        settings.Controls.Add(modeBox, 1, 0);
        settings.Controls.Add(defaultActionBox, 3, 0);

        var hint = new Label
        {
            Text = "Rules are checked from top to bottom. The first matching rule is applied.",
            Dock = DockStyle.Fill,
            ForeColor = Color.Silver,
            TextAlign = ContentAlignment.MiddleLeft
        };

        settings.Controls.Add(hint, 0, 1);
        settings.SetColumnSpan(hint, 4);

        root.Controls.Add(settings, 0, 1);

        ConfigureGrid();

        root.Controls.Add(rulesGrid, 0, 2);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };

        var addButton = CreateButton("+ Add rule");
        addButton.Click += (_, _) => AddRule();

        var removeButton = CreateButton("Remove");
        removeButton.Click += (_, _) => RemoveSelectedRule();

        var saveButton = CreateButton("Save");
        saveButton.BackColor = Color.FromArgb(105, 75, 190);
        saveButton.Click += (_, _) => SaveProfile();

        var cancelButton = CreateButton("Cancel");
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        bottom.Controls.Add(addButton);
        bottom.Controls.Add(removeButton);
        bottom.Controls.Add(saveButton);
        bottom.Controls.Add(cancelButton);

        root.Controls.Add(bottom, 0, 3);
    }

    private void ConfigureGrid()
    {
        rulesGrid.Dock = DockStyle.Fill;
        rulesGrid.BackgroundColor = Color.FromArgb(25, 25, 34);
        rulesGrid.BorderStyle = BorderStyle.None;
        rulesGrid.GridColor = Color.FromArgb(48, 48, 62);
        rulesGrid.EnableHeadersVisualStyles = false;
        rulesGrid.AutoGenerateColumns = false;
        rulesGrid.AllowUserToAddRows = false;
        rulesGrid.AllowUserToDeleteRows = false;
        rulesGrid.RowHeadersVisible = false;
        rulesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        rulesGrid.MultiSelect = false;
        rulesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        rulesGrid.DataSource = rules;

        rulesGrid.ColumnHeadersDefaultCellStyle.BackColor =
            Color.FromArgb(38, 38, 52);

        rulesGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        rulesGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor =
            Color.FromArgb(38, 38, 52);

        rulesGrid.DefaultCellStyle.BackColor =
            Color.FromArgb(30, 30, 41);

        rulesGrid.DefaultCellStyle.ForeColor = Color.White;
        rulesGrid.DefaultCellStyle.SelectionBackColor =
            Color.FromArgb(75, 60, 115);

        rulesGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        rulesGrid.DefaultCellStyle.Padding = new Padding(5);

        rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Domain / Pattern",
            DataPropertyName = nameof(RoutingRule.Pattern),
            FillWeight = 150
        });

        var actionColumn = new DataGridViewComboBoxColumn
        {
            HeaderText = "Action",
            DataPropertyName = nameof(RoutingRule.Action),
            DataSource = Enum.GetValues(typeof(RoutingAction)),
            FillWeight = 75,
            FlatStyle = FlatStyle.Flat
        };

        rulesGrid.Columns.Add(actionColumn);

        rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Enabled",
            DataPropertyName = nameof(RoutingRule.Enabled),
            FillWeight = 55
        });

        rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Description",
            DataPropertyName = nameof(RoutingRule.Description),
            FillWeight = 130
        });

        rulesGrid.DataError += (_, e) =>
        {
            e.ThrowException = false;
        };
    }

    private void LoadProfile()
    {
        modeBox.SelectedItem = profile.Mode;
        defaultActionBox.SelectedItem = profile.DefaultAction;
    }

    private void AddRule()
    {
        rules.Add(new RoutingRule
        {
            Pattern = "example.com",
            Action = RoutingAction.Proxy,
            Enabled = true,
            Description = "New rule"
        });

        int index = rules.Count - 1;

        rulesGrid.ClearSelection();
        rulesGrid.Rows[index].Selected = true;
        rulesGrid.CurrentCell = rulesGrid.Rows[index].Cells[0];
    }

    private void RemoveSelectedRule()
    {
        if (rulesGrid.CurrentRow?.DataBoundItem is not RoutingRule rule)
            return;

        rules.Remove(rule);
    }

    private void SaveProfile()
    {
        rulesGrid.EndEdit();

        if (modeBox.SelectedItem is not RoutingMode mode ||
            defaultActionBox.SelectedItem is not RoutingAction defaultAction)
        {
            MessageBox.Show(
                "Select a routing mode and default action.",
                "Veyron",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        foreach (RoutingRule rule in rules)
        {
            rule.Pattern = rule.Pattern.Trim();

            if (string.IsNullOrWhiteSpace(rule.Pattern))
            {
                MessageBox.Show(
                    "A domain pattern cannot be empty.",
                    "Veyron",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }
        }

        profile.Mode = mode;
        profile.DefaultAction = defaultAction;
        profile.Rules = rules.Select(CloneRule).ToList();

        DialogResult = DialogResult.OK;
        Close();
    }

    private static RoutingRule CloneRule(RoutingRule rule)
    {
        return new RoutingRule
        {
            Id = rule.Id,
            Pattern = rule.Pattern,
            Action = rule.Action,
            Enabled = rule.Enabled,
            Description = rule.Description
        };
    }

    private static void ConfigureCombo(
        ComboBox combo,
        Array values)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Color.FromArgb(40, 40, 54);
        combo.ForeColor = Color.White;

        combo.Items.AddRange(values.Cast<object>().ToArray());
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            Text = text,
            Width = 115,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(42, 42, 56),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
    }
}