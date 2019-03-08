// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

using static System.FormattableString;

namespace AccessibilityInsights.SharedUx.FileBug
{
    /// <summary>
    /// Winform based bug filing dialog code. 
    /// because of parenting issue, wpf can't be used for IE Control
    /// </summary>
    public partial class BugFileForm: Form
    {
        private const int ZOOM_MAX = 1000;
        private const int ZOOM_MIN = 25;
        private const int ZOOM_STEP_SIZE = 25;

        // new work form item template URL
        private Uri Url;

        private bool makeTopMost;

        private Action<int> UpdateZoomLevel;

        /// <summary>
        /// Value to zoom the embedded web browser by
        /// </summary>
        public int ZoomValue { get; set; }

        /// <summary>
        /// Represents the id of the bug filed in this window after ShowDialog() returns
        /// (null if no bug was filed)
        /// </summary>
        public int? BugId { get; internal set; }

        /// <summary>
        /// Javascript code to run once page is loaded
        /// </summary>
        public string ScriptToRun { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public BugFileForm(Uri url, bool topmost, int zoomLevel, Action<int> updateZoom)
        {
            InitializeComponent();
            SetBrowserFeatureControl();
            this.UpdateZoomLevel = updateZoom;
            this.makeTopMost = topmost;
            this.Url = url;
            this.zoomIn.Click += ZoomIn_Click;
            this.zoomOut.Click += ZoomOut_Click;
            this.zoomIn.FlatAppearance.BorderSize = 0;
            this.zoomOut.FlatAppearance.BorderSize = 0;
            this.ZoomValue = zoomLevel;
            this.FormClosed += BugFileForm_FormClosed;
            this.fileBugBrowser.ScriptErrorsSuppressed = true; // Hides script errors AND other dialog boxes.
        }

        /// <summary>
        /// When the form is closed, set the zoom level
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BugFileForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            UpdateZoomLevel(ZoomValue);
        }

        /// <summary>
        /// Use URL changes to check whether the bug has been filed or not, close the window if bug has been filed or closed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Browser_Navigated(object sender, WebBrowserNavigatedEventArgs e)
        {
            Debug.WriteLine($"Navigated: {e.Url}");
            ZoomToValue();

            var url = e.Url.PathAndQuery;
            var savedUrlSubstrings = new List<String>() { "_queries/edit/", "_workitems/edit/", "_workitems?id=" };
            int urlIndex = savedUrlSubstrings.FindIndex(str => url.Contains(str));
            if (urlIndex >= 0)
            {
                var matched = savedUrlSubstrings[urlIndex];
                var endIndex = url.IndexOf(matched, StringComparison.Ordinal) + matched.Length;

                // URL looks like "_queries/edit/2222222/..." where 2222222 is bug id
                // or is "_workitems/edit/2222222"
                // or is "_workitems?id=2222222"
                url = url.Substring(endIndex);
                bool worked = int.TryParse(new String(url.TakeWhile(Char.IsDigit).ToArray()), out int result);
                if (worked)
                {
                    this.BugId = result;
                }
                else
                {
                    this.BugId = null;
                }
                this.Close();
            }
        }

        /// <summary>
        /// Navigates to the given url
        /// </summary>
        /// <param name="url"></param>
        private void Navigate(Uri url) => fileBugBrowser.Navigate(url);

        private void BugFileForm_Load(object sender, EventArgs e)
        {
            this.fileBugBrowser.ObjectForScripting = new ScriptInterface(this);
            this.fileBugBrowser.DocumentCompleted += (s, ea) => this.fileBugBrowser.Document.InvokeScript("eval", new object[] { ScriptToRun });
            fileBugBrowser.Navigated += Browser_Navigated;
            Navigate(this.Url);
            this.TopMost = makeTopMost;
            Debug.WriteLine("Loaded");
        }

