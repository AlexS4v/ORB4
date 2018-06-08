using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ORB4.ExportFormats
{
    class OsuSync : ExportFormat
    {
        public OsuSync(Engine.Beatmap[] beatmaps)
            : base(beatmaps)
        { }

        public override string ToString()
        {
            Dictionary<string, object> format = new Dictionary<string, object>();
            Dictionary<string, object> info = new Dictionary<string, object>
            {
                { "_date", DateTime.Now.ToString("yyyyMMdd") },
                { "_version", "1.0.1.1" }
            };

            format.Add("_info", info);

            foreach (var bt in Beatmaps)
            {
                Dictionary<string, object> beatmap = new Dictionary<string, object>
                {
                    { "artist", bt.Artist },
                    { "creator", bt.Creator },
                    { "id", bt.BeatmapsetId.ToString() },
                    { "title", bt.Title }
                };
                format.Add(bt.BeatmapsetId.ToString(), beatmap);
            }

            return Newtonsoft.Json.JsonConvert.SerializeObject(format);
        }

        public override byte[] GetBytes()
        {
            return Encoding.UTF8.GetBytes(ToString());
        }
    }
}
