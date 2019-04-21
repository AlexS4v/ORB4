using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net.Sockets;
using System.Net;
using Microsoft.Win32;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace ORB4
{
    static class Utils
    {
        public static KeyValuePair<int, int> GetMaxIds()
        {
            string filenameS = $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\MaxIdsS";
            string filenameB = $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\MaxIdsB";

            int maxNumberBeatmapset = 1500000;
            int maxNumberBeatmap = 2500000;

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = new TimeSpan(0, 0, 4);
                    HttpResponseMessage message = client.GetAsync("http://zusupedl.altervista.org/api/get_last_ids.php").GetAwaiter().GetResult();
                    if (message.IsSuccessStatusCode)
                    {
                        var obj = JObject.Parse(message.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                        maxNumberBeatmapset = int.Parse((string)obj["max_number_beatmapset"]);
                        maxNumberBeatmap = int.Parse((string)obj["max_number_beatmap"]);

                        File.WriteAllText(filenameS, maxNumberBeatmapset.ToString());
                        File.WriteAllText(filenameB, maxNumberBeatmap.ToString());

                        return new KeyValuePair<int, int>(maxNumberBeatmapset, maxNumberBeatmap);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e.ToString());
            }

            if (File.Exists(filenameS) && int.TryParse(File.ReadAllText(filenameS), out int val))
                maxNumberBeatmapset = val;

            if (File.Exists(filenameB) && int.TryParse(File.ReadAllText(filenameB), out int val2))
                maxNumberBeatmap = val2;

            return new KeyValuePair<int, int>(maxNumberBeatmapset, maxNumberBeatmap);
        }


        public static string GetOsuPath()
        {
            RegistryKey key = Registry.ClassesRoot.OpenSubKey(@"\osu!\shell\open\command");
            var path = (string)key.GetValue(string.Empty);
            var splitted = path.Split('"');
            return System.IO.Path.GetDirectoryName(splitted[1]);
        }

        public static async void PlayWavAsync(byte[] bytes)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    using (NAudio.Wave.WaveOutEvent waveOut = new NAudio.Wave.WaveOutEvent())
                    {
                        using (var reader = new NAudio.Wave.WaveFileReader(stream))
                        {
                            waveOut.Init(reader);
                            waveOut.Play();

                            while (waveOut.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                                await Task.Delay(1);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.MainLogger.Log(Logger.LogTypes.Error, e.ToString());
            }
        }
    }
}
