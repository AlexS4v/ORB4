using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System.IO;
using static System.BitConverter;

namespace ORB4
{
    class Engine : IDisposable
    {
        public enum DownloadMirrors
        {
            Hexide, Bloodcat 
        }

        public const string Version = "4.2.1T";

        public const int MaxRequestsPerMinute = 350;

        private int _requestsCount = 0;

        private Queue<Beatmap> _downloadedBeatmaps = new Queue<Beatmap>();

        private Queue<string> _uncheckedLinks = new Queue<string>();

        private List<int> _testedBeatmapsets = new List<int>();
        private List<int> _testedBeatmaps = new List<int>();
        private Queue<int[]> _untestedBeatmaps = new Queue<int[]>();

        private List<Task> _tasks;

        private CancellationTokenSource _cancellationTokenSource;

        private string _lastError = string.Empty;
        private bool _invalidApiKey = false;

        private Semaphore _downloadedSemaphore = new Semaphore(1, 1);
        private Semaphore _testedSemaphore = new Semaphore(1, 1);

        public static Random RandomHelper = new Random();
        public static Beatmap LastBeatmap { get; set; } = null;

        //private BeatmapsCache _rankedCache = new BeatmapsCache($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\RankedCache");
        //private BeatmapsCache _unrankedCache = new BeatmapsCache($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\UnrankedCache");
        //private BeatmapsCache _lovedCache = new BeatmapsCache($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\LovedCache");

        private BeatmapsCache _rankedCache, _unrankedCache, _lovedCache;

        public enum RankStatus
        {
            Approved = 2, Qualified = 3, Loved = 4, Ranked = 1, Pending = 0, WIP = -1,
            Graveyarded = -2
        }

        public enum Modes
        {
            Standard = 0,
            Taiko = 1,
            CatchTheBeat = 2,
            Mania = 3
        }

        public enum Genres
        {
            Any = 0,
            Unspecified = 1,
            VideoGame = 2,
            Anime = 3,
            Rock = 4,
            Pop = 5,
            Other = 6,
            Novelty = 7,
            HipHop = 9,
            Electronic = 10
        }

        public JArray ConvertBeatmapsToCompressedJArray(Beatmap[] beatmaps)
        {
            var array = new JArray();

            foreach (var beatmap in beatmaps)
            {
                array.Add(new JObject()
                {
                    { "id", beatmap.BeatmapsetId.ToString() }, { "artist", beatmap.Artist },
                    { "creator", beatmap.Creator }, { "title", beatmap.Title }
                });
            }

            return array;
        }

        private int _foundCount;

        public Beatmap[] GetRankedPage(int index)
        {
            return _rankedCache.ReadBeatmapsPage(index);
        }

        public Beatmap[] GetRanked()
        {
            return _rankedCache.ReadBeatmaps();
        }

        public Beatmap[] GetUnrankedPage(int index)
        {
            return _unrankedCache.ReadBeatmapsPage(index);
        }

        public Beatmap[] GetUnranked()
        {
            return _unrankedCache.ReadBeatmaps();
        }

        public Beatmap[] GetLovedPage(int index)
        {
            return _unrankedCache.ReadBeatmapsPage(index);
        }

        public Beatmap[] GetLoved()
        {
            return _lovedCache.ReadBeatmaps();
        }

        public string GetRankedPageJson(int index)
        {
            return _rankedCache.GetBeatmapsPageJson(index);
        }

        public string GetRankedJson()
        {
            return _rankedCache.GetBeatmapsJson();
        }

        public string GetLovedPageJson(int index)
        {
            return _lovedCache.GetBeatmapsPageJson(index);
        }

        public string GetLovedJson()
        {
            return _lovedCache.GetBeatmapsJson();
        }

        public string GetUnrankedPageJson(int index)
        {
            return _unrankedCache.GetBeatmapsPageJson(index);
        }

        public string GetUnrankedJson()
        {
            return _unrankedCache.GetBeatmapsJson();
        }

        public string GetAPIKeySha512()
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(ApiKey);
            using (var hash = System.Security.Cryptography.SHA512.Create())
            {
                var hashedInputBytes = hash.ComputeHash(bytes);

                var hashedInputStringBuilder = new System.Text.StringBuilder(128);
                foreach (var b in hashedInputBytes)
                    hashedInputStringBuilder.Append(b.ToString("X2"));
                return hashedInputStringBuilder.ToString().ToLower();
            }
        }

