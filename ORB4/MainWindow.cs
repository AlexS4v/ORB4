using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using CefSharp.Internals;

namespace ORB4
{
    public class CustomMenuHandler : CefSharp.IContextMenuHandler
    {
        public void OnBeforeContextMenu(IWebBrowser browserControl, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model)
        { }

        public bool OnContextMenuCommand(IWebBrowser browserControl, IBrowser browser, IFrame frame, IContextMenuParams parameters, CefMenuCommand commandId, CefEventFlags eventFlags)
        { return false; }

        public void OnContextMenuDismissed(IWebBrowser browserControl, IBrowser browser, IFrame frame)
        { }

        public bool RunContextMenu(IWebBrowser browserControl, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model, IRunContextMenuCallback callback)
        { return false; }
    }

    class CSharpTools
    {
        private Engine _engine { get; set; }
        private MainWindow _mainWindow { get; set; }

        public CSharpTools(ref Engine engine, ref MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _engine = engine;
        }

        public void Initialize()
        {
            if (!_engine.LocalSettings.NightMode)
            {
                _mainWindow.Invoke(new Action(() => { _mainWindow._browser.Dock = DockStyle.Fill; }));
                System.Threading.Thread.Sleep(200);
                _mainWindow.Invoke(new Action(() => { _mainWindow.ResumeLayout(); _mainWindow._browser.Refresh(); }));
                System.Threading.Thread.Sleep(200);
                _mainWindow.Invoke(new Action(() => { _mainWindow._browser.BringToFront(); }));
            } else
            {
                System.Threading.Thread.Sleep(400);
                _mainWindow.Invoke(new Action(() => { _mainWindow._browser.Dock = DockStyle.Fill; }));
                _mainWindow.Invoke(new Action(() => { _mainWindow.ResumeLayout(); _mainWindow._browser.Refresh(); }));

            }
        }

        public string GetVersion()
        {
            return Engine.Version;
        }

        public void OpenUrl(string url)
        {
            System.Diagnostics.Process.Start(url);
        }

        public void FixZoom()
        {
            _mainWindow.FixZoom();
        }


        public void CalmDownMessageBox()
        {
            System.Windows.Forms.MessageBox.Show($"Yo, chill! Are you trying to kill {_engine.LocalSettings.Mirror.ToString()}? You can only queue 10 beatmaps a time.", "Alert", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Exclamation);
        }

        public void RegisterApiKey(string apikey)
        {
            if (System.Windows.Forms.MessageBox.Show("Do you want to save your API key on this computer? It could be used by bad guys for malicious activities.", "Question", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.Yes)
            {
                string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!System.IO.Directory.Exists($"{AppData}\\ORB\\Private\\"))
                    System.IO.Directory.CreateDirectory($"{AppData}\\ORB\\Private\\");

                System.IO.File.WriteAllText($"{AppData}\\ORB\\Private\\Key", apikey);
            }
            _engine.ApiKey = apikey;
        }
    }

    public partial class MainWindow : Form
    {
        CefSettings _cefSettings;
        internal ChromiumWebBrowser _browser;
        WebServer _server;

        public static MainWindow Current; 

