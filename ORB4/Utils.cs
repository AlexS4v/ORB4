using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net.Sockets;
using System.Net;

namespace ORB4
{
    class Utils
    {

        public static async void PlayWavAsync(byte[] bytes)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream(bytes)) {
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
