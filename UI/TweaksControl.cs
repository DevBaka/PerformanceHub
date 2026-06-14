using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using DJWinOptimizer.Core.Interfaces;
using DJWinOptimizer.Core.Models;

namespace DJWinOptimizer.UI
{
    public partial class TweaksControl : UserControl
    {
        private readonly ISystemTweaksManager _tweaksManager;
        private readonly ILogger _logger;
        private List<SystemTweak> _allTweaks = new();
        private List<SystemTweak> _filteredTweaks = new();

        public TweaksControl(ISystemTweaksManager tweaksManager, ILogger logger)
        {
            _tweaksManager = tweaksManager;
            _logger = logger;
            InitializeComponent();
            LoadTweaks();
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
                Text = "System Tweaks",
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

            var infoLabel = new Label
            {
                Text = "⚠ Some tweaks require admin privileges",
                Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Italic),
                Location = new System.Drawing.Point(5, 300),
                Size = new System.Drawing.Size(250, 40),
                ForeColor = System.Drawing.Color.Orange
            };

            leftPanel.Controls.AddRange(new Control[] { categoryLabel, categoryListBox, searchLabel, searchTextBox, infoLabel });
            mainPanel.Controls.Add(leftPanel, 0, 1);

            // Right panel - Tweaks list
            var rightPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            var tweaksLabel = new Label
            {
                Text = "Available Tweaks",
                Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(5, 5),
                AutoSize = true
            };

            var tweaksListView = new ListView
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
            tweaksListView.Columns.Add("Tweak", 200);
            tweaksListView.Columns.Add("Category", 120);
            tweaksListView.Columns.Add("Description", 300);
            tweaksListView.Columns.Add("Applied", 80);
            tweaksListView.ItemChecked += TweaksListView_ItemChecked;

            rightPanel.Controls.AddRange(new Control[] { tweaksLabel, tweaksListView });
            mainPanel.Controls.Add(rightPanel, 1, 1);

            // Bottom panel - Actions
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            var applyButton = new Button
            {
                Text = "Apply Selected",
                Size = new System.Drawing.Size(150, 40),
                Location = new System.Drawing.Point(5, 10),
                BackColor = System.Drawing.Color.FromArgb(0, 120, 215),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };
            applyButton.Click += ApplyButton_Click;

            var undoButton = new Button
            {
                Text = "Undo Selected",
                Size = new System.Drawing.Size(150, 40),
                Location = new System.Drawing.Point(165, 10),
                BackColor = System.Drawing.Color.FromArgb(200, 100, 0),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };
            undoButton.Click += UndoButton_Click;

            var refreshButton = new Button
            {
                Text = "Refresh Status",
                Size = new System.Drawing.Size(120, 40),
                Location = new System.Drawing.Point(325, 10),
                FlatStyle = FlatStyle.Flat
            };
            refreshButton.Click += RefreshButton_Click;

            var selectAllButton = new Button
            {
                Text = "Select All",
                Size = new System.Drawing.Size(100, 40),
                Location = new System.Drawing.Point(455, 10),
                FlatStyle = FlatStyle.Flat
            };
            selectAllButton.Click += SelectAllButton_Click;

            var deselectAllButton = new Button
            {
                Text = "Deselect All",
                Size = new System.Drawing.Size(120, 40),
                Location = new System.Drawing.Point(565, 10),
                FlatStyle = FlatStyle.Flat
            };
            deselectAllButton.Click += DeselectAllButton_Click;

            bottomPanel.Controls.AddRange(new Control[] { applyButton, undoButton, refreshButton, selectAllButton, deselectAllButton });
            mainPanel.Controls.Add(bottomPanel, 0, 2);
            mainPanel.SetColumnSpan(bottomPanel, 2);

            this.Controls.Add(mainPanel);

            // Store references
            _categoryListBox = categoryListBox;
            _searchTextBox = searchTextBox;
            _tweaksListView = tweaksListView;
        }

        private ListBox _categoryListBox = null!;
        private TextBox _searchTextBox = null!;
        private ListView _tweaksListView = null!;

