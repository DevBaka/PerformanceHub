using System;
using System.Windows.Forms;

namespace DJWinOptimizer.UI
{
    public class ProgressForm : Form
    {
        private readonly ProgressBar _progressBar;
        private readonly Label _statusLabel;
        private int _totalItems;

        public ProgressForm(string title, int totalItems)
        {
            _totalItems = totalItems;
            this.Text = title;
            this.Size = new System.Drawing.Size(400, 150);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.ControlBox = false;

            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20)
            };

            _statusLabel = new Label
            {
                Text = "Processing...",
                Location = new System.Drawing.Point(0, 0),
                AutoSize = true
            };

            _progressBar = new ProgressBar
            {
                Location = new System.Drawing.Point(0, 30),
                Size = new System.Drawing.Size(340, 25),
                Style = ProgressBarStyle.Continuous
            };

            panel.Controls.AddRange(new Control[] { _statusLabel, _progressBar });
            this.Controls.Add(panel);
        }

        public void UpdateProgress(int current, string status)
        {
            _progressBar.Value = (int)((double)current / _totalItems * 100);
            _statusLabel.Text = status;
            Application.DoEvents();
        }
    }
}
