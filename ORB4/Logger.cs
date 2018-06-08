using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ORB4
{
    class Logger
    {
        public static Logger MainLogger;

        public enum LogTypes {
            Error, 
            Warning,
            Info
        }

        private Queue<KeyValuePair<LogTypes, object>> _logQueue;
        private System.IO.StreamWriter _writer;

        public void Log(LogTypes logType, object @object)
        {
            _logQueue.Enqueue(new KeyValuePair<LogTypes, object>(logType, @object));
        }

        private bool _running = false;
        private bool _queueUpdaterRunning = false;

        public async Task Start()
        {
            Log(LogTypes.Info, $"Osu! Random Beatmap - {Engine.Version}");
            Log(LogTypes.Info, $"The main logger was started.");

            _running = true;
            _queueUpdaterRunning = true;
            while (_running)
            {
                _queueUpdaterRunning = true;
                if (_logQueue.Count > 0)
                {
                    KeyValuePair<LogTypes, object> pair = _logQueue.Dequeue();
                    await _writer.WriteLineAsync($"[{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fffffff")}] [{pair.Key.ToString().ToUpper()}] {pair.Value}");
                    await _writer.FlushAsync();
                }

                await Task.Delay(1);
            }

            _queueUpdaterRunning = false;
        }

        public async Task Stop()
        {
            Log(LogTypes.Info, "The main logger was stopped.");
            _running = false;

            while (_queueUpdaterRunning)
            { await Task.Delay(1); }

            await _writer.FlushAsync();
            _writer.Close();
        }

        public Logger()
        {
            _writer = System.IO.File.CreateText($"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\ORB\\program.log");
            _logQueue = new Queue<KeyValuePair<LogTypes, object>>();
        }
    }
}