        private void LoadTweaks()
        {
            try
            {
                _logger.Info("Loading tweaks from SystemTweaksManager...");
                _allTweaks = _tweaksManager.GetAvailableTweaks().ToList();
                _logger.Info($"Loaded {_allTweaks.Count} tweaks");
                
                _filteredTweaks = new List<SystemTweak>(_allTweaks);

                // Populate categories
                var categories = _allTweaks.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
                _categoryListBox.Items.Clear();
                _categoryListBox.Items.Add("All");
                _categoryListBox.Items.AddRange(categories.ToArray());
                _categoryListBox.SelectedIndex = 0;

                // Populate tweaks list
                PopulateTweaksList();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load tweaks", ex);
                MessageBox.Show($"Failed to load tweaks: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulateTweaksList()
        {
            _tweaksListView.BeginUpdate();
            _tweaksListView.Items.Clear();
            foreach (var tweak in _filteredTweaks)
            {
                var isApplied = _tweaksManager.IsTweakApplied(tweak.Id);
                var item = new ListViewItem(tweak.Content)
                {
                    SubItems = { tweak.Category, tweak.Description, isApplied ? "Yes" : "No" },
                    Tag = tweak,
                    Checked = isApplied
                };
                _tweaksListView.Items.Add(item);
            }
            _tweaksListView.EndUpdate();
        }

        private void CategoryListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_categoryListBox.SelectedItem == null) return;

            var selectedCategory = _categoryListBox.SelectedItem.ToString();
            if (selectedCategory == "All")
            {
                _filteredTweaks = new List<SystemTweak>(_allTweaks);
            }
            else
            {
                _filteredTweaks = _allTweaks.Where(t => t.Category == selectedCategory).ToList();
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

            var filtered = _allTweaks.AsEnumerable();

            if (selectedCategory != null && selectedCategory != "All")
            {
                filtered = filtered.Where(t => t.Category == selectedCategory);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                filtered = filtered.Where(t => 
                    t.Content.ToLower().Contains(searchTerm) ||
                    t.Description.ToLower().Contains(searchTerm));
            }

            _filteredTweaks = filtered.ToList();
            PopulateTweaksList();
        }

        private void TweaksListView_ItemChecked(object? sender, ItemCheckedEventArgs e)
        {
            // Handle item check/uncheck
        }

        private void ApplyButton_Click(object? sender, EventArgs e)
        {
            var selectedTweaks = GetSelectedTweaks();
            if (selectedTweaks.Count == 0)
            {
                MessageBox.Show("Please select at least one tweak to apply.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Filter out already applied tweaks
            var tweaksToApply = selectedTweaks.Where(t => !_tweaksManager.IsTweakApplied(t.Id)).ToList();
            var alreadyAppliedCount = selectedTweaks.Count - tweaksToApply.Count;

            if (alreadyAppliedCount > 0)
            {
                var dialogResult = MessageBox.Show($"{alreadyAppliedCount} selected tweaks are already applied. Apply only the remaining {tweaksToApply.Count} tweaks?", "Already Applied", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (dialogResult == DialogResult.Cancel)
                {
                    return;
                }
                if (dialogResult == DialogResult.No)
                {
                    // Apply all selected tweaks anyway
                    tweaksToApply = selectedTweaks;
                }
            }

            if (tweaksToApply.Count == 0)
            {
                MessageBox.Show("All selected tweaks are already applied.", "Nothing to Apply", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var actions = tweaksToApply.Select(tweak => new TweakAction
            {
                TweakId = tweak.Id,
                Action = TweakActionType.Apply
            }).ToList();

            var result = MessageBox.Show($"Apply {tweaksToApply.Count} system tweaks?\n\n⚠ Some tweaks may require admin privileges.", "Confirm Apply", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                ExecuteActions(actions);
            }
        }

        private void UndoButton_Click(object? sender, EventArgs e)
        {
            var selectedTweaks = GetSelectedTweaks();
            if (selectedTweaks.Count == 0)
            {
                MessageBox.Show("Please select at least one tweak to undo.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var actions = selectedTweaks.Select(tweak => new TweakAction
            {
                TweakId = tweak.Id,
                Action = TweakActionType.Undo
            }).ToList();

            var result = MessageBox.Show($"Undo {selectedTweaks.Count} system tweaks?", "Confirm Undo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                ExecuteActions(actions);
            }
        }

        private void RefreshButton_Click(object? sender, EventArgs e)
        {
            LoadTweaks();
        }

        private void SelectAllButton_Click(object? sender, EventArgs e)
        {
            foreach (ListViewItem item in _tweaksListView.Items)
            {
                item.Checked = true;
            }
        }

        private void DeselectAllButton_Click(object? sender, EventArgs e)
        {
            foreach (ListViewItem item in _tweaksListView.Items)
            {
                item.Checked = false;
            }
        }

        private List<SystemTweak> GetSelectedTweaks()
        {
            var selected = new List<SystemTweak>();
            foreach (ListViewItem item in _tweaksListView.CheckedItems)
            {
                if (item.Tag is SystemTweak tweak)
                {
                    selected.Add(tweak);
                }
            }
            return selected;
        }

        private void ExecuteActions(List<TweakAction> actions)
        {
            var progressForm = new ProgressForm("Processing Tweaks", actions.Count);
            progressForm.Show();

            var results = _tweaksManager.ExecuteActions(actions);

            progressForm.Close();

            var successCount = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);
            var adminRequiredCount = results.Count(r => !r.Success && r.ErrorMessage != null && r.ErrorMessage.StartsWith("ADMIN_REQUIRED:"));

            if (adminRequiredCount > 0)
            {
                var adminResult = MessageBox.Show(
                    $"{adminRequiredCount} tweaks require administrator privileges to apply.\n\nWould you like to restart the application as administrator and retry?",
                    "Admin Privileges Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (adminResult == DialogResult.Yes)
                {
                    RestartAsAdmin();
                    return;
                }
            }

            var message = $"Completed: {successCount} successful, {failCount} failed";
            if (failCount > 0)
            {
                var errors = string.Join("\n", results.Where(r => !r.Success).Select(r => $"{r.TweakId}: {r.ErrorMessage}"));
                MessageBox.Show($"{message}\n\nErrors:\n{errors}", "Results", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(message, "Results", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // Refresh the list
            LoadTweaks();
        }

        private void RestartAsAdmin()
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? System.Windows.Forms.Application.ExecutablePath,
                    Verb = "runas",
                    UseShellExecute = true,
                    Arguments = "--elevated" // Add argument to indicate this is an elevated restart
                };
                
                // Start the new process first
                System.Diagnostics.Process.Start(startInfo);
                
                // Then exit the current application
                System.Windows.Forms.Application.Exit();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to restart as admin", ex);
                MessageBox.Show($"Failed to restart as administrator: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
