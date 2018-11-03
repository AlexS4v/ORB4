using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ORB4.Updater
{
    class Uninstall : Process
    {
        List<string> _backupFiles = new List<string>();

        public async override Task Clear()
        {
            foreach (var file in _backupFiles)
            {
                if (System.IO.File.Exists(file))
                {
                    try
                    {
                        System.IO.File.Delete(file);
                    }
                    catch { continue; }
                }
            }
        }

        Dictionary<string, byte> _componentsToUninstall = new Dictionary<string, byte>();

        public async Task DeleteFiles()
        {
            KeyValuePair<string,byte>[] files = _componentsToUninstall.Where(x => x.Value == 0).ToArray();
            KeyValuePair<string, byte>[] dirs = _componentsToUninstall.Where(x => x.Value == 255).ToArray();

            foreach (var file in files)
            {
                if (System.IO.File.Exists(file.Key))
                {
                    string temp = System.IO.Path.GetTempFileName();
                    System.IO.File.Copy(file.Key, temp, true);

                    AddRollbackOperation(() => {
                        if (System.IO.File.Exists(file.Key))
                            System.IO.File.Delete(file.Key);

                        System.IO.File.Copy(temp, file.Key, true);

                        System.IO.File.Delete(temp);
                    });

                    try
                    {
                        System.IO.File.Delete(file.Key);
                    } catch (Exception e) {
                        Console.WriteLine(e);
                        continue; }
                }

                Percentage += (5000 / files.Length);
            }

            Percentage = 2500 + 5000;

            foreach (var dir in dirs)
            {
                if (System.IO.Directory.Exists(dir.Key))
                {
                    AddRollbackOperation(() =>
                    {
                        if (!System.IO.Directory.Exists(dir.Key))
                            System.IO.Directory.CreateDirectory(dir.Key);
                    });

                    try
                    {
                        System.IO.Directory.Delete(dir.Key);
                    } catch (Exception e) {
                        System.Windows.Forms.MessageBox.Show("Oops, that's so embarrassing... An error occurred: " + e.ToString(), "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                        CurrentDescription = "Operations rollback...";
                        await OperationsRollback();
                        Environment.Exit(e.HResult);
                    }
                }
                
                Percentage += (2500 / dirs.Length);
            }

            Percentage = 10000;
        }

        Dictionary<string, object> _valueNamesBackup = new Dictionary<string, object>();

        public async Task ReadUninsFile()
        {
            try
            {
                RegistryKey SoftwareKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32);
                Microsoft.Win32.RegistryKey orbKey =
                    SoftwareKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Osu! Random Beatmap", true);
                
                if (orbKey == null)
                {
                    System.Windows.Forms.MessageBox.Show("ORB is not installed. If it is, the program may be not properly installed. You should try to install it again.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    Environment.Exit(-1);
                }
                else
                {
                    string[] valueNames = orbKey.GetValueNames();

                    foreach (var valueName in valueNames)
                    {
                        _valueNamesBackup.Add(valueName, orbKey.GetValue(valueName));
                    }

                    Path = (string)orbKey.GetValue("InstallLocation");
                    string dataPath = Path + "unins.dat";

                    string[] exes = Utils.GetAllFilesFromPath(Path).Where(x => {
                        if (x.Length > 4)
                        {
                            if (x.Substring(x.Length - 4, 4) == ".exe")
                                return true;
                            else
                                return false;
                        }
                        else
                            return false;
                    }).ToArray();

                    foreach (var exe in exes)
                    {
                        await Utils.CloseProcesses(Utils.CalculateSHA512FromPath(exe), exe);
                    }


                    System.IO.FileStream fs = new System.IO.FileStream(dataPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);

                    while (fs.Position <= fs.Length - 1)
                    {
                        byte[] type = new byte[1];
                        byte[] filenameLength = new byte[2];
                        byte[] filename;

                        await fs.ReadAsync(type, 0, type.Length);
                        await fs.ReadAsync(filenameLength, 0, filenameLength.Length);

                        ushort length = BitConverter.ToUInt16(filenameLength, 0);
                        filename = new byte[length];

                        await fs.ReadAsync(filename, 0, filename.Length);

                        string encodedFilename = Encoding.UTF8.GetString(filename);

                        if (type[0] == 0)
                        {
                            byte[] hash = new byte[64];
                            await fs.ReadAsync(hash, 0, hash.Length);

                            if (Utils.CalculateSHA512BytesFromPath(encodedFilename).SequenceEqual(hash))
                                _componentsToUninstall.Add(encodedFilename, type[0]);
                        }
                        else
                            _componentsToUninstall.Add(encodedFilename, type[0]);

                    }
                }

                Percentage += 2500;
            }
            catch (Exception e)
            {
                System.Windows.Forms.MessageBox.Show("Oops, that's so embarrassing... An error occurred: " + e.ToString(), "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                CurrentDescription = "Operations rollback...";
                await OperationsRollback();
                Environment.Exit(e.HResult);
            }
        }

        public async Task FinalizeUninstallation()
        {
            try
            {
                System.IO.File.Delete(Path + "unins.dat");
            } catch { }

            try
            {
                Microsoft.Win32.RegistryKey rootKey =
                    Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\", true);

                AddRollbackOperation(() => {
                    Microsoft.Win32.RegistryKey orbKey = rootKey.CreateSubKey("Osu! Random Beatmap");
                    foreach (var valueName in _valueNamesBackup)
                    {
                        orbKey.SetValue(valueName.Key, valueName.Value);
                    }
                });

                rootKey.DeleteSubKey("Osu! Random Beatmap");
            }
            catch (Exception e)
            {
                System.Windows.Forms.MessageBox.Show("Oops, that's so embarrassing... An error occurred: " + e.ToString() + "\n\nThe program may be not properly installed. You should try to install it again.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                CurrentDescription = "Operations rollback...";
                await OperationsRollback();
                Environment.Exit(e.HResult);
            }
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
                    Name = "CFG#1",
                    Description = "Finalizing...",
                    Main = new Func<Task>(FinalizeUninstallation)
                }
            };
        }
    }
}
