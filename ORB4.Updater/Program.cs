using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ORB4.Updater
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0)
            {
                if (args[0] == "--update")
                    Application.Run(new MainWindow(new Update()));
                if (args[0] == "--uninstall")
                    return;
                if (args[0] == "--install")
                {
                    Application.Run(new MainWindow(new Install()));
                }
                //TODO
            }
            else
            {
                Application.Run(new MainWindow(new Install()));
            }
        }
    }
}
