using System;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;

namespace PerformanceHub.UI
{
    partial class ProcessPickerForm
    {
        private System.ComponentModel.IContainer components = null;
        private TextBox txtFilter;
        private Button btnRefresh;
        private ListView listProcesses;
        private ColumnHeader colName;
        private ColumnHeader colPid;
        private Button btnOk;
        private Button btnCancel;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            txtFilter = new TextBox();
            btnRefresh = new Button();
            listProcesses = new ListView();
            colName = new ColumnHeader();
            colPid = new ColumnHeader();
            btnOk = new Button();
            btnCancel = new Button();

            SuspendLayout();

            Text = "Pick Running Processes";
            Width = 520; Height = 520;
            StartPosition = FormStartPosition.CenterParent;

            txtFilter.PlaceholderText = "Filter by name...";
            txtFilter.Location = new System.Drawing.Point(10, 10);
            txtFilter.Width = 360;

            btnRefresh.Text = "Refresh";
            btnRefresh.Location = new System.Drawing.Point(380, 8);
            btnRefresh.Width = 110;

            listProcesses.Location = new System.Drawing.Point(10, 40);
            listProcesses.Size = new System.Drawing.Size(480, 400);
            listProcesses.View = View.Details;
            listProcesses.MultiSelect = true;
            listProcesses.FullRowSelect = true;
            listProcesses.Columns.AddRange(new ColumnHeader[] { colName, colPid });
            colName.Text = "Process"; colName.Width = 340;
            colPid.Text = "PID"; colPid.Width = 100;

            btnOk.Text = "OK"; btnOk.Location = new System.Drawing.Point(320, 450); btnOk.DialogResult = DialogResult.OK;
            btnCancel.Text = "Cancel"; btnCancel.Location = new System.Drawing.Point(410, 450); btnCancel.DialogResult = DialogResult.Cancel;

            Controls.Add(txtFilter);
            Controls.Add(btnRefresh);
            Controls.Add(listProcesses);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            ResumeLayout(false);
        }
    }
}
