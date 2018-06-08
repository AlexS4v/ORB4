using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ORB4.Updater
{
    class Operation
    {
        public string Description { get; set; }
        public string Name { get; set; }
        public Func<Task> Main { get; set; }
        public Func<Task> Rollback { get; set; }
    }



    class Install
    {

        public static string Reverse(string s)
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }
        
        string _link;
        string _version;
        string _tempFile;
        public string CurrentDescription { get; set; } = "Install";

        public string Path { get; set; }

        int _previousPercentage = 0;
        public int Percentage { get; set; } = 0;

        public const string GetLink = "http://zusupedl.altervista.org/orb-link";
        public const string GetVersion = "http://zusupedl.altervista.org/orb-version";

        public Install()
        {
            Operations = new List<Operation> {
            new Operation()
            {
                Name = "DL#0",
                Description = "Downloading information...",
                Main = new Func<Task>(async () => {
                    using (HttpClient client = new HttpClient())
                    {
                        _link = await (await client.GetAsync(GetLink)).Content.ReadAsStringAsync();
                        Percentage += 1000;
                        _version = await (await client.GetAsync(GetVersion)).Content.ReadAsStringAsync();
                        Percentage += 1000;
                    }
                })
            },

            new Operation()
            {
                Name = "DL#1",
                Description = "Downloading data...",
                Main = new Func<Task>(async () => {
                    using (HttpClient client = new HttpClient())
                    {
                        HttpResponseMessage message = await client.GetAsync(_link, HttpCompletionOption.ResponseHeadersRead);

                        long? totalLength = message.Content.Headers.ContentLength;
                        System.IO.Stream stream = await message.Content.ReadAsStreamAsync();

                        _tempFile = System.IO.Path.GetTempFileName();
                        System.IO.FileStream fsStream = new System.IO.FileStream(_tempFile, System.IO.FileMode.Create);

                        byte[] buffer = new byte[8192];
                        long currentLength = 0;

                        int len = 0;
                        while ((len = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            fsStream.Write(buffer, 0, len);
                            currentLength += len;
                            double percentage = (double)currentLength / (totalLength ?? -currentLength) * 5500;
                            Percentage = _previousPercentage + ((percentage > 0) ? (int)percentage : 0);
                        }

                        Percentage = 7500;

                        fsStream.Close();
                    }
                }),
            },


            new Operation()
            {
                Name = "EX#0",
                Description = "Extracting files...",
                Main = new Func<Task>(async () => {
                    string tempPath = System.IO.Path.GetTempFileName();
                    System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.Open(_tempFile, System.IO.Compression.ZipArchiveMode.Read);
                    System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();

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
                                    filename = Reverse(filename);
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
                                dir = Reverse(dir);
                            }

                            if (!System.IO.Directory.Exists(Path + dir))
                            {
                                System.IO.Directory.CreateDirectory(Path + dir);
                                dir = System.IO.Path.Combine(Path + dir);
                            }
                            else
                                dir = System.IO.Path.Combine(Path + dir);
                        }
                        else
                        {
                            filename = archive.Entries[i].FullName;
                            dir = Path;
                        }

                        if (filename != string.Empty) {

                            var fileStream = File.Create(dir + filename);
                            Stream stream = archive.Entries[i].Open();
                            await stream.CopyToAsync(fileStream);
                            stream.Close();
                            fileStream.Close();
                        }

                        Percentage = _previousPercentage + (int)((double)(i+1) / (double)archive.Entries.Count * 2500);
                    }

                    archive.Dispose();
                }),
            },

            new Operation()
            {
                
            }

            };
        }

        public List<Operation> Operations { get; set; }

        public bool Running { get; set; }

        public async Task Clear()
        {
            System.IO.File.Delete(_tempFile);
        }

        public async Task Start()
        {
            Running = true;
            foreach (var op in Operations)
            {
                Console.WriteLine(op.Name);
                CurrentDescription = op.Description;
                _previousPercentage = Percentage;
                var result = Task.Factory.StartNew(() => op.Main.Invoke().GetAwaiter().GetResult());
                await result;
            }

            await Clear();

            Running = false;
        }
    }
}
