using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ORB4
{
    public partial class LoginWindowDark : Form
    {
        public LoginWindowDark()
        {
            InitializeComponent();
        }

        private void LoginWindow_Load(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.ControlBox = false;

            if (string.IsNullOrEmpty(textBox1.Text))
            {
                MessageBox.Show("You have to enter both username and password.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            APIKeyGrabber.Username = textBox1.Text;
            APIKeyGrabber.Password = textBox2.Text;

            button1.Text = "Logging in...";
            button1.Enabled = false;

            Task task = APIKeyGrabber.Run(new Action(() => {
                if (APIKeyGrabber.APIKey.Contains("Error:"))
                {
                    MessageBox.Show(APIKeyGrabber.APIKey, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    this.Invoke(new Action(() => { button1.Enabled = true; button1.Text = "Log in"; this.ControlBox = true; }));
                }
                else
                {
                    MessageBox.Show("Successfully got the API Key.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    if (System.Windows.Forms.MessageBox.Show("Do you want to save your API key on this computer? It could be used by bad guys for malicious activities.", "Question", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.Yes)
                    {
                        string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        if (!System.IO.Directory.Exists($"{AppData}\\ORB\\Private\\"))
                            System.IO.Directory.CreateDirectory($"{AppData}\\ORB\\Private\\");

                        System.IO.File.WriteAllText($"{AppData}\\ORB\\Private\\Key", APIKeyGrabber.APIKey);
                    }
                    Engine.ApiKey = APIKeyGrabber.APIKey;
                    this.Invoke(new Action(() => { this.Close(); }));
                }
            }));
        }
    }
}
