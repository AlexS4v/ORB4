using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ORB4
{
    class ThumbnailsDownloader : IDisposable
    {
        public static ThumbnailsDownloader MainThumbnailsDownloader;

        private class Download
        {
            public int Id { get; set; }
            public int Status { get; set; }
            public long Pos { get; set; }
            public int Length { get; set; }
        }

        private List<Download> _downloads;
        private Semaphore _writeSemaphore;
        private Semaphore _getSemaphore;
        private Semaphore _downloadsSemaphore;

        public async Task<byte[]> GetThumbnail(int id)
        {
            Download download = null;

            await Task.Delay(100);

            _downloadsSemaphore.WaitOne();
            {
                if (_downloads.Any(x => x.Id == id))
                {
                    download = _downloads.First(x => x.Id == id);
                }
            }
            _downloadsSemaphore.Release();

            if (download == null)
            {
                return Properties.Resources._13;
            }
            else
            {
                if (download.Status == 0)
                {
                    do
                    {
                        _downloadsSemaphore.WaitOne();
                        {
                            download = _downloads.First(x => x.Id == id);
                        }
                        _downloadsSemaphore.Release();
                        await Task.Delay(50);
                    } while (download.Status == 0);
                }
                else if (download.Status == -1)
                {
                    return Properties.Resources._13;
                }
                
                return ReadFromStream(download.Pos, download.Length);
            }
        }

        private byte[] ReadFromStream(long pos, int length)
        {
            byte[] buffer = new byte[length];
            _writeSemaphore.WaitOne();
            {
                long currentPos = _stream.Position;

                _stream.Position = pos;
                _stream.Read(buffer, 0, length);
                _stream.Position = currentPos;
            }
            _writeSemaphore.Release();

            return buffer;
        }

        private void CopyStream(Stream input)
        {
            _writeSemaphore.WaitOne();
            {
                byte[] buffer = new byte[8 * 1024];
                int len;

                while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    _stream.Write(buffer, 0, len);
                }
            }
            _writeSemaphore.Release();
        }

        public async Task DownloadThumbnailAsync(int id)
        {
            _downloadsSemaphore.WaitOne();
            {
                if (_downloads.Any(x => x.Id == id))
                {
                    if (_downloads.First(x => x.Id == id).Id == -1)
                        _downloads.RemoveAll(x => x.Id == id);
                    else
                    {
                        _downloadsSemaphore.Release();
                        return;
                    }
                }
            }
            _downloadsSemaphore.Release();

            _downloads.Add(new Download() { Id = id, Status = 0 });

            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage message = await client.GetAsync($"https://assets.ppy.sh/beatmaps/{id}/covers/list@2x.jpg");

                if (message.IsSuccessStatusCode)
                {
                    using (Stream netStream = await message.Content.ReadAsStreamAsync())
                    {
                        _downloadsSemaphore.WaitOne();
                        {
                            Download download = _downloads.First(x => x.Id == id);
                            download.Length = (int)netStream.Length;
                            download.Pos = _stream.Position;
                            download.Status = 1;
                        }
                        _downloadsSemaphore.Release();

                        CopyStream(netStream);
                    }
                }
                else
                {
                    _downloadsSemaphore.WaitOne();
                    {
                        _downloads.First(x => x.Id == id).Status = -1;
                    }
                    _downloadsSemaphore.Release();
                }
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
            _writeSemaphore.Dispose();
            _getSemaphore.Dispose();

            System.IO.File.Delete($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\ThumbnailsCache");
        }

        private FileStream _stream;

        public ThumbnailsDownloader()
        {
            if (System.IO.File.Exists($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\ThumbnailsCache"))
                System.IO.File.Delete($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\ThumbnailsCache");

            _stream = new FileStream($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\ThumbnailsCache", FileMode.CreateNew);

            _downloads = new List<Download>();
            _downloadsSemaphore = new Semaphore(1, 1);
            _writeSemaphore = new Semaphore(1, 1);
            _getSemaphore = new Semaphore(1, 1);
        }
    }
}
