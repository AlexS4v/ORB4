using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using IWshRuntimeLibrary;

namespace ORB4.Updater
{
    partial class MainWindow : Form
    {
        public MainWindow(Process process)
        {
            InstallationProcess = process;
            InitializeComponent();
        }

        private void MainWindow_Load(object sender, EventArgs e)
        {
            if (MessageBox.Show("Please, read the following license which this software is distributed with and press YES if you want to proceed or NO if don't want to. \n\n --------------- \n\n" + Properties.Resources.License, "License", MessageBoxButtons.YesNo) == DialogResult.No)
            { 
                Environment.Exit(0);
            }
                
            label1.Location = new Point((this.Width - label1.Width) / 2 - 9, label1.Location.Y);
            label2.Location = new Point((this.Width - label2.Width) / 2 - 9, label2.Location.Y);
            this.Icon = Properties.Resources.Main;

            InstallationProcess.OnInstallationFinish += InstallationFinish;
        }

        private void InstallationFinish(object sender, EventArgs e)
        {
            this.Invoke(new Action(() => { 
                Timer.Stop();
                button1.Text = "Installed";

                MessageBox.Show("ORB was successfully installed. We hope you'll enjoy our program!", 
                    "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);

                Environment.Exit(0);
            }));
        }

        private void InstallationError(object sender, EventArgs e)
        {
            button1.Text = "An error occurred";
        }

        Process InstallationProcess { get; set; }

        private void button1_Click(object sender, EventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                InstallationProcess.Path = dialog.SelectedPath;
            }
            else
            {
                return;
            }

            Timer.Start();
            Task.Factory.StartNew(InstallationProcess.Start);
            button1.Enabled = false;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            progressBar1.Value =InstallationProcess.Percentage;
            button1.Text = InstallationProcess.CurrentDescription + $" {((double)InstallationProcess.Percentage /10000.0)*100}%";
        }

        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (InstallationProcess.Running) {
                if (InstallationProcess.CurrentDescription.Contains("Rollback requested..."))
                {
                    e.Cancel = true;
                    return;
                }

                if (MessageBox.Show("Are you sure to stop the current operation?", 
                    "Question", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
                {
                    e.Cancel = true;

                    InstallationProcess.CancellationTokenSource.Cancel();
                    InstallationProcess.CurrentDescription = "Rollback requested...";
                    this.ControlBox = false;
                }
                else
                {
                    e.Cancel = true;
                }
            }
        }
    }
}
