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

        private System.Threading.SemaphoreSlim _thumbnailsSemaphore;

        public Engine Engine;
        public BeatmapDownloader BeatmapDownloader;

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
            _thumbnailsSemaphore = new System.Threading.SemaphoreSlim(4,4);
            _listener = new HttpListener();
            Engine = new Engine() { LocalSettings = ORB4.Engine.Settings.Load() };
            BeatmapDownloader = new BeatmapDownloader(ref Engine);

            Token = Guid.NewGuid().ToByteArray();

            Logger.MainLogger.Log(Logger.LogTypes.Info, "BrowserObject.RegisterJS -> Success");

            Url = $"http://localhost:{GetAvailablePort(41500)}/";

            _listener.Prefixes.Add(Url);

            _resources = new Dictionary<string, HtmlResource>()
            {
                { "/html/mainwindow.html", new HtmlResource(0, "text/html") },
                { "/html/settings.html", new HtmlResource(1, "text/html") },
                { "/css/main.css", new HtmlResource(2, "text/css") },
                { "/css/night.css", new HtmlResource(17, "text/css") },
                { "/css/default.css", new HtmlResource(18, "text/css") },
                { "/css/svg/settings-icon.svg", new HtmlResource(3, "image/svg+xml") },
                { "/css/fonts/NotoSans-Regular.ttf", new HtmlResource(4, "application/font") },
                { "/css/fonts/NotoSans-Light.ttf", new HtmlResource(5, "application/font") },
                { "/css/fonts/NotoSans-Thin.ttf", new HtmlResource(6, "application/font") },
                { "/html/foundbeatmaps.html", new HtmlResource(10, "text/html") },
                { "/css/svg/close-icon.svg", new HtmlResource(11, "image/svg+xml") },
                { "/jquery", new HtmlResource(12, "application/javascript") },
                { "/gifs/loading.gif", new HtmlResource(14, "image/gif") },
                { "/gifs/loading_dark.gif", new HtmlResource(19, "image/gif") },
                { "/css/svg/save.svg", new HtmlResource(15, "image/svg+xml") },
                { "/css/svg/clear.svg", new HtmlResource(16, "image/svg+xml") },
                { "/html/beatmap_downloader.html", new HtmlResource(20, "text/html") },
                { "/html/search_dl.html", new HtmlResource(21, "text/html") },
                { "/css/svg/downloader-icon.svg", new HtmlResource(22, "image/svg+xml") },
                { "/css/svg/downloader-bracket-icon.svg", new HtmlResource(23, "image/svg+xml") },
                { "/progressbar.js", new HtmlResource(24, "application/javascript") },
                { "/html/list_dl.html", new HtmlResource(25, "text/html") },
                { "/preview_player.js", new HtmlResource(27, "application/javascript") },
                { "/css/standard_mode.png", new HtmlResource(30, "image/png") },
                { "/css/taiko_mode.png", new HtmlResource(31, "image/png") },
                { "/css/ctb_mode.png", new HtmlResource(32, "image/png") },
                { "/css/mania_mode.png", new HtmlResource(33, "image/png") },
                { "/css/standard_mode_light.png", new HtmlResource(40, "image/png") },
                { "/css/taiko_mode_light.png", new HtmlResource(41, "image/png") },
                { "/css/ctb_mode_light.png", new HtmlResource(42, "image/png") },
                { "/css/mania_mode_light.png", new HtmlResource(43, "image/png") },
                { "/css/preview_stop.png", new HtmlResource(50, "image/png") },
                { "/css/preview_play.png", new HtmlResource(51, "image/png") },
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
        {/*
            <!DOCTYPE html>
<html lang="en">

<head>

            */
            try
            {
                var cookie = context.Request.Cookies["token"];
                if (cookie != null)
                {
                    string base64 = Convert.ToBase64String(Token);
                    if (cookie.Value != base64)
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
                    byte[] bytes = { };

                    if (resource.Id == 0)
                    {
                        string data = $"<!DOCTYPE html>\r\n<html lang=\"en\">\r\n<head>\r\n<script>const version = \"{Engine.Version}\";</script>\r\n" + Encoding.UTF8.GetString((byte[])Properties.Resources.ResourceManager.GetObject($"_{resource.Id}")) ;
                        bytes = Encoding.UTF8.GetBytes(data);
                    }
                    else
                        bytes = (byte[])Properties.Resources.ResourceManager.GetObject($"_{resource.Id}");

                    context.Response.ContentLength64 = bytes.Length;
                    context.Response.ContentType = resource.Type;
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);

                    context.Response.OutputStream.Close();
                }
                else
                {
                    switch (context.Request.RawUrl)
                    {
                        case "/utils/login_form":
                            if (!Engine.LocalSettings.NightMode)
                                new LoginWindowLight().ShowDialog();
                            else
                                new LoginWindowDark().ShowDialog();
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "text/plain";
                            await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                            context.Response.OutputStream.Close();
                            break;
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

                            string statusd = Engine.GetStatus();

                            if ((!Engine.LocalSettings.AutoOpen || Engine.LocalSettings.OpenInDownloader) && Engine.FoundCurrentSearch > 0)
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
                        case "/engine/mirror":
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "text/plain";

                            bytes = Encoding.UTF8.GetBytes("MIRROR");

                            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);

                            context.Response.OutputStream.Close();
                            return;
                        case "/downloader/dls":
                            bytes = Encoding.UTF8.GetBytes(
                                await BeatmapDownloader.DownloadStatusAsync());

                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "application/json";

                            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                            context.Response.OutputStream.Close();
                            return;
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
                            else if (context.Request.RawUrl.Contains("/engine/beatmaps/ranked"))
                            {
                                string query = context.Request.QueryString["p"];

                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "application/json";

                                bytes = Encoding.UTF8.GetBytes(Engine.GetRankedPageJson(int.Parse(query)));

                                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);

                                context.Response.OutputStream.Close();
                            }
                            else if (context.Request.RawUrl.Contains("/engine/beatmaps/unranked"))
                            {
                                string query = context.Request.QueryString["p"];

                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "application/json";

                                bytes = Encoding.UTF8.GetBytes(Engine.GetUnrankedPageJson(int.Parse(query)));

                                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);

                                context.Response.OutputStream.Close();
                            }
                            else if (context.Request.RawUrl.Contains("/engine/beatmaps/loved"))
                            {
                                string query = context.Request.QueryString["p"];

                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "application/json";

                                bytes = Encoding.UTF8.GetBytes(Engine.GetLovedPageJson(int.Parse(query)));

                                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);

                                context.Response.OutputStream.Close();
                            }
                            else if (context.Request.RawUrl.Contains("/utils/open_beatmapset_mirror"))
                            {
                                string query = context.Request.QueryString["id"];
                                string query_bId = context.Request.QueryString["b_id"];

                                if (query != string.Empty)
                                {
                                    context.Response.StatusCode = 200;
                                    context.Response.ContentType = "text/plain";

                                    if (!int.TryParse(query, out int i))
                                    {
                                        break;
                                    }

                                    switch (Engine.LocalSettings.Mirror)
                                    {
                                        case Engine.DownloadMirrors.Hexide:
                                            Process.Start($"https://osu.hexide.com/beatmaps/{query}/download/novid/no-video.osz");
                                            break;
                                        case Engine.DownloadMirrors.Bloodcat:
                                            Process.Start($"https://bloodcat.com/osu/s/{query}");
                                            break;
                                        default:
                                            break;
                                    }

                                    await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                                    context.Response.OutputStream.Close();
                                }
                                return;
                            }
                            else if (context.Request.RawUrl.Contains("/utils/open_beatmapset"))
                            {
                                string query = context.Request.QueryString["id"];
                                string query_bId = context.Request.QueryString["b_id"];

                                if (query != string.Empty)
                                {
                                    context.Response.StatusCode = 200;
                                    context.Response.ContentType = "text/plain";

                                    if (!int.TryParse(query, out int i))
                                    {
                                        break;
                                    }

                                    if (Engine.LocalSettings.OpenInGame)
                                        Process.Start($"osu://s/{query}");
                                    else
                                    {
                                        if (!Engine.Ripple)
                                            Process.Start($"https://osu.ppy.sh/s/{query}");
                                        else
                                            Process.Start($"https://ripple.moe/b/{query_bId}");
                                    }
                                    
                                    await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                                    context.Response.OutputStream.Close();
                                }
                                return;
                            }
                            else if (context.Request.RawUrl.Contains("/images/get_beatmap_image_nodisk"))
                            {
                                await _thumbnailsSemaphore.WaitAsync();
                                try
                                {
                                    string query = context.Request.QueryString["id"];

                                    context.Response.StatusCode = 200;
                                    bytes = await ThumbnailDownloader.MainThumbnailDownloader.GetThumbnailNoDisk(int.Parse(query));
                                    context.Response.ContentLength64 = bytes.Length;
                                    context.Response.ContentType = "image/jpeg";

                                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                                    context.Response.OutputStream.Close();
                                    return;
                                }
                                finally
                                {
                                    _thumbnailsSemaphore.Release();
                                }
                            }
                            else if (context.Request.RawUrl.Contains("/images/get_beatmap_image"))
                            {
                                await _thumbnailsSemaphore.WaitAsync();
                                try
                                {
                                    string query = context.Request.QueryString["id"];

                                    context.Response.StatusCode = 200;
                                    bytes = await ThumbnailDownloader.MainThumbnailDownloader.GetThumbnail(int.Parse(query));
                                    context.Response.ContentLength64 = bytes.Length;
                                    context.Response.ContentType = "image/jpeg";

                                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                                    context.Response.OutputStream.Close();
                                    return;
                                }
                                finally
                                {
                                    _thumbnailsSemaphore.Release();
                                }
                            }
                            else if (context.Request.RawUrl.Contains("/sounds/play"))
                            {
                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "text/plain";

                                if (Engine.LocalSettings.SoundEffects)
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
                                        case "Downloaded":
                                            Utils.PlayWavAsync(Properties.Resources.Downloaded);
                                            break;
                                        default:
                                            break;
                                    }
                                }

                                await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                                context.Response.OutputStream.Close();
                            }
                            else if (context.Request.RawUrl.Contains("/downloader/search_unranked"))
                            {
                                string query = context.Request.QueryString["query"];
                                string page = "1";

                                try
                                {
                                    page = context.Request.QueryString["page"];
                                }
                                catch { }

                                bytes = Encoding.UTF8.GetBytes(
                                    await BeatmapDownloader.SearchUnranked(query, int.Parse(page)));

                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "application/json";

                                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                                context.Response.OutputStream.Close();
                                return;
                            }
                            else if (context.Request.RawUrl.Contains("/downloader/search"))
                            {
                                string query = context.Request.QueryString["query"];
                                string page = "0";

                                try
                                {
                                    page = context.Request.QueryString["page"];
                                } catch {

                                }

                                if (page == null) page = "0";

                                bytes = Encoding.UTF8.GetBytes(
                                    await BeatmapDownloader.Search(query, int.Parse(page)));

                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "application/json";

                                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                                context.Response.OutputStream.Close();
                                return;
                            }
                            else if (context.Request.RawUrl.Contains("/downloader/start"))
                            {
                                string query0 = context.Request.QueryString["id"];
                                string query1 = context.Request.QueryString["author"];
                                string query2 = context.Request.QueryString["artist"];
                                string query3 = context.Request.QueryString["title"];
                                string query4 = context.Request.QueryString["status"];
                                
                                if (await BeatmapDownloader.Exists(int.Parse(query0)))
                                {
                                    if (!MainWindow.Current.DownloadBeatmapAgainMessageBox())
                                    {
                                        await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                                        context.Response.OutputStream.Close();
                                        break;
                                    }
                                    else
                                    {
                                        await BeatmapDownloader.Delete(int.Parse(query0));
                                    }
                                }

                                bytes = Encoding.UTF8.GetBytes(
                                    (await BeatmapDownloader
                                    .RegisterDownload (int.Parse(query0), int.Parse(query4), query1, query2, query3))
                                    .ToString());

                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "text/plain";

                                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                                context.Response.OutputStream.Close();
                                return;
                            }
                            else if (context.Request.RawUrl.Contains("/downloader/stop"))
                            {
                                string query = context.Request.QueryString["id"];
                                int status = await BeatmapDownloader.Stop(int.Parse(query));

                                context.Response.StatusCode = status;
                                context.Response.ContentType = "text/plain";

                                await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                                context.Response.OutputStream.Close();
                                return;
                            }
                            else if (context.Request.RawUrl.Contains("/downloader/run"))
                            {
                                string query = context.Request.QueryString["id"];
                                int status = await BeatmapDownloader.RunBeatmap(int.Parse(query));

                                context.Response.StatusCode = status;
                                context.Response.ContentType = "text/plain";

                                await context.Response.OutputStream.WriteAsync(new byte[] { }, 0, 0);
                                context.Response.OutputStream.Close();
                                return;
                            }
                            else if (context.Request.RawUrl.Contains("/downloader/dls"))
                            {
                                try
                                {
                                    string query = context.Request.QueryString["id"];
                                    bytes = Encoding.UTF8.GetBytes(
                                        await BeatmapDownloader.DownloadStatusAsync(int.Parse(query)));
                                } catch(Exception e)
                                {
                                    bytes = Encoding.UTF8.GetBytes("ERROR");
                                } 
                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "application/json";

                                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                                context.Response.OutputStream.Close();
                                return;
                            }
                            else if (context.Request.RawUrl.Contains("/ui_mode/"))
                            {
                                string mode = string.Empty;

                                if (Engine.LocalSettings.NightMode)
                                    mode = "../css/night.css";
                                else
                                    mode = "../css/main.css";

                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "text/plain";

                                bytes = Encoding.UTF8.GetBytes(mode);

                                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                                context.Response.OutputStream.Close();
                                return;
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
                new JObject() { { "id", "auto_open" }, { "checked", Engine.LocalSettings.AutoOpen.ToString().ToLower()  } },
                new JObject() { { "id", "api_key" }, { "value", (Engine.ApiKey != string.Empty) ? Engine.GetAPIKeySha512() : string.Empty } },
                new JObject() { { "id", "open_in_game" }, { "checked", Engine.LocalSettings.OpenInGame.ToString().ToLower()  } },
                new JObject() { { "id", "old_beatmaps" }, { "checked", Engine.LocalSettings.OldBeatmapsB.ToString().ToLower()  } },
                new JObject() { { "id", "max_stars"}, { "value", Engine.LocalSettings.MaxStars.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) } },
                new JObject() { { "id", "min_stars"}, { "value", Engine.LocalSettings.MinStars.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) } },
                new JObject() { { "id", "max_length"}, { "value", Engine.LocalSettings.MaxLength.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) } },
                new JObject() { { "id", "min_length"}, { "value", Engine.LocalSettings.MinLength.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) } },
                new JObject() { { "id", "max_bpm"}, { "value", Engine.LocalSettings.MaxBPM.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) } },
                new JObject() { { "id", "min_bpm"}, { "value", Engine.LocalSettings.MinBPM.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) } },
                new JObject() { { "id", "any_bpm"}, { "checked", Engine.LocalSettings.AnyBPM.ToString().ToLower() } },
                new JObject() { { "id", "any_difficulty"}, { "checked", Engine.LocalSettings.AnyDifficulty.ToString().ToLower() } },
                new JObject() { { "id", "any_length"}, { "checked", Engine.LocalSettings.AnyLength.ToString().ToLower() } },
                new JObject() { { "id", "sound_effects"}, { "checked", Engine.LocalSettings.SoundEffects.ToString().ToLower() } },
                new JObject() { { "id", "hexide_mirror"}, { "checked", Engine.LocalSettings.Mirror == Engine.DownloadMirrors.Hexide ? "true" : "false" } },
                new JObject() { { "id", "bloodcat_mirror"}, { "checked", Engine.LocalSettings.Mirror == Engine.DownloadMirrors.Bloodcat ? "true" : "false" } },
                new JObject() { { "id", "ripple" }, { "checked", Engine.LocalSettings.Ripple.ToString().ToLower() } },
                new JObject() { { "id", "night_mode" }, { "checked", Engine.LocalSettings.NightMode.ToString().ToLower() } },
                new JObject() { { "id", "new_beatmaps" }, { "checked", Engine.LocalSettings.NewBeatmapsB.ToString().ToLower()  } },
                new JObject() { { "id", "open_in_downloader" }, { "checked", Engine.LocalSettings.OpenInDownloader.ToString().ToLower()  } },
            };

            var modes = Enum.GetValues(typeof(Engine.Modes)).Cast<Engine.Modes>().ToArray();
            var genres = Enum.GetValues(typeof(Engine.Genres)).Cast<Engine.Genres>().ToArray();
            var rankStatus = Enum.GetValues(typeof(Engine.RankStatus)).Cast<Engine.RankStatus>().ToArray();

            foreach (var mode in modes)
            {
                if (mode == Engine.Modes.CatchTheBeat)
                {
                    if (Engine.LocalSettings.Modes.Contains(mode))
                        settings.Add(new JObject() { { "id", "catch_the_beat" }, { "checked", "true" } });
                    else
                        settings.Add(new JObject() { { "id", "catch_the_beat" }, { "checked", "false" } });
                }
                else
                {
                    if (Engine.LocalSettings.Modes.Contains(mode))
                        settings.Add(new JObject() { { "id", mode.ToString().ToLower() }, { "checked", "true" } });
                    else
                        settings.Add(new JObject() { { "id", mode.ToString().ToLower() }, { "checked", "false" } });
                }
            }

            foreach (var status in rankStatus)
            {
                if (Engine.LocalSettings.RankStatus.Contains(status))
                    settings.Add(new JObject() { { "id", status.ToString().ToLower() }, { "checked", "true" } });
                else
                    settings.Add(new JObject() { { "id", status.ToString().ToLower() }, { "checked", "false" } });
            }

            foreach (var genre in genres)
            {
                if (genre == Engine.Genres.HipHop)
                {
                    if (Engine.LocalSettings.Genres.Contains(genre))
                        settings.Add(new JObject() { { "id", "hip_hop" }, { "checked", "true" } });
                    else
                        settings.Add(new JObject() { { "id", "hip_hop" }, { "checked", "false" } });
                }
                else if (genre == Engine.Genres.VideoGame)
                {
                    if (Engine.LocalSettings.Genres.Contains(genre))
                        settings.Add(new JObject() { { "id", "video_game" }, { "checked", "true" } });
                    else
                        settings.Add(new JObject() { { "id", "video_game" }, { "checked", "false" } });
                }
                else
                {
                    if (Engine.LocalSettings.Genres.Contains(genre))
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
                    Engine.LocalSettings.SoundEffects = bool.Parse(check);
                    break;
                case "any_bpm":
                    Engine.LocalSettings.AnyBPM = bool.Parse(check);
                    break;
                case "any_difficulty":
                    Engine.LocalSettings.AnyDifficulty = bool.Parse(check);
                    break;
                case "any_length":
                    Engine.LocalSettings.AnyLength = bool.Parse(check);
                    break;
                case "auto_open":
                    Engine.LocalSettings.AutoOpen = bool.Parse(check);
                    if (Engine.LocalSettings.OpenInDownloader) Engine.LocalSettings.OpenInDownloader = false;
                    break;
                case "open_in_game":
                    Engine.LocalSettings.OpenInGame = bool.Parse(check);
                    break;
                case "old_beatmaps":
                    Engine.LocalSettings.OldBeatmapsB = bool.Parse(check);
                    break;
                case "new_beatmaps":
                    Engine.LocalSettings.NewBeatmapsB = bool.Parse(check);
                    break;
                case "max_stars":
                    try
                    {
                        Engine.LocalSettings.MaxStars = float.Parse(value, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                    }
                    catch
                    {
                        Engine.LocalSettings.MaxStars = 100;
                    }
                    break;
                case "min_stars":
                    try
                    {
                        Engine.LocalSettings.MinStars = float.Parse(value, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                    }
                    catch
                    {
                        Engine.LocalSettings.MinStars = 100;
                    }
                    break;
                case "max_length":
                    try
                    {
                        Engine.LocalSettings.MaxLength = int.Parse(value, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                    }
                    catch
                    {
                        Engine.LocalSettings.MaxLength = 600;
                    }
                    break;
                case "min_length":
                    try
                    {
                        Engine.LocalSettings.MinLength = int.Parse(value, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                    }
                    catch
                    {
                        Engine.LocalSettings.MinLength = 0;
                    }
                    break;
                case "max_bpm":
                    try
                    {
                        Engine.LocalSettings.MaxBPM = float.Parse(value, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                    }
                    catch
                    {
                        Engine.LocalSettings.MaxBPM = 250;
                    }
                    break;
                case "min_bpm":
                    try
                    {
                        Engine.LocalSettings.MinBPM = float.Parse(value, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                    }
                    catch
                    {
                        Engine.LocalSettings.MinBPM = 0;
                    }
                    break;
                case "hexide_mirror":
                    Engine.LocalSettings.Mirror = Engine.DownloadMirrors.Hexide;
                    break;
                case "bloodcat_mirror":
                    Engine.LocalSettings.Mirror = Engine.DownloadMirrors.Bloodcat;
                    break;
                case "ripple":
                    Engine.LocalSettings.Ripple = bool.Parse(check);
                    break;
                case "night_mode":
                    Engine.LocalSettings.NightMode = bool.Parse(check);
                    break;
                case "open_in_downloader":
                    Engine.LocalSettings.OpenInDownloader = bool.Parse(check);
                    if (Engine.LocalSettings.AutoOpen) Engine.LocalSettings.AutoOpen = false;
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
                    Engine.LocalSettings.Modes.Add(modes.First(x => x.ToString().ToLower() == id));
                else
                    Engine.LocalSettings.Modes.Remove(modes.First(x => x.ToString().ToLower() == id));
            }

            if (genres.Any(x => x.ToString().ToLower() == id))
            {
                //Console.WriteLine(genres.First(x => x.ToString().ToLower() == id).ToString());
                if (bool.Parse(check))
                    Engine.LocalSettings.Genres.Add(genres.First(x => x.ToString().ToLower() == id));
                else
                    Engine.LocalSettings.Genres.Remove(genres.First(x => x.ToString().ToLower() == id));
            }

            if (rankStatus.Any(x => x.ToString().ToLower() == id))
            {
                //Console.WriteLine(rankStatus.First(x => x.ToString().ToLower() == id).ToString());
                if (bool.Parse(check))
                    Engine.LocalSettings.RankStatus.Add(rankStatus.First(x => x.ToString().ToLower() == id));
                else
                    Engine.LocalSettings.RankStatus.Remove(rankStatus.First(x => x.ToString().ToLower() == id));
            }

            Task.Factory.StartNew(async () => { await Task.Delay(1); Engine.LocalSettings.Save(); });
        }

        private async Task Update()
        {
            _listener.Start();
            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();

            while (_running)
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                ProcessRequest(context);
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
