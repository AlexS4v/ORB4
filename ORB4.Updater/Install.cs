using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using IWshRuntimeLibrary;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ORB4.Updater
{
    class Utils
    {
        public static async Task CloseProcesses(string hash, string path)
        {
            var processes = System.Diagnostics.Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    if (CalculateSHA512FromPath(process.MainModule.FileName) == hash)
                    {
                        if (process.HasExited)
                            continue;

                        process.CloseMainWindow();
                        await Task.Delay(3000);

                        if (!process.HasExited)
                            process.Kill();
                    }
                }
                catch { continue; }
            } 
        }

        public static string Reverse(string s)
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }

        public static string CalculateSHA512FromPath(string path)
        {
            using (var algorithm = SHA512.Create())
            {
                byte[] hash = algorithm.ComputeHash(new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
            }
        }

        public static string[] GetAllFilesFromPath(string path)
        {
            List<string> files = new List<string>();
            files.AddRange(System.IO.Directory.GetFiles(path));

            string[] dirs = System.IO.Directory.GetDirectories(path);
            foreach (var dir in dirs)
            {
                files.AddRange(GetAllFilesFromPath(dir));
            }

            return files.ToArray();
        }
    }

    class Install : Process
    {
        string _link;
        string _version;
        string _tempFile;
        
        public const string GetLink = "http://zusupedl.altervista.org/orb-link";
        public const string GetVersion = "http://zusupedl.altervista.org/orb-version";

        public void AddRollbackOperation(Action action)
        {
            RollbackOperations.Add(action);
        }

        public async Task DownloadData()
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    const long TimeoutLimit = 10000;

                    CancellationTokenSource.Token.ThrowIfCancellationRequested();
                    HttpResponseMessage message = await client.GetAsync(_link, HttpCompletionOption.ResponseHeadersRead);

                    long? totalLength = message.Content.Headers.ContentLength;
                    System.IO.Stream stream = await message.Content.ReadAsStreamAsync();

                    _tempFile = System.IO.Path.GetTempFileName();

                    AddRollbackOperation(() =>
                    System.IO.File.Delete(_tempFile));

                    System.IO.FileStream fsStream = new System.IO.FileStream(_tempFile, System.IO.FileMode.Create);

                    AddRollbackOperation(() =>
                    {
                        if (fsStream.CanRead || fsStream.CanWrite)
                            fsStream.Close();
                    });

                    byte[] buffer = new byte[8192];
                    long currentLength = 0;

                    int len = 0;

                    bool finished = false;

                    System.Threading.CancellationTokenSource cancSrc =
                        new System.Threading.CancellationTokenSource();

                    DateTime time = DateTime.Now;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task timeoutTask = Task.Run(async () =>
                    {
                        while (!finished)
                        {
                            if ((DateTime.Now - time).TotalMilliseconds > TimeoutLimit)
                            {
                                cancSrc.Cancel();
                                stream.Close();
                                break;
                            }
                            else
                                await Task.Delay(1);
                        }
                    });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                    while ((len = await stream.ReadAsync(buffer, 0, buffer.Length, cancSrc.Token)) > 0)
                    {
                        if (timeoutTask.IsCompleted)
                            timeoutTask.Start();

                        time = DateTime.Now;

                        CancellationTokenSource.Token.ThrowIfCancellationRequested();
                        fsStream.Write(buffer, 0, len);
                        currentLength += len;
                        double percentage = (double)currentLength / (totalLength ?? -currentLength) * 5500;
                        Percentage = _previousPercentage + ((percentage > 0) ? (int)percentage : 0);
                    }

                    finished = true;

                    Percentage = 7500;

                    fsStream.Close();

                }
                catch (HttpRequestException)
                {
                    System.Windows.Forms.MessageBox.Show("Cannot download program data. Please, check your internet connection.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    await OperationsRollback();
                    Environment.Exit(-1);
                }
                catch (ObjectDisposedException)
                {
                    System.Windows.Forms.MessageBox.Show("Cannot download program data. Please, check your internet connection.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    await OperationsRollback();
                    Environment.Exit(-1);
                }
                catch (System.OperationCanceledException)
                {
                    await OperationsRollback();
                    Environment.Exit(0);
                }
                catch (Exception e)
                {
                    System.Windows.Forms.MessageBox.Show("Oops, that's so embarrassing... An error occurred: " + e.ToString(), "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    CurrentDescription = "Operations rollback...";
                    await OperationsRollback();
                    Environment.Exit(e.HResult);
                }
            }

        }

        public async Task DownloadInfo()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    _link = await (await client.GetAsync(GetLink)).Content.ReadAsStringAsync();
                    Percentage += 1000;
                    _version = await (await client.GetAsync(GetVersion)).Content.ReadAsStringAsync();
                    Percentage += 1000;
                }
            }
            catch (HttpRequestException)
            {
                System.Windows.Forms.MessageBox.Show("Cannot download the needed information to install the program. Please, check your internet connection.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                Environment.Exit(-1);
            }
            catch (System.OperationCanceledException)
            {
                Environment.Exit(0);
            }
            catch (Exception e)
            {
                System.Windows.Forms.MessageBox.Show("Oops, that's so embarrassing... An error occurred: " + e.ToString(), "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                CurrentDescription = "Operations rollback...";
                Environment.Exit(e.HResult);
            }
        }
        Dictionary<string, byte> _installedComponents = new Dictionary<string, byte>();

        public async Task ConfigureInstallation()
        {
            try
            {
                RegistryKey SoftwareKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                CancellationTokenSource.Token.ThrowIfCancellationRequested();
                RegistryKey parent = SoftwareKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true);

                if (parent.OpenSubKey("Osu! Random Beatmap") != null) { parent.DeleteSubKey("Osu! Random Beatmap"); }

                var orbKey =
                  parent.CreateSubKey("Osu! Random Beatmap");

                orbKey.SetValue("DisplayIcon", Path + "\\ORB4.exe", RegistryValueKind.String);
                orbKey.SetValue("DisplayName", "Osu! Random Beatmap", RegistryValueKind.String);
                orbKey.SetValue("DisplayVersion", _version, RegistryValueKind.String);
                orbKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                orbKey.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
                orbKey.SetValue("InstallLocation", Path, RegistryValueKind.String);
                orbKey.SetValue("UninstallString", Path + "\\ORB4.exe --uninstall", RegistryValueKind.String);
                orbKey.SetValue("NoModify", 1, RegistryValueKind.DWord);

                orbKey.Close();
                parent.Close();

                parent = null;
                orbKey = null;

                AddRollbackOperation(() =>
                {
                    if (parent == null)
                        parent = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true);

                    parent.DeleteSubKey("ORB");
                    parent.Close();

                    parent = null;
                });
                CancellationTokenSource.Token.ThrowIfCancellationRequested();
                System.Windows.Forms.DialogResult result = System.Windows.Forms.MessageBox.Show("Create a desktop shortcut?", "Question", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question);
                if (result == System.Windows.Forms.DialogResult.Yes)
                {
                    object shDesktop = (object)"Desktop";
                    WshShell shell = new WshShell();

                    string shortcutAddress = (string)shell.SpecialFolders.Item(ref shDesktop) + @"\ORB4.lnk";
                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutAddress);
                    shortcut.Description = "A program that gives you random beatmaps for the game osu!";
                    shortcut.TargetPath = Path + "\\ORB4.exe";
                    shortcut.Save();

                    AddRollbackOperation(() =>
                    {
                        System.IO.File.Delete(shortcutAddress);
                    });
                }

                CancellationTokenSource.Token.ThrowIfCancellationRequested();
                result = System.Windows.Forms.MessageBox.Show("Create a Quick Menu shortcut?", "Question", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question);
                if (result == System.Windows.Forms.DialogResult.Yes)
                {
                    WshShell shell = new WshShell();

                    string path = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) + "\\ORB4.lnk";

                    string shortcutAddress = path;
                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutAddress);
                    shortcut.Description = "A program that gives you random beatmaps for the game osu!";
                    shortcut.TargetPath = Path + "\\ORB4.exe";
                    shortcut.Save();

                    AddRollbackOperation(() =>
                    {
                        System.IO.File.Delete(shortcutAddress);
                    });
                }

                using (FileStream fs = new FileStream(Path + "unins.dat", FileMode.CreateNew, FileAccess.Write)) {

                    foreach (var component in _installedComponents)
                    {
                        byte[] filename = Encoding.UTF8.GetBytes(component.Key);
                        byte[] length = BitConverter.GetBytes((ushort)filename.Length);

                        await fs.WriteAsync(new byte[] { component.Value }, 0, 1);
                        await fs.WriteAsync(length, 0, length.Length);
                        await fs.WriteAsync(filename, 0, filename.Length);
                    }
                }

                CancellationTokenSource.Cancel();
            }
            catch (System.OperationCanceledException)
            {
                await OperationsRollback();
                Environment.Exit(0);
            }
            catch (Exception e)
            {
                System.Windows.Forms.MessageBox.Show("Oops, that's so embarrassing... An error occurred: " + e.ToString(), "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                CurrentDescription = "Operations rollback...";
                await OperationsRollback();
                Environment.Exit(e.HResult);
            }
        }

        public async Task ExtractFiles()
        {
            try
            {
                CancellationTokenSource.Token.ThrowIfCancellationRequested();

                if (Path[Path.Length - 1] != '\\')
                {
                    Path += "\\";
                }

                System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.Open(_tempFile, System.IO.Compression.ZipArchiveMode.Read);

                AddRollbackOperation(() =>
                {
                    archive.Dispose();
                });

                for (int i = 0; i < archive.Entries.Count; i++)
                {
                    string filename = string.Empty;
                    string dir = string.Empty;

                    if (archive.Entries[i].FullName.Contains("/"))
                    {
                        bool gotSymbol = false;

                        for (int k = archive.Entries[i].FullName.Length - 1; k > -1; k--)
                        {
                            if (archive.Entries[i].FullName[k] == '/' && !gotSymbol)
                            {
                                dir += '/';
                                filename = Utils.Reverse(filename);
                                gotSymbol = true;
                                continue;
                            }

                            if (!gotSymbol)
                                filename += archive.Entries[i].FullName[k];
                            else
                            {
                                dir += archive.Entries[i].FullName[k];
                            }
                        }

                        if (dir != string.Empty)
                        {
                            dir = Utils.Reverse(dir);
                        }

                        if (!System.IO.Directory.Exists(Path + dir))
                        {
                            System.IO.Directory.CreateDirectory(Path + dir);
                            _installedComponents.Add(Path + dir.Replace("/","\\"), 255);

                            dir = System.IO.Path.Combine(Path + dir);
                            
                            AddRollbackOperation(() =>
                            {
                                if (System.IO.Directory.Exists(dir))
                                    System.IO.Directory.Delete(dir, true);
                            });
                        }
                        else
                        {
                            dir = System.IO.Path.Combine(Path + dir);
                        }
                    }
                    else
                    {
                        filename = archive.Entries[i].FullName;
                        dir = Path;
                    }

                    if (filename != string.Empty)
                    {

                        var fileStream = System.IO.File.Create(dir + filename);

                        AddRollbackOperation(() => System.IO.File.Delete(dir + filename));

                        Stream stream = archive.Entries[i].Open();
                        await stream.CopyToAsync(fileStream);
                        stream.Close();
                        fileStream.Close();

                        _installedComponents.Add((dir + filename).Replace("/", "\\"), 0);
                    }

                    Percentage = _previousPercentage + (int)((double)(i + 1) / (double)archive.Entries.Count * 2500);
                }

                System.IO.File.WriteAllText($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\Version", _version, System.Text.Encoding.ASCII);
                
                AddRollbackOperation(() => System.IO.File.Delete($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\Version"));

                CancellationTokenSource.Token.ThrowIfCancellationRequested();
                archive.Dispose();
            }
            catch (System.OperationCanceledException)
            {
                await OperationsRollback();
                Environment.Exit(0);
            }
            catch (Exception e)
            {
                System.Windows.Forms.MessageBox.Show("Oops, that's so embarrassing... An error occurred: " + e.ToString(), "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                CurrentDescription = "Operations rollback...";
                await OperationsRollback();
                Environment.Exit(e.HResult);
            }
        }

        public Install()
        {
            Operations = new List<Operation> {
            new Operation()
            {
                Name = "DL#0",
                Description = "Downloading information...",
                Main = new Func<Task>(DownloadInfo)
            },

            new Operation()
            {
                Name = "DL#1",
                Description = "Downloading data...",
                Main = new Func<Task>(DownloadData)
            },


            new Operation()
            {
                Name = "EX#0",
                Description = "Extracting files...",
                Main = new Func<Task>(ExtractFiles)
            },

            new Operation()
            {
                Main = new Func<Task>(ConfigureInstallation),
                Name = "CFG#1",
                Description = "Finalizing installation..."
            }

            };
        }
        
        public override async Task Clear()
        {
            System.IO.File.Delete(_tempFile);
        }
    }
}