        const string ErrorJsonSchema = @"{
        'error': {'type': 'string'}, }";

        public class Beatmap
        {
            [JsonProperty("mode")]
            public Modes Mode { get; set; }

            [JsonProperty("approved")]
            public RankStatus RankStatus { get; set; }

            [JsonProperty("approved_date")]
            public string ApprovedDate { get; set; }

            [JsonProperty("last_update")]
            public string LastUpdate { get; set; }

            [JsonProperty("difficultyrating")]
            public double DifficultyRating { get; set; }

            [JsonProperty("Artist")]
            public string Artist { get; set; }

            [JsonProperty("creator")]
            public string Creator { get; set; }

            [JsonProperty("beatmap_id")]
            public int BeatmapId { get; set; }

            [JsonProperty("beatmapset_id")]
            public int BeatmapsetId { get; set; }

            [JsonProperty("genre_id")]
            public Genres Genre { get; set; }

            [JsonProperty("bpm")]
            public float BPM { get; set; }

            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("total_length")]
            public int TotalLength { get; set; }
        }

        public class BeatmapsCache : IDisposable
        {
            private FileStream _stream;

            public Engine Engine { get; private set; }
            public string Path { get; private set; }

            public BeatmapsCache(string path, Engine engine)
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);

                Engine = engine;

                _stream = new FileStream(path, FileMode.CreateNew);
                Path = path;
            }

            private void WriteBytes(byte[] bytes)
            {
                _stream.Write(bytes, 0, bytes.Length);
            }

            private byte[] ReadBytes(int length)
            {
                byte[] bytes = new byte[length];
                _stream.Read(bytes, 0, length);
                return bytes;
            }

            private int ReadInt()
            {
                byte[] bytes = new byte[4];
                _stream.Read(bytes, 0, 4);

                return ToInt32(bytes, 0);
            }

            private string ReadString()
            {
                ushort length = ToUInt16(ReadBytes(2), 0);
                byte[] bytes = ReadBytes(length);

                return Encoding.UTF8.GetString(bytes);
            }

            public string GetBeatmapsJson()
            {
                if (_stream.Length > 0)
                {
                    _stream.Position = 0;

                    var array = new JArray();
                    int count = ToInt32(ReadBytes(4), 0);

                    for (int i = 0; i < count; i++)
                    {
                        int size = ReadInt();

                        array.Add(new JObject()
                        {
                            { "id", ToInt32(ReadBytes(4), 0) }, { "b_id", ToInt32(ReadBytes(4), 0) }, { "title", ReadString() },
                            { "artist", ReadString() }, { "creator", ReadString() },
                        });
                    }

                    return array.ToString(Formatting.None);
                }
                else
                {
                    return "[]";
                }
            }

            public const int PageSize = 10;

            public string GetBeatmapsPageJson(int index)
            {
                if (_stream.Length > 0)
                {
                    _stream.Position = 0;

                    int count = ToInt32(ReadBytes(4), 0);
                    int pages = count / PageSize;

                    var array = new JArray();

                    for (int i = 0; i < count; i++)
                    {
                        int size = ReadInt();

                        if (i >= PageSize * index && i < PageSize + (PageSize * index))
                        {
                            array.Add(new JObject()
                                {
                                    { "id", ToInt32(ReadBytes(4), 0) }, { "b_id", ToInt32(ReadBytes(4), 0) }, { "title", ReadString() },
                                    { "artist", ReadString() }, { "creator", ReadString() },
                                });
                        }
                        else
                            _stream.Position += size;
                    }

                    return array.ToString(Formatting.None);
                }
                else
                {
                    return "[]";
                }
            }

            public Beatmap[] ReadBeatmapsPage(int index)
            {
                if (_stream.Length > 0)
                {
                    _stream.Position = 0;

                    int count = ToInt32(ReadBytes(4), 0);
                    int pages = count / PageSize;

                    Beatmap[] beatmaps = new Beatmap[count - PageSize * pages + 1];

                    for (int i = 0; i < count; i++)
                    {
                        int size = ReadInt();

                        if (i >= PageSize * index && i < PageSize + (PageSize * index))
                        {
                            beatmaps[i - PageSize * pages] = new Beatmap()
                            {
                                BeatmapsetId = ToInt32(ReadBytes(4), 0),
                                BeatmapId = ToInt32(ReadBytes(4), 0),
                                Title = ReadString(),
                                Artist = ReadString(),
                                Creator = ReadString(),
                            };
                        }
                        else
                            _stream.Position += size;
                    }

                    return beatmaps;
                }
                else
                {
                    return new Beatmap[0];
                }
            }

            public Beatmap[] ReadBeatmaps()
            {
                if (_stream.Length > 0)
                {
                    _stream.Position = 0;

                    int count = ToInt32(ReadBytes(4), 0);
                    Beatmap[] beatmaps = new Beatmap[count];

                    for (int i = 0; i < beatmaps.Length; i++)
                    {
                        int size = ReadInt();

                        beatmaps[i] = new Beatmap()
                        {
                            BeatmapsetId = ToInt32(ReadBytes(4), 0),
                            BeatmapId = ToInt32(ReadBytes(4), 0),
                            Title = ReadString(),
                            Artist = ReadString(),
                            Creator = ReadString(),
                        };
                    }

                    return beatmaps;
                }
                else
                {
                    return new Beatmap[0];
                }

            }

            public void WriteBeatmaps(Beatmap[] beatmaps)
            {
                if (beatmaps.Length > 0)
                {
                    if (_stream.Length == 0)
                    {
                        WriteBytes(GetBytes(beatmaps.Length));
                    }
                    else
                    {
                        long previousPos = _stream.Position;
                        _stream.Position = 0;
                        int count = ToInt32(ReadBytes(4), 0);
                        _stream.Position = 0;
                        WriteBytes(GetBytes((Int32)(beatmaps.Length + count)));
                        _stream.Position = previousPos;
                    }

                    foreach (var beatmap in beatmaps)
                    {
                        byte[] bmSetId = GetBytes((UInt32)beatmap.BeatmapsetId);
                        byte[] bmId = GetBytes((UInt32)beatmap.BeatmapId);
                        byte[] title = Encoding.UTF8.GetBytes(beatmap.Title);
                        byte[] artist = Encoding.UTF8.GetBytes(beatmap.Artist);
                        byte[] creator = Encoding.UTF8.GetBytes(beatmap.Creator);

                        int beatmapFileSize = bmSetId.Length + bmId.Length + 2 + title.Length + 2 + artist.Length + 2 + creator.Length;

                        WriteBytes(GetBytes(beatmapFileSize));

                        WriteBytes(bmSetId);
                        WriteBytes(bmId);

                        WriteBytes(GetBytes((ushort)title.Length)); WriteBytes(title);
                        WriteBytes(GetBytes((ushort)artist.Length)); WriteBytes(artist);
                        WriteBytes(GetBytes((ushort)creator.Length)); WriteBytes(creator);
                    }
                }
            }

            public void Clear()
            {
                _stream.Close();
                File.Delete(Path);

                _stream = new FileStream(Path, FileMode.CreateNew);
            }

            public void Dispose()
            {
                if (_stream.CanRead || _stream.CanWrite || _stream.CanSeek)
                    _stream.Close();

                File.Delete(Path);
            }
        }

        public void Clear()
        {
            _testedSemaphore.WaitOne();

            try
            {

                _foundCount = 0;

                _lovedCache.Clear();
                _rankedCache.Clear();
                _unrankedCache.Clear();

            }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, $"Engine.Clear -> {e.ToString()}");
            }
            finally
            {
                _testedSemaphore.Release();
            }

        }

        public Beatmap[] FoundBeatmaps
        {
            get
            {
                List<Beatmap> beatmaps = new List<Beatmap>();
                beatmaps.AddRange(_rankedCache.ReadBeatmaps());
                beatmaps.AddRange(_unrankedCache.ReadBeatmaps());
                beatmaps.AddRange(_lovedCache.ReadBeatmaps());

                return beatmaps.ToArray();
            }
        }

        public bool Ripple {
            get {
                return LocalSettings.Ripple;
            }
        }

        public class Settings
        {
            public Settings() { }

            [JsonConstructor]
            public Settings(int[] RankStatus, int[] Modes, int[] Genres)
            {
                this.RankStatus = new HashSet<Engine.RankStatus>();
                foreach (var rank in RankStatus)
                    this.RankStatus.Add((Engine.RankStatus)rank);

                this.Modes = new HashSet<Engine.Modes>();
                foreach (var mode in Modes)
                    this.Modes.Add((Engine.Modes)mode);

                this.Genres = new HashSet<Engine.Genres>();
                foreach (var genre in Genres)
                    this.Genres.Add((Engine.Genres)genre);
            }

            public HashSet<RankStatus> RankStatus { get; set; } = new HashSet<Engine.RankStatus> { Engine.RankStatus.Approved,
                Engine.RankStatus.Ranked, Engine.RankStatus.Loved, Engine.RankStatus.Qualified};
            public HashSet<Modes> Modes { get; set; } = new HashSet<Modes> { Engine.Modes.Standard };
            public HashSet<Genres> Genres { get; set; } = new HashSet<Genres>() { Engine.Genres.Anime, Engine.Genres.Electronic, Engine.Genres.Pop, Engine.Genres.HipHop, Engine.Genres.Novelty, Engine.Genres.Other, Engine.Genres.Rock, Engine.Genres.Unspecified, Engine.Genres.VideoGame };
            public bool OldBeatmapsB { get; set; } = true;
            public bool NewBeatmapsB { get; set; } = true;

            public float MinBPM { get; set; } = 0;
            public float MaxBPM { get; set; } = 250;
            public int MinLength { get; set; } = 0;
            public int MaxLength { get; set; } = 600;
            public float MaxStars { get; set; } = 100;
            public float MinStars { get; set; } = 0;

            public bool Ripple { get; set; } = false;
            public bool AnyDifficulty { get; set; } = true;
            public bool AnyLength { get; set; } = true;
            public bool AnyBPM { get; set; } = true;
            public bool NightMode { get; set; } = false;

            public static Settings Load()
            {
                try
                {
                    string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    if (System.IO.File.Exists($"{AppData}\\ORB\\Private\\Settings"))
                        return JsonConvert.DeserializeObject<Settings>(System.IO.File.ReadAllText($"{AppData}\\ORB\\Private\\Settings"));
                    else
                        return new Settings();
                }
                catch
                {
                    return new Settings();
                }
            }

            public void Save()
            {
                string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!System.IO.Directory.Exists($"{AppData}\\ORB\\Private\\"))
                    System.IO.Directory.CreateDirectory($"{AppData}\\ORB\\Private\\");

                System.IO.File.WriteAllText($"{AppData}\\ORB\\Private\\Settings", Newtonsoft.Json.JsonConvert.SerializeObject(this));
            }

            public bool AutoOpen { get; set; } = true;
            public bool SoundEffects { get; set; } = true;
            public bool OpenInGame { get; set; } = false;

            public DownloadMirrors Mirror { get; set; } = DownloadMirrors.Bloodcat;
        }

        public List<int> _processedBeatmaps = new List<int>();

        private async Task ProcessBeatmaps()
        {
            while (Running)
            {
                try
                {
                    await Task.Delay(1);

                    if (ApiKey == string.Empty && !Ripple)
                    {
                        _lastError = "Invalid API key";
                        _invalidApiKey = true;
                    }

                    if (_invalidApiKey)
                        continue;

                    while (_downloadedBeatmaps.Count > 0)
                    {
                        Beatmap beatmap = _downloadedBeatmaps.Dequeue();

                        bool diff = beatmap.DifficultyRating <= LocalSettings.MaxStars
                            && beatmap.DifficultyRating >= LocalSettings.MinStars;

                        bool bpm = beatmap.BPM <= LocalSettings.MaxBPM &&
                            beatmap.BPM >= LocalSettings.MinBPM;

                        bool length = beatmap.TotalLength <= LocalSettings.MaxLength
                                && beatmap.TotalLength >= LocalSettings.MinLength;

                        DateTime update_date = DateTime.ParseExact(beatmap.LastUpdate, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                        bool mode = LocalSettings.Modes.Any(x => x == beatmap.Mode) || LocalSettings.Modes.Count == 0;

                        bool genre = LocalSettings.Genres.Any(x => x == beatmap.Genre) || LocalSettings.Genres.Count == 0;

                        if (LocalSettings.Ripple)
                            genre = true;

                        bool ranking = LocalSettings.RankStatus.Any(x => x == beatmap.RankStatus);
                        bool oldB = LocalSettings.OldBeatmapsB ? update_date.Year <= 2011 : false;
                        bool newB = LocalSettings.NewBeatmapsB ? update_date.Year > 2011 : false;

                        if (LocalSettings.AnyDifficulty)
                            diff = true;

                        if (LocalSettings.AnyLength)
                            length = true;

                        if (LocalSettings.AnyBPM)
                            bpm = true;

                        if (diff && length && mode && genre && ranking && (oldB || newB) && bpm)
                        {
                            if (LocalSettings.AutoOpen)
                            {
                                if (Running)
                                {
                                    if (LocalSettings.OpenInGame)
                                        Process.Start($"osu://b/{beatmap.BeatmapId}");
                                    else
                                    {
                                       if (!LocalSettings.Ripple)
                                            Process.Start($"https://osu.ppy.sh/b/{beatmap.BeatmapId}");
                                        else
                                            Process.Start($"https://ripple.moe/b/{beatmap.BeatmapId}");
                                    }

                                    if (LocalSettings.SoundEffects)
                                    {
                                        if ((int)beatmap.RankStatus > 0 && (int)beatmap.RankStatus < 4)
                                        {
                                            Utils.PlayWavAsync(Properties.Resources.Found);
                                        }
                                    }

#pragma warning disable CS4014
                                    ThumbnailDownloader.MainThumbnailDownloader.DownloadThumbnailAsync(beatmap.BeatmapsetId);
#pragma warning restore CS4014 

                                    if ((int)beatmap.RankStatus <= 0)
                                    {
                                        _unrankedCache.WriteBeatmaps(new Beatmap[] { beatmap });
                                    }
                                    else if ((int)beatmap.RankStatus > 0 && (int)beatmap.RankStatus < 4)
                                    {
                                        _rankedCache.WriteBeatmaps(new Beatmap[] { beatmap });
                                    }
                                    else if ((int)beatmap.RankStatus == 4)
                                    {
                                        _lovedCache.WriteBeatmaps(new Beatmap[] { beatmap });
                                    }

                                    Stop();
                                    break;
                                }
                            }


                            if (!_processedBeatmaps.Any(x => x == beatmap.BeatmapsetId))
                            {
                                _processedBeatmaps.Add(beatmap.BeatmapsetId);
                                _foundCount++;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                                ThumbnailDownloader.MainThumbnailDownloader.DownloadThumbnailAsync(beatmap.BeatmapsetId);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                                if ((int)beatmap.RankStatus <= 0)
                                {
                                    _unrankedCache.WriteBeatmaps(new Beatmap[] { beatmap });
                                }
                                else if ((int)beatmap.RankStatus > 0 && (int)beatmap.RankStatus < 4)
                                {
                                    _rankedCache.WriteBeatmaps(new Beatmap[] { beatmap });
                                }
                                else if ((int)beatmap.RankStatus == 4)
                                {
                                    _lovedCache.WriteBeatmaps(new Beatmap[] { beatmap });
                                }

                                if (LocalSettings.SoundEffects)
                                {
                                    if ((int)beatmap.RankStatus > 0 && (int)beatmap.RankStatus < 4)
                                    {
                                        Utils.PlayWavAsync(Properties.Resources.Found);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.MainLogger.Log(Logger.LogTypes.Error, $"BeatmapsProcessorThread -> {e.ToString()}");
                    continue;
                }
            }
        }

        bool _errorNotified = false;

        public string GetStatus()
        {
            if (!Running)
                return "Stopped";

            if (_foundCount > 0)
            {
                _errorNotified = false;
                return _foundCount.ToString();
            }
            else
            {
                if (_lastError != string.Empty)
                {
                    if (!_errorNotified) {
                        Utils.PlayWavAsync(Properties.Resources.Error);
                        _errorNotified = true;
                    }

                    return _lastError;
                }
                else
                {
                    _errorNotified = false;
                    return "Searching";
                }
            }
        }

        public bool Running { get; private set; }

        public void Stop()
        {
            if (Running)
            {
                _invalidApiKey = false;
                _cancellationTokenSource.Cancel();
                Running = false;
            }
        }

        public async Task RateChecker()
        {
            try
            {
                while (Running)
                {
                    await Task.Delay(1000, _cancellationTokenSource.Token);

                    if (_lastRequestDateTime.Minute != DateTime.Now.Minute)
                    {
                        _rateLimited = false;
                        _requestsCount = 0;
                    }
                    else
                    {
                        if (_requestsCount > MaxRequestsPerMinute)
                            _rateLimited = true;
                    }
                }
            }
            catch (TaskCanceledException)
            { }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e.ToString());
            }
        }

        private DateTime _lastRequestDateTime = DateTime.Now;

        private bool _rateLimited = false;

        public async Task SearchAsync()
        {
            HttpClient client = new HttpClient
            {
                Timeout = new TimeSpan(0, 0, 5)
            };

            client.DefaultRequestHeaders.Add("user-agent", $"ORB ({Version})");

            Logger.MainLogger.Log(Logger.LogTypes.Info, "SearchThread.Start -> Success");

            while (Running)
            {
                bool t_locked = false;
                bool d_locked = false;
                try
                {
                    Console.WriteLine(_requestsCount);

                    if (_rateLimited)
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    if (_invalidApiKey)
                    {
                        await Task.Delay(1000);
                        continue;
                    }
 
                    if (ApiKey != string.Empty || Ripple)
                    {
                        string link = string.Empty;
                        bool method = true; int id = 0;

                        if (_uncheckedLinks.Count > 0)
                        {
                            link = _uncheckedLinks.Dequeue();

                            if (LocalSettings.Ripple) 
                                id = int.Parse(link.Replace($"https://ripple.moe/api/get_beatmaps?k=NULL&s=", string.Empty));
                            else
                                id = int.Parse(link.Replace($"https://osu.ppy.sh/api/get_beatmaps?k={ApiKey}&s=", string.Empty));

                            method = false;
                        }
                        else
                        {
                            method = (RandomHelper.NextDouble() > 0.5);

                            ChooseId:
                            id = method ? RandomHelper.Next(71, 2500000) : RandomHelper.Next(1, 1500000);
                            char mChar = method ? 'b' : 's';

                            if (method == true)
                            {
                                int[] beatmaps = _testedBeatmaps.ToArray();
                                if (beatmaps.Any(x => x == id))
                                    goto ChooseId;
                            }
                            else
                            {
                                int[] beatmapset = _testedBeatmapsets.ToArray();
                                if (beatmapset.Any(x => x == id))
                                    goto ChooseId;
                            }

                            if (LocalSettings.Ripple)
                                link = $"https://ripple.moe/api/get_beatmaps?k=NULL&{mChar}={id}";
                            else
                                link = $"https://osu.ppy.sh/api/get_beatmaps?k={ApiKey}&{mChar}={id}";
                        }

                        HttpResponseMessage response =
                            await client.GetAsync(link, _cancellationTokenSource.Token);

                        Console.WriteLine(link);

                        string json = await response.Content.ReadAsStringAsync();

                        if (json == "null")
                            continue;

                        object obj = JsonConvert.DeserializeObject(json);

                        if (obj is JObject convObj)
                        {
                            if (convObj.IsValid(JsonSchema.Parse(ErrorJsonSchema)))
                            {
                                string error = convObj.Value<string>("error");
                                if (error == "Please provide a valid API key.")
                                {
                                    _lastError = "Invalid API Key";
                                    _invalidApiKey = true;
                                }
                                else 
                                    _lastError = "API Error";

                                Logger.MainLogger.Log(Logger.LogTypes.Warning, $"SearchThread.Response.Error -> {error}.");
                            }
                        }
                        else
                        {
                            try
                            {
                                _requestsCount++;
                                _lastRequestDateTime = DateTime.Now;

                                Beatmap[] beatmaps = ((JArray)obj).ToObject<Beatmap[]>();

                                _downloadedSemaphore.WaitOne();
                                d_locked = true;
                                if (beatmaps.Length > 0)
                                {
                                    foreach (var beatmap in beatmaps)
                                    {
                                        if (!_downloadedBeatmaps.Any(x => x.BeatmapId == beatmap.BeatmapId))
                                            _downloadedBeatmaps.Enqueue(beatmap);
                                    }

                                    if (method == true)
                                    {
                                        if (!LocalSettings.Ripple)
                                            _uncheckedLinks.Enqueue($"https://osu.ppy.sh/api/get_beatmaps?k={ApiKey}&s={beatmaps[0].BeatmapsetId}");
                                        else
                                            _uncheckedLinks.Enqueue($"https://ripple.moe/api/get_beatmaps?k={ApiKey}&s={beatmaps[0].BeatmapsetId}");
                                    }
                                }
                                d_locked = false;
                                _downloadedSemaphore.Release();

                                _testedSemaphore.WaitOne();
                                t_locked = true;
                                if (method == true)
                                {
                                    if (!_testedBeatmaps.Any(x => x == id))
                                        _testedBeatmaps.Add(id);
                                }
                                else
                                {
                                    if (!_testedBeatmapsets.Any(x => x == id))
                                        _testedBeatmapsets.Add(id);
                                }
                                t_locked = false;
                                _testedSemaphore.Release();

                                _lastError = string.Empty;
                            }
                            catch (Exception e)
                            {
                                Logger.MainLogger.Log(Logger.LogTypes.Error, $"SearchThread -> {e.ToString()}");
                                _lastError = "Unknown <br> error";
                            }
                        }
                    }
                    else
                    {
                        _lastError = "Invalid API key";
                        _invalidApiKey = true;
                    }
                }
                catch (HttpRequestException e)
                {
                    Logger.MainLogger.Log(Logger.LogTypes.Error, $"SearchThread -> {e.ToString()}");
                    _lastError = "Network <br> Error";
                    await Task.Delay(1000);
                    continue;
                }
                catch (TaskCanceledException e)
                {
                    Logger.MainLogger.Log(Logger.LogTypes.Info, $"SearchThread.Cancel -> Success");
                }
                catch (Exception e)
                {
                    Logger.MainLogger.Log(Logger.LogTypes.Error, $"SearchThread -> {e.ToString()}");
                    _lastError = "Unknown <br> Error";
                    continue;
                }
                finally
                {
                    if (t_locked)
                        _testedSemaphore.Release();

                    if (d_locked)
                        _downloadedSemaphore.Release();
                }
            }
        }

        public Settings LocalSettings { get; set; } = new Settings();
        public string ApiKey { get; set; }

        public int FoundCurrentSearch {
            get
            {
                return _foundCount;
            }
        } 

        public void Start()
        {
            _errorNotified = false;

            if (!LocalSettings.OldBeatmapsB && !LocalSettings.NewBeatmapsB)
            {
                if (System.Windows.Forms.MessageBox.Show("Please, enable at least old or new beatmaps in the settings in order to search.", "...Really?", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Exclamation) == System.Windows.Forms.DialogResult.OK)
                { return; }
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _invalidApiKey = false;
            _lastError = string.Empty;

            _foundCount = 0;

            _downloadedBeatmaps.Clear();
            _uncheckedLinks.Clear();

            Running = true;

            _tasks.Clear();

            if (LocalSettings.AutoOpen)
                Threads = 8;
            else
                Threads = 2;

            for (int i = 0; i < Threads; i++)
                _tasks.Add(new Task(async () => await SearchAsync()));

            for (int i = 0; i < Threads; i++)
                _tasks[i].Start();

            Task.Factory.StartNew(async () => await ProcessBeatmaps());
            Task.Factory.StartNew(async () => await RateChecker());
            Logger.MainLogger.Log(Logger.LogTypes.Info, "SearchEngine.Start -> Success");
        }

        public void Dispose()
        {
            _cancellationTokenSource.Dispose();

            _lovedCache.Dispose();
            _rankedCache.Dispose();
            _unrankedCache.Dispose();
        }

        public int Threads { get; private set; }


        //private BeatmapsCache _rankedCache = new BeatmapsCache($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\RankedCache");
        //private BeatmapsCache _unrankedCache = new BeatmapsCache($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\UnrankedCache");
        //private BeatmapsCache _lovedCache = new BeatmapsCache($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\LovedCache");

        public Engine()
        {
            string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            _rankedCache = new BeatmapsCache($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\RankedCache", this);
            _unrankedCache = new BeatmapsCache($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\UnrankedCache", this);
            _lovedCache = new BeatmapsCache($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\LovedCache", this);

            _cancellationTokenSource = new CancellationTokenSource();
            Threads = 0;
            _tasks = new List<Task>();
        }
    }
}
