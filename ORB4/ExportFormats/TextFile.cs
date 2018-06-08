using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ORB4.ExportFormats
{
    class TextFile : ExportFormat
    {
        public TextFile(Engine.Beatmap[] beatmaps) :
            base(beatmaps)
        { }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine($"Osu! Random Beatmap - {Engine.Version}");
            builder.AppendLine($"Found beatmaps: {Beatmaps.Length}");
            builder.AppendLine();

            foreach (var beatmap in Beatmaps)
            {
                builder.AppendLine($"{beatmap.Artist} - {beatmap.Title}");
                builder.AppendLine($"Created by {beatmap.Creator}");
                builder.AppendLine($"Download link: https://osu.ppy.sh/s/{beatmap.BeatmapsetId}");
                builder.AppendLine();
            }
            
            return builder.ToString();
        }

        public override byte[] GetBytes()
        {
            return Encoding.UTF8.GetBytes(ToString());
        }
    }
}
