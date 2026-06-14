using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using DJWinOptimizer.Core.Interfaces;
using DJWinOptimizer.Core.Models;

namespace DJWinOptimizer.UI
{
    public partial class SoftwareManagerControl : UserControl
    {
        private readonly IPackageManager _packageManager;
        private readonly ILogger _logger;
        private List<PackageApplication> _allApplications = new();
        private List<PackageApplication> _filteredApplications = new();

        public SoftwareManagerControl(IPackageManager packageManager, ILogger logger)
        {
            _packageManager = packageManager;
            _logger = logger;
            InitializeComponent();
            LoadApplications();
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            // Main layout
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 2,
                Padding = new Padding(10)
            };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            // Header panel
            var headerPanel = new Panel
            {
                Dock = DockStyle.Fill
            };
            var titleLabel = new Label
            {
                Text = "Software Manager",
                Font = new System.Drawing.Font("Segoe UI", 16, System.Drawing.FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            headerPanel.Controls.Add(titleLabel);
            mainPanel.Controls.Add(headerPanel, 0, 0);
            mainPanel.SetColumnSpan(headerPanel, 2);

            // Left panel - Categories and Filter
            var leftPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            var categoryLabel = new Label
            {
                Text = "Categories",
                Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(5, 5),
                AutoSize = true
            };

            var categoryListBox = new ListBox
            {
                Location = new System.Drawing.Point(5, 30),
                Size = new System.Drawing.Size(250, 200),
                Dock = DockStyle.Top
            };
            categoryListBox.SelectedIndexChanged += CategoryListBox_SelectedIndexChanged;

            var searchLabel = new Label
            {
                Text = "Search",
                Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(5, 240),
                AutoSize = true
            };

            var searchTextBox = new TextBox
            {
                Location = new System.Drawing.Point(5, 265),
                Size = new System.Drawing.Size(250, 25)
            };
            searchTextBox.TextChanged += SearchTextBox_TextChanged;

            var packageManagerLabel = new Label
            {
                Text = "Package Manager",
                Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(5, 300),
                AutoSize = true
            };

            var packageManagerComboBox = new ComboBox
            {
                Location = new System.Drawing.Point(5, 325),
                Size = new System.Drawing.Size(250, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            packageManagerComboBox.Items.AddRange(new object[] { "Auto", "Winget", "Chocolatey" });
            packageManagerComboBox.SelectedIndex = 0;

            leftPanel.Controls.AddRange(new Control[] { categoryLabel, categoryListBox, searchLabel, searchTextBox, packageManagerLabel, packageManagerComboBox });
            mainPanel.Controls.Add(leftPanel, 0, 1);

            // Right panel - Applications list
            var rightPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            var appsLabel = new Label
            {
                Text = "Applications",
                Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(5, 5),
                AutoSize = true
            };

            var appsListView = new ListView
            {
                Location = new System.Drawing.Point(5, 30),
                Size = new System.Drawing.Size(600, 400),
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                CheckBoxes = true,
                GridLines = true
            };
            appsListView.Columns.Add("Name", 200);
            appsListView.Columns.Add("Category", 100);
            appsListView.Columns.Add("Description", 300);
            appsListView.Columns.Add("Installed", 80);
            appsListView.ItemChecked += AppsListView_ItemChecked;

            rightPanel.Controls.AddRange(new Control[] { appsLabel, appsListView });
            mainPanel.Controls.Add(rightPanel, 1, 1);

            // Bottom panel - Actions
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            var installButton = new Button
            {
                Text = "Install Selected",
                Size = new System.Drawing.Size(150, 40),
                Location = new System.Drawing.Point(5, 10),
                BackColor = System.Drawing.Color.FromArgb(0, 120, 215),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };
            installButton.Click += InstallButton_Click;

            var uninstallButton = new Button
            {
                Text = "Uninstall Selected",
                Size = new System.Drawing.Size(150, 40),
                Location = new System.Drawing.Point(165, 10),
                BackColor = System.Drawing.Color.FromArgb(200, 50, 50),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };
            uninstallButton.Click += UninstallButton_Click;

            var refreshButton = new Button
            {
                Text = "Refresh",
                Size = new System.Drawing.Size(100, 40),
                Location = new System.Drawing.Point(325, 10),
                FlatStyle = FlatStyle.Flat
            };
            refreshButton.Click += RefreshButton_Click;

            var wingetStatusLabel = new Label
            {
                Text = "Winget: Checking...",
                Location = new System.Drawing.Point(450, 10),
                AutoSize = true
            };

            var chocoStatusLabel = new Label
            {
                Text = "Chocolatey: Checking...",
                Location = new System.Drawing.Point(450, 35),
                AutoSize = true
            };

            bottomPanel.Controls.AddRange(new Control[] { installButton, uninstallButton, refreshButton, wingetStatusLabel, chocoStatusLabel });
            mainPanel.Controls.Add(bottomPanel, 0, 2);
            mainPanel.SetColumnSpan(bottomPanel, 2);

            this.Controls.Add(mainPanel);

            // Store references
            _categoryListBox = categoryListBox;
            _searchTextBox = searchTextBox;
            _packageManagerComboBox = packageManagerComboBox;
            _appsListView = appsListView;
            _wingetStatusLabel = wingetStatusLabel;
            _chocoStatusLabel = chocoStatusLabel;

            // Check package managers
            CheckPackageManagers();
        }

        private ListBox _categoryListBox = null!;
        private TextBox _searchTextBox = null!;
        private ComboBox _packageManagerComboBox = null!;
        private ListView _appsListView = null!;
        private Label _wingetStatusLabel = null!;
        private Label _chocoStatusLabel = null!;

        private void LoadApplications()
        {
            try
            {
                _logger.Info("Loading applications from PackageManager...");
                _allApplications = _packageManager.GetAvailableApplications().ToList();
                _logger.Info($"Loaded {_allApplications.Count} applications");
                
                _filteredApplications = new List<PackageApplication>(_allApplications);

                // Populate categories
                var categories = _allApplications.Select(a => a.Category).Distinct().OrderBy(c => c).ToList();
                _categoryListBox.Items.Clear();
                _categoryListBox.Items.Add("All");
                _categoryListBox.Items.AddRange(categories.ToArray());
                _categoryListBox.SelectedIndex = 0;

                // Populate applications list
                PopulateApplicationsList();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load applications", ex);
                MessageBox.Show($"Failed to load applications: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulateApplicationsList()
        {
            _appsListView.Items.Clear();
            foreach (var app in _filteredApplications)
            {
                var item = new ListViewItem(app.Content)
                {
                    SubItems = { app.Category, app.Description, app.Installed ? "Yes" : "No" },
                    Tag = app
                };
                _appsListView.Items.Add(item);
            }
        }

        private void CategoryListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_categoryListBox.SelectedItem == null) return;

            var selectedCategory = _categoryListBox.SelectedItem.ToString();
            if (selectedCategory == "All")
            {
                _filteredApplications = new List<PackageApplication>(_allApplications);
            }
            else
            {
                _filteredApplications = _allApplications.Where(a => a.Category == selectedCategory).ToList();
            }

            ApplySearchFilter();
        }

        private void SearchTextBox_TextChanged(object? sender, EventArgs e)
        {
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            var searchTerm = _searchTextBox.Text.ToLower();
            var selectedCategory = _categoryListBox.SelectedItem?.ToString();

            var filtered = _allApplications.AsEnumerable();

            if (selectedCategory != null && selectedCategory != "All")
            {
                filtered = filtered.Where(a => a.Category == selectedCategory);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                filtered = filtered.Where(a => 
                    a.Content.ToLower().Contains(searchTerm) ||
                    a.Description.ToLower().Contains(searchTerm));
            }

            _filteredApplications = filtered.ToList();
            PopulateApplicationsList();
        }

        private void AppsListView_ItemChecked(object? sender, ItemCheckedEventArgs e)
        {
            // Handle item check/uncheck
        }

        private void CheckPackageManagers()
        {
            var wingetAvailable = _packageManager.IsWingetAvailable();
            var chocoAvailable = _packageManager.IsChocolateyAvailable();

            _wingetStatusLabel.Text = $"Winget: {(wingetAvailable ? "Available" : "Not Available")}";
            _wingetStatusLabel.ForeColor = wingetAvailable ? System.Drawing.Color.Green : System.Drawing.Color.Red;

            _chocoStatusLabel.Text = $"Chocolatey: {(chocoAvailable ? "Available" : "Not Available")}";
            _chocoStatusLabel.ForeColor = chocoAvailable ? System.Drawing.Color.Green : System.Drawing.Color.Red;
        }

        private void InstallButton_Click(object? sender, EventArgs e)
        {
            var selectedApps = GetSelectedApplications();
            if (selectedApps.Count == 0)
            {
                MessageBox.Show("Please select at least one application to install.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var packageManagerType = GetSelectedPackageManagerType();
            var actions = selectedApps.Select(app => new PackageManagerAction
            {
                PackageId = app.Id,
                Type = packageManagerType,
                Action = PackageAction.Install
            }).ToList();

            var result = MessageBox.Show($"Install {selectedApps.Count} applications?", "Confirm Installation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                ExecuteActions(actions);
            }
        }

        private void UninstallButton_Click(object? sender, EventArgs e)
        {
            var selectedApps = GetSelectedApplications();
            if (selectedApps.Count == 0)
            {
                MessageBox.Show("Please select at least one application to uninstall.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var packageManagerType = GetSelectedPackageManagerType();
            var actions = selectedApps.Select(app => new PackageManagerAction
            {
                PackageId = app.Id,
                Type = packageManagerType,
                Action = PackageAction.Uninstall
            }).ToList();

            var result = MessageBox.Show($"Uninstall {selectedApps.Count} applications?", "Confirm Uninstallation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                ExecuteActions(actions);
            }
        }

        private void RefreshButton_Click(object? sender, EventArgs e)
        {
            LoadApplications();
            CheckPackageManagers();
        }

        private List<PackageApplication> GetSelectedApplications()
        {
            var selected = new List<PackageApplication>();
            foreach (ListViewItem item in _appsListView.CheckedItems)
            {
                if (item.Tag is PackageApplication app)
                {
                    selected.Add(app);
                }
            }
            return selected;
        }

        private PackageManagerType GetSelectedPackageManagerType()
        {
            return _packageManagerComboBox.SelectedIndex switch
            {
                1 => PackageManagerType.Winget,
                2 => PackageManagerType.Chocolatey,
                _ => PackageManagerType.Auto
            };
        }

        private void ExecuteActions(List<PackageManagerAction> actions)
        {
            var progressForm = new ProgressForm("Processing Package Actions", actions.Count);
            progressForm.Show();

            var results = _packageManager.ExecuteActions(actions);

            progressForm.Close();

            var successCount = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);

            var message = $"Completed: {successCount} successful, {failCount} failed";
            if (failCount > 0)
            {
                var errors = string.Join("\n", results.Where(r => !r.Success).Select(r => $"{r.PackageId}: {r.ErrorMessage}"));
                MessageBox.Show($"{message}\n\nErrors:\n{errors}", "Results", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(message, "Results", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // Refresh the list
            LoadApplications();
        }
    }
}
