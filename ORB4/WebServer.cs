using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.Net.NetworkInformation;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace ORB4
{
    class WebServer
    {
        HttpListener _listener;
        Dictionary<string, HtmlResource> _resources;

        public Engine Engine;

        class HtmlResource
        {
            public HtmlResource(int id, string type)
            {
                Id = id;
                Type = type;
            }

            public string Type { get; set; }
            public int Id { get; set; }
        }

        public byte[] Token { get; set; }

        public string Url { get; private set; }

        public WebServer()
        {
            _listener = new HttpListener();
            Engine = new Engine() { ApiKey = "", Settings = ORB4.Engine.SearchSettings.Load() };

            Token = Guid.NewGuid().ToByteArray();

            Logger.MainLogger.Log(Logger.LogTypes.Info, "BrowserObject.RegisterJS -> Success");

            Url = $"http://localhost:{GetAvailablePort(41500)}/";

            _listener.Prefixes.Add(Url);

            _resources = new Dictionary<string, HtmlResource>()
            {
                { "/html/mainwindow.html", new HtmlResource(0, "text/html") },
                { "/html/settings.html", new HtmlResource(1, "text/html") },
                { "/css/main.css", new HtmlResource(2, "text/css") },
                { "/css/svg/settings-icon.svg", new HtmlResource(3, "image/svg+xml") },
                { "/css/fonts/NotoSans-Regular.ttf", new HtmlResource(4, "application/font") },
                { "/css/fonts/NotoSans-Light.ttf", new HtmlResource(5, "application/font") },
                { "/css/fonts/NotoSans-Thin.ttf", new HtmlResource(6, "application/font") },
                { "/html/foundbeatmaps.html", new HtmlResource(10, "text/html") },
                { "/css/svg/close-icon.svg", new HtmlResource(11, "image/svg+xml") },
                { "/jquery", new HtmlResource(12, "application/javascript") },
                { "/gifs/loading.gif", new HtmlResource(14, "image/gif") },
                { "/css/svg/save.svg", new HtmlResource(15, "image/svg+xml") },
                { "/css/svg/clear.svg", new HtmlResource(16, "image/svg+xml") }
            };
        }

        public void Start()
        {
            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();

            _running = true;
            Task.Factory.StartNew(async () => await Update(), System.Threading.CancellationToken.None, TaskCreationOptions.None, scheduler);
        }

        public void Stop()
        {
            _running = false;
        }

        private bool _running = false;

        private async Task BadRequest(HttpListenerContext context)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("Osu! Random Beatmap - Bad Request");

            context.Response.StatusCode = 400;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var cookie = context.Request.Cookies["token"];
                if (cookie != null)
                {
                    if (cookie.Value != Convert.ToBase64String(Token))
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }

                if (_resources.Any(x => x.Key == context.Request.RawUrl))
                {
                    HtmlResource resource = _resources.First(x => x.Key == context.Request.RawUrl).Value;
                    byte[] bytes = (byte[])Properties.Resources.ResourceManager.GetObject($"_{resource.Id}");

                    context.Response.ContentLength64 = bytes.Length;
                    context.Response.ContentType = resource.Type;
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);

                    context.Response.OutputStream.Close();
                }
                else
                {
                    switch (context.Request.RawUrl)
                    {
                        case "/engine/start":
                            Engine.Start();
                            context.Response.StatusCode = 200;
                            byte[] bytes = Encoding.UTF8.GetBytes("Started!");
                            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                            context.Response.OutputStream.Close();
                            break;
                        case "/engine/stop":
                            Engine.Stop();
                            context.Response.ContentType = "text/plain";
                            context.Response.StatusCode = 200;
                            bytes = new byte[] { };

                            if (!Engine.Settings.AutoOpen)
                            {
                                bytes = Encoding.UTF8.GetBytes("Beatmaps_viewer");
                            }

                            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                            context.Response.OutputStream.Close();
                            break;
                        case "/engine/status":
                            bytes = Encoding.UTF8.GetBytes(Engine.GetStatus());

                            context.Response.ContentLength64 = bytes.Length;
                            context.Response.ContentType = "text/plain";
                            context.Response.StatusCode = 200;
                            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                            context.Response.OutputStream.Close();

                            break;
                        case "/engine/beatmaps/ranked":
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "application/json";

                            bytes = Encoding.UTF8.GetBytes(Engine.GetRankedJson());

                            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);

                            context.Response.OutputStream.Close();
                            break;
                        case "/engine/beatmaps/clear":
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "text/plain";

                            Engine.Clear();

                            await context.Response.OutputStream.WriteAsync(new byte [] { }, 0, 0);
                            context.Response.OutputStream.Close();
                            break;
                        case "/engine/beatmaps/loved":
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "application/json";

                            bytes = Encoding.UTF8.GetBytes(Engine.GetLovedJson());

                            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);

                            context.Response.OutputStream.Close();
                            break;
                        case "/engine/beatmaps/unranked":
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "application/json";

                            bytes = Encoding.UTF8.GetBytes(Engine.GetUnrankedJson());

                            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);

                            context.Response.OutputStream.Close();
                            return;
                        case "/engine/beatmaps/save":
                            System.Windows.Forms.SaveFileDialog dialog = new System.Windows.Forms.SaveFileDialog
                            {
                                Filter = "osu!Sync|*.nw520-osbl|Text file|*.txt"
                            };

                            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                            {
                                if (dialog.FilterIndex == 1)
                                    System.IO.File.WriteAllBytes(dialog.FileName, new ExportFormats.OsuSync(Engine.FoundBeatmaps).GetBytes());
                                else if (dialog.FilterIndex == 2)
                                    System.IO.File.WriteAllBytes(dialog.FileName, new ExportFormats.TextFile(Engine.FoundBeatmaps).GetBytes());
                            }

                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "application/json";
                            await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                            context.Response.OutputStream.Close();
                            break;
                        default:
                            if (context.Request.RawUrl.Contains("/engine/settings"))
                            {
                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "application/json";

                                if (context.Request.HttpMethod == "PATCH")
                                {
                                    string query = context.Request.QueryString["p"];
                                    PatchSettings(query);
                                    await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                                }
                                else if (context.Request.HttpMethod == "GET")
                                {
                                    bytes = Encoding.UTF8.GetBytes(GetSettings());
                                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                                }
                                else
                                {
                                    await BadRequest(context);
                                    return;
                                }

                                context.Response.OutputStream.Close();
                                return;
                            }
                            else if (context.Request.RawUrl.Contains("/utils/open_beatmapset"))
                            {
                                string query = context.Request.QueryString["id"];
                                if (query != string.Empty)
                                {
                                    context.Response.StatusCode = 200;
                                    context.Response.ContentType = "text/plain";

                                    if (!int.TryParse(query, out int i))
                                    {
                                        break;
                                    }

                                    if (Engine.Settings.OpenInGame)
                                        Process.Start($"osu://s/{query}");
                                    else
                                        Process.Start($"https://osu.ppy.sh/s/{query}");

                                    await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                                    context.Response.OutputStream.Close();
                                }
                                return;
                            }
                            else if (context.Request.RawUrl.Contains("/images/get_beatmap_image"))
                            {
                                string query = context.Request.QueryString["id"];

                                context.Response.StatusCode = 200;
                                bytes = await ThumbnailsDownloader.MainThumbnailsDownloader.GetThumbnail(int.Parse(query));
                                context.Response.ContentLength64 = bytes.Length;
                                context.Response.ContentType = "image/jpeg";

                                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                                context.Response.OutputStream.Close();
                                return;
                            }else if (context.Request.RawUrl.Contains("/sounds/play"))
                            {
                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "text/plain";

                                if (Engine.Settings.SoundEffects)
                                {
                                    string query = context.Request.QueryString["id"];

                                    switch (query)
                                    {
                                        case "Click":
                                            Utils.PlayWavAsync(Properties.Resources.Click);
                                            break;
                                        case "Switch":
                                            Utils.PlayWavAsync(Properties.Resources.Switch);
                                            break;
                                        default:
                                            break;
                                    }
                                }

                                await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                                context.Response.OutputStream.Close();
                            }

                            await BadRequest(context);
                            break;
                    }
                }
            } catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e.ToString());
            }
        }

        private string GetSettings()
        {
            JArray settings = new JArray
            {
                new JObject() { { "id", "auto_open" }, { "checked", Engine.Settings.AutoOpen.ToString().ToLower()  } },
                new JObject() { { "id", "api_key" }, { "value", (Engine.ApiKey != string.Empty) ? Engine.GetAPIKeySha512() : string.Empty } },
                new JObject() { { "id", "open_in_game" }, { "checked", Engine.Settings.OpenInGame.ToString().ToLower()  } },
                new JObject() { { "id", "old_beatmaps" }, { "checked", Engine.Settings.OldBeatmaps.ToString().ToLower()  } },
                new JObject() { { "id", "max_stars"}, { "value", Engine.Settings.MaxStars.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) } },
                new JObject() { { "id", "min_stars"}, { "value", Engine.Settings.MinStars.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) } },
                new JObject() { { "id", "max_length"}, { "value", Engine.Settings.MaxLength.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) } },
                new JObject() { { "id", "min_length"}, { "value", Engine.Settings.MinLength.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) } },
                new JObject() { { "id", "any_difficulty"}, { "checked", Engine.Settings.AnyDifficulty.ToString().ToLower() } },
                new JObject() { { "id", "any_length"}, { "checked", Engine.Settings.AnyLength.ToString().ToLower() } },
                new JObject() { { "id", "sound_effects"}, { "checked", Engine.Settings.SoundEffects.ToString().ToLower() } }
                
            };

            var modes = Enum.GetValues(typeof(Engine.Modes)).Cast<Engine.Modes>().ToArray();
            var genres = Enum.GetValues(typeof(Engine.Genres)).Cast<Engine.Genres>().ToArray();
            var rankStatus = Enum.GetValues(typeof(Engine.RankStatus)).Cast<Engine.RankStatus>().ToArray();

            foreach (var mode in modes)
            {
                if (mode == Engine.Modes.CatchTheBeat)
                {
                    if (Engine.Settings.Modes.Contains(mode))
                        settings.Add(new JObject() { { "id", "catch_the_beat" }, { "checked", "true" } });
                    else
                        settings.Add(new JObject() { { "id", "catch_the_beat" }, { "checked", "false" } });
                }
                else
                {
                    if (Engine.Settings.Modes.Contains(mode))
                        settings.Add(new JObject() { { "id", mode.ToString().ToLower() }, { "checked", "true" } });
                    else
                        settings.Add(new JObject() { { "id", mode.ToString().ToLower() }, { "checked", "false" } });
                }
            }

            foreach (var status in rankStatus)
            {
                if (Engine.Settings.RankStatus.Contains(status))
                    settings.Add(new JObject() { { "id", status.ToString().ToLower() }, { "checked", "true" } });
                else
                    settings.Add(new JObject() { { "id", status.ToString().ToLower() }, { "checked", "false" } });
            }

            foreach (var genre in genres)
            {
                if (genre == Engine.Genres.HipHop)
                {
                    if (Engine.Settings.Genres.Contains(genre))
                        settings.Add(new JObject() { { "id", "hip_hop" }, { "checked", "true" } });
                    else
                        settings.Add(new JObject() { { "id", "hip_hop" }, { "checked", "false" } });
                }
                else if (genre == Engine.Genres.VideoGame)
                {
                    if (Engine.Settings.Genres.Contains(genre))
                        settings.Add(new JObject() { { "id", "video_game" }, { "checked", "true" } });
                    else
                        settings.Add(new JObject() { { "id", "video_game" }, { "checked", "false" } });
                }
                else
                {
                    if (Engine.Settings.Genres.Contains(genre))
                        settings.Add(new JObject() { { "id", genre.ToString().ToLower() }, { "checked", "true" } });
                    else
                        settings.Add(new JObject() { { "id", genre.ToString().ToLower() }, { "checked", "false" } });
                }
            }

            return settings.ToString(Newtonsoft.Json.Formatting.None);
        }

        private void PatchSettings(string query)
        {
            JObject obj = JObject.Parse(query);
            string id = obj.Value<string>("setting_id");
            string type = obj.Value<string>("type");
            string value = obj.Value<string>("value");
            string check = obj.Value<string>("checked");

            switch (id)
            {
                case "sound_effects":
                    Engine.Settings.SoundEffects = bool.Parse(check);
                    break;
                case "any_difficulty":
                    Engine.Settings.AnyDifficulty = bool.Parse(check);
                    break;
                case "any_length":
                    Engine.Settings.AnyLength = bool.Parse(check);
                    break;
                case "auto_open":
                    Engine.Settings.AutoOpen = bool.Parse(check);
                    break;
                case "open_in_game":
                    Engine.Settings.OpenInGame = bool.Parse(check);
                    break;
                case "old_beatmaps":
                    Engine.Settings.OldBeatmaps = bool.Parse(check);
                    break;
                case "max_stars":
                    try
                    {
                        Engine.Settings.MaxStars = float.Parse(value, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                    }
                    catch
                    {
                        Engine.Settings.MaxStars = 100;
                    }
                    break;
                case "min_stars":
                    try
                    {
                        Engine.Settings.MinStars = float.Parse(value, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                    }
                    catch
                    {
                        Engine.Settings.MinStars = 100;
                    }
                    break;
                case "max_length":
                    try
                    {
                        Engine.Settings.MaxLength = int.Parse(value, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                    }
                    catch
                    {
                        Engine.Settings.MaxLength = 600;
                    }
                    break;
                case "min_length":
                    try
                    {
                        Engine.Settings.MinLength = int.Parse(value, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                    }
                    catch
                    {
                        Engine.Settings.MinLength = 0;
                    }
                    break;
                default:
                    break;
            }

            var modes = Enum.GetValues(typeof(Engine.Modes)).Cast<Engine.Modes>().ToArray();
            var genres = Enum.GetValues(typeof(Engine.Genres)).Cast<Engine.Genres>().ToArray();
            var rankStatus = Enum.GetValues(typeof(Engine.RankStatus)).Cast<Engine.RankStatus>().ToArray();

            id = id.Replace("_", "");

            if (modes.Any(x => x.ToString().ToLower() == id))
            {
                //Console.WriteLine(modes.First(x => x.ToString().ToLower() == id).ToString());
                if (bool.Parse(check))
                    Engine.Settings.Modes.Add(modes.First(x => x.ToString().ToLower() == id));
                else
                    Engine.Settings.Modes.Remove(modes.First(x => x.ToString().ToLower() == id));
            }

            if (genres.Any(x => x.ToString().ToLower() == id))
            {
                //Console.WriteLine(genres.First(x => x.ToString().ToLower() == id).ToString());
                if (bool.Parse(check))
                    Engine.Settings.Genres.Add(genres.First(x => x.ToString().ToLower() == id));
                else
                    Engine.Settings.Genres.Remove(genres.First(x => x.ToString().ToLower() == id));
            }

            if (rankStatus.Any(x => x.ToString().ToLower() == id))
            {
                //Console.WriteLine(rankStatus.First(x => x.ToString().ToLower() == id).ToString());
                if (bool.Parse(check))
                    Engine.Settings.RankStatus.Add(rankStatus.First(x => x.ToString().ToLower() == id));
                else
                    Engine.Settings.RankStatus.Remove(rankStatus.First(x => x.ToString().ToLower() == id));
            }

            Task.Factory.StartNew(async () => { await Task.Delay(1); Engine.Settings.Save(); });
        }

        private async Task Update()
        {
            _listener.Start();
            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();

            while (_running)
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                await ProcessRequest(context);
            }

            _listener.Stop();
        }

        //https://gist.github.com/jrusbatch/4211535
        public static int GetAvailablePort(int startingPort)
        {
            IPEndPoint[] endPoints;
            List<int> portArray = new List<int>();

            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();

            TcpConnectionInformation[] connections = properties.GetActiveTcpConnections();
            portArray.AddRange(from n in connections
                               where n.LocalEndPoint.Port >= startingPort
                               select n.LocalEndPoint.Port);

            endPoints = properties.GetActiveTcpListeners();
            portArray.AddRange(from n in endPoints
                               where n.Port >= startingPort
                               select n.Port);

            endPoints = properties.GetActiveUdpListeners();
            portArray.AddRange(from n in endPoints
                               where n.Port >= startingPort
                               select n.Port);

            portArray.Sort();

            for (int i = startingPort; i < UInt16.MaxValue; i++)
                if (!portArray.Contains(i))
                    return i;

            return 0;
        }
    }
}
