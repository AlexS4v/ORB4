using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ORB4
{
    abstract class ExportFormat
    {
        internal Engine.Beatmap[] Beatmaps { get; set; }
        public ExportFormat(Engine.Beatmap[] beatmaps)
        {
            Beatmaps = beatmaps;
        }

        public abstract byte[] GetBytes();
    }
}
