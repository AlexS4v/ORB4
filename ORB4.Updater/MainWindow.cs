using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ORB4.Updater
{
    public partial class MainWindow : Form
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindow_Load(object sender, EventArgs e)
        {
            label1.Location = new Point((this.Width - label1.Width) / 2 - 9, label1.Location.Y);
            label2.Location = new Point((this.Width - label2.Width) / 2 - 9, label2.Location.Y);
            this.Icon = Properties.Resources.Main;
            install = new Install();
            Timer.Start();
        }

        Install install;

        private void button1_Click(object sender, EventArgs e)
        {
            Task.Factory.StartNew(install.Start);
            button1.Enabled = false;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            progressBar1.Value = install.Percentage;
            button1.Text = install.CurrentDescription;
        }
    }
}
