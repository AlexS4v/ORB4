using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ORB4.Updater
{
    class Uninstall : Process
    {
        public async override Task Clear()
        { }

        Dictionary<string, byte> _componentsToUninstall = new Dictionary<string, byte>();

        public async Task DeleteFiles()
        {
            KeyValuePair<string,byte>[] files = _componentsToUninstall.Where(x => x.Value == 0).ToArray();

            //foreach (var file in files)
            //    System.IO.
        }

        public async Task ReadUninsFile()
        {
            Microsoft.Win32.RegistryKey orbKey =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Osu! Random Beatmap", true);

            if (orbKey == null)
            {
                System.Windows.Forms.MessageBox.Show("ORB is not installed. If it is, the program may be not properly installed. You should try to install it again.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                Environment.Exit(-1);
            }
            else
            {

                Path = (string)orbKey.GetValue("InstallLocation");
                string dataPath = Path + "unins.dat";

                System.IO.FileStream fs = new System.IO.FileStream(dataPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);

                while (fs.Position >= fs.Length - 1)
                {
                    byte[] type = new byte[1];
                    byte[] filenameLength = new byte[2];
                    byte[] filename;

                    await fs.ReadAsync(type, 0, type.Length);
                    await fs.ReadAsync(filenameLength, 0, filenameLength.Length);

                    ushort length = BitConverter.ToUInt16(filenameLength, 0);
                    filename = new byte[length];

                    await fs.ReadAsync(filename, 0, filename.Length);

                    _componentsToUninstall.Add(Encoding.UTF8.GetString(filename), type[0]);
                }
            }
        }

        public async Task FinalizeUninstallation()
        {

        }

        public Uninstall()
        {
            Operations = new List<Operation>
            {
                new Operation()
                {
                    Name = "RD#1",
                    Description = "Collecting files...",
                    Main = new Func<Task>(ReadUninsFile) 
                },

                new Operation()
                {
                    Name = "UN#1",
                    Description = "Uninstalling...",
                    Main = new Func<Task>(DeleteFiles)
                },

                new Operation()
                {
                    Name = "UN#2",
                    Description = "Unininstalling...", 
                }
            };
        }
    }
}