        public MainWindow()
        {
            InitializeComponent();

            Current = this;

            this.Resize += OnWinResize;

            string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            _cefSettings = new CefSettings() { CachePath = null };

            _cefSettings.CefCommandLineArgs.Add("--disable-cache", "1");
            _cefSettings.CefCommandLineArgs.Add("--disable-application-cache", "1");
            _cefSettings.CefCommandLineArgs.Add("--disable-session-storage", "1");
            _cefSettings.CefCommandLineArgs.Add("--disable-local-storage", "1");

            if (System.IO.File.Exists($"{AppData}\\ORB\\cef.log"))
                System.IO.File.Delete($"{AppData}\\ORB\\cef.log");
            


            _cefSettings.LogFile = $"{AppData}\\ORB\\cef.log";
            _cefSettings.LogSeverity = LogSeverity.Error | LogSeverity.Info | LogSeverity.Warning;

            Cef.Initialize(_cefSettings);

            if (!Cef.IsInitialized)
            {
                throw new Exception("Couldn't initialize CEF.");
            }

            Logger.MainLogger.Log(Logger.LogTypes.Info, "CEF.Initialize -> Success");

            CefSharpSettings.LegacyJavascriptBindingEnabled = true;

            var manager = Cef.GetGlobalCookieManager();

            _server = new WebServer();
            _server.Start();

            Logger.MainLogger.Log(Logger.LogTypes.Info, "HTTPServer.Start -> Success");

            if (System.IO.File.Exists($"{AppData}\\ORB\\Private\\Key"))
            {
                _server.Engine.ApiKey = System.IO.File.ReadAllText($"{AppData}\\ORB\\Private\\Key");
                Logger.MainLogger.Log(Logger.LogTypes.Info, "APIKey.Load -> Success");
            }

            _browser = new ChromiumWebBrowser($"{_server.Url}html/mainwindow.html") { Dock = DockStyle.Fill };
            Logger.MainLogger.Log(Logger.LogTypes.Info, "BrowserObject.Create -> Success");
            _browser.RegisterJsObject("cSharpTools", new CSharpTools(ref _server.Engine, ref Current));
            Logger.MainLogger.Log(Logger.LogTypes.Info, "BrowserObject.RegisterJS -> Success");
            _browser.BackColor = Color.Black;
            this.BackColor = Color.Black;

            manager.SetCookie(_server.Url, new CefSharp.Cookie()
            {
                Path = "/",
                Name = "token",
                Value = Convert.ToBase64String(_server.Token)
            });
            
            Logger.MainLogger.Log(Logger.LogTypes.Info, "CookieManager.SetCookie -> Success");
            _browser.Dock = DockStyle.None;

            _browser.IsBrowserInitializedChanged += InitializeStatus;
            this.Controls.Add(_browser);
            _browser.Paint += BrowserPaint;

            Color panelColor = _server.Engine.LocalSettings.NightMode ?
                Color.FromArgb(32, 31, 36) : Color.White;

            if (!_server.Engine.LocalSettings.NightMode)
            {

                _loadingPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = panelColor
                };

                this.Controls.Add(_loadingPanel);
                _loadingPanel.BringToFront();
            }

            BackColor = Color.Black ;
        }

        internal Panel _loadingPanel;


        public bool DownloadBeatmapAgainMessageBox()
        {
            return System.Windows.Forms.MessageBox.Show($"You already have this beatmap: are you sure you want to download it again?", "Question", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void BrowserPaint(object sender, PaintEventArgs e)
        {
        }

        bool _initialized = false;

        private void InitializeStatus(object sender, IsBrowserInitializedChangedEventArgs e)
        {
            if (e.IsBrowserInitialized)
            {
                //_browser.ShowDevTools();
                Logger.MainLogger.Log(Logger.LogTypes.Info, "CookieManager.Initialize -> Success");

                predW = this.Width;
                predY = this.Height;
   
                _initialized = true;
            }
        }

        int predW = 800;
        int predY = 600;

        public void FixZoom()
        {
            double areaPred = predW * predY;
            double areaCurrent = this.Width * this.Height;

            double scale = Math.Round(areaCurrent / 1.5 / areaPred, 1);
            _browser.SetZoomLevel(scale - 1);
        }

        private void OnWinResize(object sender, EventArgs e)
        {
            if (_initialized) {
                FixZoom();
            }
        }

        private void OnClosed(object sender, System.Windows.Forms.FormClosedEventArgs e)
        {
            _browser.Dispose();
            Cef.Shutdown();
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            _server.Engine.Stop();
            _server.Engine.Dispose();
            _browser.Hide();
            this.Hide();
        }

        private void MainWindow_Load(object sender, EventArgs e)
        {
            this.SuspendLayout();
        }
    }
}