        /// <summary>
        /// Zooms the browser's active x instance to the ZoomValue property
        /// can throw com exception if form isn't fully loaded yet, silently ignore
        /// </summary>
        private void ZoomToValue()
        {
            try
            {
                var browser = this.fileBugBrowser.ActiveXInstance as SHDocVw.InternetExplorer;
                browser.ExecWB(SHDocVw.OLECMDID.OLECMDID_OPTICAL_ZOOM, SHDocVw.OLECMDEXECOPT.OLECMDEXECOPT_DODEFAULT, ZoomValue, IntPtr.Zero);
                this.zoomLabel.Text = Invariant($"{ZoomValue}%");
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
        }

        /// <summary>
        /// Zoom out by 25% (max 1000)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ZoomOut_Click(object sender, System.EventArgs e)
        {
            AddZoomValue(-ZOOM_STEP_SIZE);
        }

        /// <summary>
        /// Zoom in by 25% (min 25)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ZoomIn_Click(object sender, System.EventArgs e)
        {
            AddZoomValue(ZOOM_STEP_SIZE);
        }

        /// <summary>
        /// Make sure value is between 25 and 1000 and zoom to it
        /// </summary>
        /// <param name="delta"></param>
        private void AddZoomValue(int delta)
        {
            ZoomValue = Math.Max(ZoomValue + delta, ZOOM_MIN);
            ZoomValue = Math.Min(ZoomValue, ZOOM_MAX);
            ZoomToValue();
        }

        #region "IE Fix"
        // from https://stackoverflow.com/questions/18333459/c-sharp-webbrowser-ajax-call
        private void SetBrowserFeatureControl()
        {
            // http://msdn.microsoft.com/en-us/library/ee330720(v=vs.85).aspx

            // FeatureControl settings are per-process
            dynamic fileName = System.IO.Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);

            // make the control is not running inside Visual Studio Designer
            if (String.Compare(fileName, "devenv.exe", true) == 0 || String.Compare(fileName, "XDesProc.exe", true) == 0)
            {
                return;
            }

            SetBrowserFeatureControlKey("FEATURE_BROWSER_EMULATION", fileName, GetBrowserEmulationMode());
            // Webpages containing standards-based !DOCTYPE directives are displayed in IE10 Standards mode.
            SetBrowserFeatureControlKey("FEATURE_AJAX_CONNECTIONEVENTS", fileName, 1);
            SetBrowserFeatureControlKey("FEATURE_GPU_RENDERING", fileName, 1);
        }

        private static void SetBrowserFeatureControlKey(string feature, string appName, uint value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(String.Concat("Software\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\", feature), RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                key.SetValue(appName, (UInt32)value, RegistryValueKind.DWord);
            }
        }

        private static UInt32 GetBrowserEmulationMode()
        {
            int browserVersion = 7;
            using (var Key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Internet Explorer", RegistryKeyPermissionCheck.ReadSubTree, System.Security.AccessControl.RegistryRights.QueryValues))
            {
                dynamic version = Key.GetValue("svcVersion");
                if (null == version)
                {
                    version = Key.GetValue("Version");
                    if (null == version)
                    {
                        throw new ApplicationException("Microsoft Internet Explorer is required!");
                    }
                }
                int.TryParse(version.ToString().Split('.')[0], out browserVersion);
            }

            UInt32 mode = 10000;
            // Internet Explorer 10. Webpages containing standards-based !DOCTYPE directives are displayed in IE10 Standards mode. Default value for Internet Explorer 10.
            switch (browserVersion)
            {
                case 7:
                    mode = 7000;
                    // Webpages containing standards-based !DOCTYPE directives are displayed in IE7 Standards mode. Default value for applications hosting the WebBrowser Control.
                    break;
                case 8:
                    mode = 8000;
                    // Webpages containing standards-based !DOCTYPE directives are displayed in IE8 mode. Default value for Internet Explorer 8
                    break;
                case 9:
                    mode = 9000;
                    // Internet Explorer 9. Webpages containing standards-based !DOCTYPE directives are displayed in IE9 mode. Default value for Internet Explorer 9.
                    break;
                case 10:
                    mode = 10000;
                    // Internet Explorer 10. Webpages containing standards-based !DOCTYPE directives are displayed in IE9 mode. Default value for Internet Explorer 9.
                    break;
                case 11:
                    mode = 11001;
                    // Internet Explorer 11. Webpages containing standards-based !DOCTYPE directives are displayed in IE9 mode. Default value for Internet Explorer 9.
                    break;
            }

            return mode;
        }
        #endregion
    }
}
