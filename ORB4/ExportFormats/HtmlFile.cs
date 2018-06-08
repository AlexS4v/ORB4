using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ORB4.ExportFormats
{
    class HtmlFile : ExportFormat
    {
        public HtmlFile(Engine.Beatmap[] beatmaps) :
            base(beatmaps)
        { }

        public override string ToString()
        {
            return base.ToString();
        }

        public override byte[] GetBytes()
        {
            return Encoding.UTF8.GetBytes(ToString());
        }
    }
}
