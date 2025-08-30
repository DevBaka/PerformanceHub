using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace DJWinOptimizer.UI
{
    public partial class ProcessPickerForm : Form
    {
        public IReadOnlyList<string> SelectedExecutables { get; private set; } = Array.Empty<string>();

        public ProcessPickerForm()
        {
            InitializeComponent();

            btnRefresh.Click += (_, __) => LoadProcesses();
            txtFilter.TextChanged += (_, __) => LoadProcesses();
            btnOk.Click += (_, __) => OnOk();

            LoadProcesses();
        }

        private void LoadProcesses()
        {
            try
            {
                var filter = (txtFilter.Text ?? string.Empty).Trim();
                var procs = Process.GetProcesses();
                var items = new List<ListViewItem>();
                foreach (var p in procs)
                {
                    string name;
                    try { name = (p.MainModule?.FileName != null) ? System.IO.Path.GetFileName(p.MainModule.FileName) : p.ProcessName + ".exe"; }
                    catch { name = p.ProcessName + ".exe"; }
                    if (!string.IsNullOrWhiteSpace(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var lvi = new ListViewItem(new[] { name, p.Id.ToString() }) { Tag = name };
                    items.Add(lvi);
                }
                // Distinct by name, prefer lower PID
                items = items
                    .GroupBy(i => (string)i.Tag!, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderBy(i => int.Parse(i.SubItems[1].Text)).First())
                    .OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                listProcesses.BeginUpdate();
                listProcesses.Items.Clear();
                listProcesses.Items.AddRange(items.ToArray());
                listProcesses.EndUpdate();
            }
            catch
            {
                // ignore transient access errors
            }
        }

        private void OnOk()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ListViewItem it in listProcesses.SelectedItems)
            {
                if (it.Tag is string s && !string.IsNullOrWhiteSpace(s)) names.Add(s);
            }
            SelectedExecutables = names.ToList();
        }
    }
}
