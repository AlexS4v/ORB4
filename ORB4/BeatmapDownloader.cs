﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ORB4
{
    class BeatmapDownloader
    {
        public enum DLStatus
        {
            Pending = 0,
            Downloading = 1,
            Success = 2,
            Error = 3,
            Stopped = 4
        }

        private class DL
        {
            public int Percentage { get; set; }
            public DLStatus Status { get; set; }

            public string Url { get; set; }

            public string Artist { get; set; }
            public string Author { get; set; }
            public string Title { get; set; }

            internal bool holdingSemaphore = false;
            internal CancellationTokenSource threadToken = new CancellationTokenSource();

            public int RankingStatus { get; set; }

            public int Beatmapset_Id { get; set; }
            public int Id { get; set; }

            public bool F { get; set; } = false;
        }

        HttpClient _searchClient;

        int dl_c = 0;

        Engine _engine;

        SemaphoreSlim _dlSemaphore;
        SemaphoreSlim _listSemaphore;

        List<DL> _dls = new List<DL>();

        public async Task<string> DownloadStatusAsync(int id)
        {
            string out_ = string.Empty;

            await _listSemaphore.WaitAsync();
            try { out_ = JsonConvert.SerializeObject(_dls.First(x => x.Id == id)); }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                _listSemaphore.Release(); return "{\"error\": \"An unknown error has occurred. Check the log file.\"}";
            }
            _listSemaphore.Release();

            return out_;
        }

        public async Task<string> DownloadStatusAsync()
        {
            string out_ = string.Empty;

            await _listSemaphore.WaitAsync();
            try { out_ = JsonConvert.SerializeObject(_dls.FindAll(x => !x.F)); }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                _listSemaphore.Release(); return "{\"error\": \"An unknown error has occurred. Check the log file.\"}";
            }
            _listSemaphore.Release();

            return out_;
        }

        public async Task<int> RegisterDownload(int id, int rankingStatus, string artist, string author, string title)
        {
            string dlLink = string.Empty;
            switch (_engine.LocalSettings.Mirror)
            {
                case Engine.DownloadMirrors.Hexide:
                    dlLink = $"https://osu.hexide.com/beatmaps/{id}/download/novid/no-video.osz";
                    break;
                case Engine.DownloadMirrors.Bloodcat:
                    dlLink = $"https://bloodcat.com/osu/s/{id}";
                    break;
                default:
                    break;
            }

            if (string.IsNullOrEmpty(dlLink))
            {
                throw new ArgumentException("Download link not set: perhaps an unknown mirror has been set (?)");
            }

            DL dl = null;

            await _listSemaphore.WaitAsync();
            try
            {
                if (_dls.Any(x => x.Beatmapset_Id == id && (x.Status == DLStatus.Pending || x.Status == DLStatus.Downloading || x.Status == DLStatus.Success)))
                {
                    _listSemaphore.Release();
                    return -2;
                }

                if (_dls.FindAll(x => x.Status == DLStatus.Pending || x.Status == DLStatus.Downloading).Count >= 10)
                {
                    _listSemaphore.Release();
                    return -3;
                }

                dl = new DL
                {
                    Beatmapset_Id = id,
                    Artist = artist,
                    Title = title,
                    Author = author,
                    Id = dl_c + 1,
                    Url = dlLink,
                    Percentage = 0,
                    RankingStatus = rankingStatus,
                    Status = DLStatus.Pending
                };

                dl_c++;

                _dls.RemoveAll(x => x.Beatmapset_Id == dl.Beatmapset_Id); _dls.Add(dl);
            }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                _listSemaphore.Release(); return -1;
            }
            _listSemaphore.Release();

            Task.Factory.StartNew(async () => { await DownloadBeatmapAsync(dl.Id); });

            return dl.Id;
        }

        public async Task Delete(int id)
        {
            await _listSemaphore.WaitAsync();
            try
            {
                _dls.RemoveAll(x => x.Beatmapset_Id == id);
            }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                _listSemaphore.Release();
            }
            _listSemaphore.Release();
        }

        public async Task<bool> Exists(int id)
        {
            await _listSemaphore.WaitAsync();
            try
            {
                if (_dls.Any(x => x.Beatmapset_Id == id && (x.Status != DLStatus.Error && x.Status != DLStatus.Stopped)))
                {
                    _listSemaphore.Release();
                    return true;
                }
                else
                {
                    _listSemaphore.Release();
                    return false;
                }
            }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                _listSemaphore.Release(); return false;
            }
        }

        public async Task<string> SearchUnranked(string query, int page)
        {
            try
            {
                page += 1;
                SyncAlreadyDownloaded();

                _searchClient.DefaultRequestHeaders.Add("user-agent", $"ORB ({Engine.Version})");

                string json = await
                (await _searchClient.GetAsync($"https://bloodcat.com/osu/?mod=json&q={query}&c=b&p={page}&s=&m=&g=&l="))
                .Content.ReadAsStringAsync();

                JArray beatmaps = JArray.Parse(json);
                JArray beatmaps_patched = new JArray();

                for (int i = beatmaps.Count - 1; i >= 0; i--)
                {
                    JToken beatmap = beatmaps[i];

                    IEnumerable<DL> dls = _dls.FindAll(x => x.Beatmapset_Id == int.Parse((string)beatmap["id"]));

                    HashSet<int> mods = new HashSet<int>();

                    var childs = beatmap["beatmaps"];

                    int b_id = childs[0]["id"].Value<int>();

                    foreach (var child in childs)
                        mods.Add(child["mode"].Value<int>());

                    JObject beatmap_patched = new JObject
                {
                    { "artist", beatmap["artist"] },
                    { "author", beatmap["creator"] },
                    { "title", beatmap["title"] },
                    { "id", beatmap["id"] },
                    { "modes", JArray.FromObject(mods) },
                    { "status", beatmap["status"] },
                    { "b_id", b_id.ToString() },
                    { "dl_status", dls.Count() != 0 && dls.First().Status != DLStatus.Stopped ? JToken.Parse(JsonConvert.SerializeObject(dls.First())) : JToken.Parse("null")}
                };

                    beatmaps_patched.Add(beatmap_patched);
                }

                return beatmaps_patched.ToString(Formatting.None);
            }
            catch (HttpRequestException e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                return "{\"error\" : \"Network error. Check your internet connection.\"}";
            }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                return "{\"error\" : \"Unknown error. Check the log.\"}";
            }
        }

        public async Task<string> Search(string query, int page)
        {
            try
            {
                page += 1;
                SyncAlreadyDownloaded();

                _searchClient.DefaultRequestHeaders.Add("user-agent", $"ORB ({Engine.Version})");

                string json = await
                (await _searchClient.GetAsync($"https://bloodcat.com/osu/?mod=json&q={query}&c=b&s=1,2,3,4&p={page}&m=&g=&l="))
                .Content.ReadAsStringAsync();

                JArray beatmaps = JArray.Parse(json);
                JArray beatmaps_patched = new JArray();

                for (int i = beatmaps.Count - 1; i >= 0; i--)
                {
                    JToken beatmap = beatmaps[i];

                    IEnumerable<DL> dls = _dls.FindAll(x => x.Beatmapset_Id == int.Parse((string)beatmap["id"]));

                    HashSet<int> mods = new HashSet<int>();

                    var childs = beatmap["beatmaps"];

                    int b_id = childs[0]["id"].Value<int>();

                    foreach (var child in childs)
                        mods.Add(child["mode"].Value<int>());

                    JObject beatmap_patched = new JObject
                {
                    { "artist", beatmap["artist"] },
                    { "author", beatmap["creator"] },
                    { "title", beatmap["title"] },
                    { "id", beatmap["id"] },
                    { "modes", JArray.FromObject(mods) },
                    { "status", beatmap["status"] },
                    { "b_id", b_id.ToString() },
                    { "dl_status", dls.Count() != 0 && dls.First().Status != DLStatus.Stopped ? JToken.Parse(JsonConvert.SerializeObject(dls.First())) : JToken.Parse("null")}
                };

                    beatmaps_patched.Add(beatmap_patched);
                }

                return beatmaps_patched.ToString(Formatting.None);
            }
            catch (HttpRequestException e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                return "{\"error\" : \"Network error. Check your internet connection.\"}";
            }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                return "{\"error\" : \"Unknown error. Check the log.\"}";
            }
        }

        public async Task<int> Stop(int id)
        {
            DL dl = null;

            await _listSemaphore.WaitAsync();
            try
            {
                dl = _dls.First(x => x.Id.Equals(id));
            }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                _listSemaphore.Release(); return 500;
            }
            _listSemaphore.Release();

            if (dl.holdingSemaphore)
            {
                Console.WriteLine($"Semaphore_Dl: {_dlSemaphore.CurrentCount}");
                _dlSemaphore.Release();
                dl.holdingSemaphore = false;

                dl.threadToken.Cancel();
            }

            switch (dl.Status)
            {
                case DLStatus.Pending:
                    dl.Status = DLStatus.Stopped;
                    return 200;
                case DLStatus.Downloading:
                    dl.Status = DLStatus.Stopped;
                    return 200;
                default:
                    return 400;
            }
        }

        public async Task<int> RunBeatmap(int id)
        {
            List<DL> dl = null;

            await _listSemaphore.WaitAsync();
            try { dl = _dls.FindAll(x => x.Beatmapset_Id.Equals(id)); }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                _listSemaphore.Release(); return 500;
            }
            _listSemaphore.Release();

            if (dl == null || dl.Count == 0)
            {
                return 404;
            }
            else
            {
                try
                {
                    Process.Start(_path + dl[0].Id.ToString() + ".osz");
                    return 200;
                }
                catch
                {
                    return 500;
                }
            }
        }

        public async Task DownloadBeatmapAsync(int id)
        {
            DL dl = null;

            await _listSemaphore.WaitAsync();
            try {
                var holdingSemaphore = _dls.FindAll(x => x.holdingSemaphore);
                if (_dlSemaphore.CurrentCount + holdingSemaphore.Count != 2)
                {
                    _dlSemaphore.Release(2 - _dlSemaphore.CurrentCount);
                }

                dl = _dls.First(x => x.Id.Equals(id));
            }
            catch (Exception e) { 
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                _listSemaphore.Release(); return;
            }
            _listSemaphore.Release();
            await _dlSemaphore.WaitAsync();

            dl.holdingSemaphore = true;

            if (dl.Status == DLStatus.Stopped)
            {
                return;
            }

            dl.Status = DLStatus.Downloading;
            int retries = 5;

            try
            {
                BeginDL:
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(new Uri(dl.Url));
                    request.UserAgent = $"ORB ({Engine.Version})";

                    var settings = new { bg = false,
                        video = _engine.LocalSettings.BCRemoveVideoStoryboard,
                        skin = _engine.LocalSettings.BCRemoveSkin,
                        cdn = _engine.LocalSettings.BCDownloadFromCDN
                    };

                    string json = JsonConvert.SerializeObject(settings);

                    request.Headers.Add("cookie", $"DLOPT={System.Web.HttpUtility.UrlEncode(json)};");
                    WebResponse response = request.GetResponse();

                    string beatmapFilename = dl.Beatmapset_Id.ToString() + " [ORB] " + dl.Artist + " - " + dl.Title;
                    beatmapFilename = string.Join("_", beatmapFilename.Split(Path.GetInvalidFileNameChars()));

                    string nextBeatmapPath = Utils.GetOsuPath() + "\\Songs\\" + beatmapFilename + ".osz";

                    using (FileStream fs = File.Create(_path + dl.Beatmapset_Id.ToString() + ".osz"))
                    {
                        using (Stream ns = response.GetResponseStream())
                        {
                            long remaining = response.ContentLength;

                            do
                            {
                                if (dl.Status == DLStatus.Stopped)
                                {
                                    fs.Dispose();
                                    ns.Dispose();

                                    File.Delete(_path + dl.Beatmapset_Id.ToString() + ".osz");
                                    if (dl.holdingSemaphore)
                                    {
                                        _dlSemaphore.Release();
                                    }

                                    dl.holdingSemaphore = false;
                                    return;
                                }

                                byte[] buffer = new byte[Environment.SystemPageSize];

                                int length = await ns.ReadAsync(buffer, 0, buffer.Length, dl.threadToken.Token);
                                await fs.WriteAsync(buffer, 0, length);

                                dl.Percentage = (int)(((double)(response.ContentLength - remaining) / response.ContentLength) * 100.0);

                                remaining -= length;
                            } while (remaining > 0);

                            await fs.FlushAsync();

                            dl.Status = DLStatus.Success;
                            if (_engine.LocalSettings.SoundEffects)
                            {
                                Utils.PlayWavAsync(Properties.Resources.Downloaded);
                            }

                            if (dl.holdingSemaphore)
                            {
                                _dlSemaphore.Release();
                            }

                            dl.holdingSemaphore = false;

                            SyncAlreadyDownloaded();
                        }
                    }

                    if (File.Exists(nextBeatmapPath))
                    {
                        File.Delete(nextBeatmapPath);
                    }

                    File.Move(_path + dl.Beatmapset_Id.ToString() + ".osz", nextBeatmapPath);
                }
                catch (Exception e)
                {
                    Logger.MainLogger.Log(Logger.LogTypes.Error, e.ToString());
                    if (retries > 0)
                    {
                        retries--;
                        goto BeginDL;
                    }
                    else
                    {
                        dl.Status = DLStatus.Error;
                        if (dl.holdingSemaphore)
                        {
                            _dlSemaphore.Release();
                        }

                        dl.holdingSemaphore = false;
                    }
                }
            }
            catch (Exception e)
            {
                dl.Status = DLStatus.Error;

                await _listSemaphore.WaitAsync();
                try
                {
                    _dls.RemoveAll(x => x.Id.Equals(id));
                    _dls.Add(dl);
                }
                catch (Exception e2)
                {
                    Logger.MainLogger.Log(Logger.LogTypes.Error, e2);
                    _listSemaphore.Release(); return;
                }
                _listSemaphore.Release();

                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                if (dl.holdingSemaphore)
                {
                    _dlSemaphore.Release();
                }

                dl.holdingSemaphore = false;
                return;
            }
        }

        string _path;

        osu_database_reader.BinaryFiles.OsuDb _osuDb;

        private Semaphore _dbSemaphore;

        private void SyncAlreadyDownloaded()
        {
            _dbSemaphore.WaitOne();

            try
            {
                _dls.RemoveAll(x =>
                {
                    if (x.F)
                    {
                        dl_c--;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                });
                _osuDb = osu_database_reader.BinaryFiles.OsuDb.Read(Utils.GetOsuPath() + "\\osu!.db");
                foreach (var beatmap in _osuDb.Beatmaps)
                {
                    if (!_dls.Any(x => x.Beatmapset_Id == beatmap.BeatmapSetId))
                    {
                        _dls.Add(new DL
                        {
                            Percentage = 100,
                            Status = DLStatus.Success,
                            Beatmapset_Id = beatmap.BeatmapSetId,
                            F = true
                        });

                        dl_c++;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e);
                _dbSemaphore.Release(); return;
            }
            _dbSemaphore.Release();

        }

        public BeatmapDownloader(ref Engine engine)
        {
            _engine = engine;

            _dbSemaphore = new Semaphore(1, 1);
            _dlSemaphore = new SemaphoreSlim(2, 2);
            _listSemaphore = new SemaphoreSlim(1, 1);

            _searchClient = new HttpClient();

            _path = $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\DLs\\";

            if (System.IO.Directory.Exists(_path))
            {
                System.IO.Directory.Delete(_path, true);
            }

            System.IO.Directory.CreateDirectory(_path);

            SyncAlreadyDownloaded();
        }
    }
}
