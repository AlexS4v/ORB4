using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ORB4.Updater
{
    class Update : Process
    {
        private string _version;
        private string _link;

        private string _tempFile;

        List<string> _filesToUpdate = new List<string>();
        Dictionary<string, byte> _installedComponents = new Dictionary<string, byte>();

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

        public async Task VerifyInstallation()
        {
            try
            {
                var serializer = new JavaScriptSerializer();

                RegistryKey SoftwareKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32);
                CancellationTokenSource.Token.ThrowIfCancellationRequested();
                RegistryKey parent = SoftwareKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true);

                RegistryKey orbKey;

                if ((orbKey = parent.OpenSubKey("Osu! Random Beatmap")) == null)
                {
                    System.Windows.Forms.MessageBox.Show("ORB is not installed. If it is, the program may be not properly installed. You should try to install it again.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    Environment.Exit(-1);
                }

                Dictionary<string, string> hashset = new Dictionary<string, string>();

                using (HttpClient client = new HttpClient())
                {
                    _version = await (await client.GetAsync(Install.GetVersion)).Content.ReadAsStringAsync();
                    _link = await (await client.GetAsync(Install.GetLink)).Content.ReadAsStringAsync();

                    string json = await (await client.GetAsync($"http://zusupedl.altervista.org/hashes/{_version}.json")).Content.ReadAsStringAsync();
                    hashset = serializer.Deserialize<Dictionary<string, string>>(json);
                }

                Percentage += 1000;

                Path = (string)orbKey.GetValue("InstallLocation"); 

                List<string> files = Utils.GetAllFilesFromPath(Path).ToList();

                string[] exes = files.Where(x => x.Substring(x.Length - 4, 4) == ".exe").ToArray();
                files.RemoveAll(x => exes.Any(y => y == x));

                foreach (var exe in exes)
                {
                    string hash = Utils.CalculateSHA512FromPath(exe);
                    string filename = exe.Replace(Path, "").Replace("\\", "/").ToLower();

                    if (hashset.Any(x => x.Key == filename))
                    {
                        await Utils.CloseProcesses(hash, Path);
                        if (hashset.First(x => x.Key == filename).Value != hash)
                        {
                            _filesToUpdate.Add(exe);
                        }
                        else
                        {
                            hashset.Remove(filename);
                        }
                    }
                }

                foreach (var file in files)
                {
                    string hash = Utils.CalculateSHA512FromPath(file);
                    string filename = file.Replace(Path, "").Replace("\\", "/");

                    if (hashset.Any(x => x.Key == filename))
                    {
                        if (hashset.First(x => x.Key == filename).Value != hash)
                        {
                            _filesToUpdate.Add(file);
                        }
                        else
                        {
                            hashset.Remove(filename);
                        }
                    }
                }

                foreach (var file in hashset)
                {
                    _filesToUpdate.Add(file.Key);
                }

                Percentage += 1000;

                if (_filesToUpdate.Count == 0)
                {
                    Percentage += 8000;
                    Running = false;
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
                System.Windows.Forms.MessageBox.Show("Oops, that's so embarrassing... An error occurred: " + e.ToString() + "\n\nThe program may be not properly installed. You should try to install it again.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                CurrentDescription = "Operations rollback...";
                await OperationsRollback();
                Environment.Exit(e.HResult);
            }
        }

        public async Task FinalizeUpdate()
        {
            if (System.IO.File.Exists(Path + "unins.dat"))
            {
                string temp = System.IO.Path.GetTempFileName();
                System.IO.File.Copy(Path + "unins.dat", temp, true);
                System.IO.File.Delete(Path + "unins.dat");

                AddRollbackOperation(() =>
                {
                    System.IO.File.Delete(Path + "unins.dat");
                    System.IO.File.Copy(temp, Path + "unins.dat");
                    System.IO.File.Delete(temp);
                });
            }

            using (FileStream fs = new FileStream(Path + "unins.dat", FileMode.CreateNew, FileAccess.Write))
            {

                foreach (var component in _installedComponents)
                {
                    byte[] filename = Encoding.UTF8.GetBytes(component.Key);
                    byte[] length = BitConverter.GetBytes((ushort)filename.Length);

                    await fs.WriteAsync(new byte[] { component.Value }, 0, 1);
                    await fs.WriteAsync(length, 0, length.Length);
                    await fs.WriteAsync(filename, 0, filename.Length);

                    if (component.Value != 255)
                    {
                        byte[] hash = Utils.CalculateSHA512BytesFromPath(component.Key);
                        await fs.WriteAsync(hash, 0, hash.Length);
                    }
                }
            }
        }

        public Update()
        {
            Operations = new List<Operation>
            {
                new Operation()
                {
                    Name = "VER#1",
                    Description = "Verifying installation...",
                    Main = new Func<Task>(VerifyInstallation)
                },

                new Operation()
                {
                    Name = "DL#1",
                    Description = "Downloading data...",
                    Main = new Func<Task>(DownloadData)
                },

                new Operation()
                {
                    Name = "EX#1",
                    Description = "Extracting files...",
                    Main = new Func<Task>(ExtractFiles)
                },

                new Operation()
                {
                    Name = "UP#1",
                    Description = "Finalizing update...",
                    Main = new Func<Task>(FinalizeUpdate)
                }
            };
        }

        Dictionary<string, string> _backupFiles = new Dictionary<string, string>();

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
                    string rawDir = string.Empty;

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

                        rawDir = dir;

                        if (!System.IO.Directory.Exists(Path + dir))
                        {
                            System.IO.Directory.CreateDirectory(Path + dir);
                            _installedComponents.Add(Path + dir.Replace("/", "\\"), 255);

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
                        if (_filesToUpdate.Any(x => rawDir + x.ToLower() == archive.Entries[i].FullName.ToLower()))
                        {
                            if (System.IO.File.Exists(dir + filename))
                            {
                                string temp = System.IO.Path.GetTempFileName();

                                System.IO.File.Delete(temp);

                                System.IO.File.Copy(dir + filename, temp);
                                _backupFiles.Add(archive.Entries[i].FullName, temp);

                                AddRollbackOperation(() =>
                                {
                                    System.IO.File.Delete(dir + filename);
                                    System.IO.File.Copy(temp, dir + filename);
                                    System.IO.File.Delete(temp);
                                });
                            }

                            var fileStream = System.IO.File.Create(dir + filename);

                            Stream stream = archive.Entries[i].Open();
                            await stream.CopyToAsync(fileStream);
                            stream.Close();
                            fileStream.Close();

                            _installedComponents.Add((dir + filename).Replace("/", "\\"), 0);
                        }
                    }

                    Percentage = _previousPercentage + (int)((double)(i + 1) / (double)archive.Entries.Count * 2500);
                }

                System.IO.File.WriteAllText($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\Version", _version, System.Text.Encoding.ASCII);

                AddRollbackOperation(() => System.IO.File.Delete($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\Version"));

                Percentage = _previousPercentage + 2500;

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

        public async override Task Clear()
        {
            try
            {
                if (_tempFile != string.Empty)
                    System.IO.File.Delete(_tempFile);

                foreach (var backup in _backupFiles)
                {
                    try
                    {
                        System.IO.File.Delete(backup.Value);
                    } catch { continue; }
                }

            } catch { return; }
        }
    }
}
