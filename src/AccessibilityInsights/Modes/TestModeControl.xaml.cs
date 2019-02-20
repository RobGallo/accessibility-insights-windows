// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.Enums;
using AccessibilityInsights.Actions;
using AccessibilityInsights.Actions.Contexts;
using AccessibilityInsights.Actions.Enums;
using AccessibilityInsights.Actions.Misc;
using AccessibilityInsights.Core.Enums;
using AccessibilityInsights.Desktop.Settings;
using AccessibilityInsights.SharedUx.Controls.CustomControls;
using AccessibilityInsights.SharedUx.Dialogs;
using AccessibilityInsights.SharedUx.Interfaces;
using AccessibilityInsights.SharedUx.Settings;
using AccessibilityInsights.SharedUx.Utilities;
using Microsoft.Win32;
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using static System.FormattableString;

namespace AccessibilityInsights.Modes
{
    /// <summary>
    /// Interaction logic for TestModeControl.xaml
    /// </summary>
    public partial class TestModeControl : UserControl, IModeControl
    {
        private ElementContext ElementContext;

        /// <summary>
        /// Indicate how to do the data context population. 
        /// Live/Snapshot/Load
        /// </summary>
        public DataContextMode DataContextMode { get; set; } = DataContextMode.Test;

        /// <summary>
        /// MainWindow to access shared methods
        /// </summary>
        static MainWindow MainWin
        {
            get
            {
                return (MainWindow)Application.Current.MainWindow;
            }
        }

        /// <summary>
        /// App configation
        /// </summary>
        public static ConfigurationModel Configuration
        {
            get
            {
                return ConfigurationManager.GetDefaultInstance()?.AppConfig;
            }
        }

        /// <summary>
        /// Layout configuration
        /// </summary>
        public static AppLayout CurrentLayout
        {
            get
            {
                return ConfigurationManager.GetDefaultInstance()?.AppLayout;
            }
        }

        /// <summary>
        /// Overriding LocalizedControlType
        /// </summary>
        /// <returns></returns>
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new CustomControlOverridingAutomationPeer(this, "page");
        }

        /// <summary>
        /// Hide control and hilighter
        /// </summary>
        public void HideControl()
        {
            this.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Show control and hilighter
        /// </summary>
        public void ShowControl()
        {
            AdjustMainWindowSize();
            this.ctrlTabStop.SetHotkeyText(Configuration.HotKeyForRecord);
            this.runHotkey.Text = Configuration.HotKeyForSnap;
            this.Visibility = Visibility.Visible;
            Dispatcher.InvokeAsync(() =>
            {
                this.SetFocusOnDefaultControl();
            }
            , System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public TestModeControl()
        {
            InitializeComponent();
            this.ctrlAutomatedChecks.RunAgainTest = Refresh;
            this.ctrlAutomatedChecks.NotifyElementSelected = MainWin.HandleSelectElementToShowHowToFix;
            this.ctrlAutomatedChecks.SwitchToServerLogin = MainWin.HandleConnectionConfigurationStart;
            this.ctrlTabStop.SetHighlighterButtonState = MainWin.SetHighlightBtnState;
        }

        /// <summary>
        /// Sets element context and updates UI
        /// </summary>
        /// <param name="ecId"></param>
        public async Task SetElement(Guid ecId)
        {
            EnableMenuButtons(false); // first disable them to avoid any conflict.

            bool selectionFailure = false;

            if (GetDataAction.ExistElementContext(ecId))
            {
                EnableMenuButtons(this.DataContextMode != DataContextMode.Load);
                this.ctrlAutomatedChecks.Visibility = Visibility.Visible;
                this.tbSelectElement.Visibility = Visibility.Collapsed;
                this.tabControl.IsEnabled = true;

                try
                {
                    AutomationProperties.SetName(this, "Test");

                    this.tabControl.IsEnabled = false;
                    this.ctrlProgressRing.Activate();

                    ElementContext ec = null;
                    string warning = string.Empty;

                    await Task.Run(() =>
                    {
                        var updated = CaptureAction.SetTestModeDataContext(ecId, this.DataContextMode, Configuration.TreeViewMode);
                        ec = GetDataAction.GetElementContext(ecId);

                        // send telemetry of scan results. 
                        var dc = GetDataAction.GetElementDataContext(ecId);
                        if (dc.ElementCounter.UpperBoundExceeded)
                        {
                            warning = string.Format(CultureInfo.InvariantCulture,
                                Properties.Resources.SetElementCultureInfoFormatMessage,
                                dc.ElementCounter.UpperBound);
                            IsSaveEnabled = false;
                        }
                        dc.PublishScanResults();
                    }).ConfigureAwait(false);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (ec.Element == null || ec.Element.Properties == null || ec.Element.Properties.Count == 0)
                        {
                            this.ctrlAutomatedChecks.ClearUI();
                            selectionFailure = true;
                        }
                        else
                        {
                            if (ec.DataContext.Screenshot == null && ec.DataContext.Mode != DataContextMode.Load)
                            {

                                if (ec.Element.BoundingRectangle.IsEmpty == true)
                                {
                                    MessageDialog.Show(Properties.Resources.noScreenShotAvailableMessage);
                                }
                                else
                                {
                                    this.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                                    {
                                        Application.Current.MainWindow.WindowStyle = WindowStyle.ToolWindow;
                                        Application.Current.MainWindow.Visibility = Visibility.Hidden;
                                    })).Wait();

                                    HighlightAction.GetDefaultInstance().IsEnabled = false;

                                    this.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                                    {
                                        ScreenShotAction.CaptureScreenShot(ecId);
                                        Application.Current.MainWindow.WindowStyle = WindowStyle.SingleBorderWindow;
                                        Application.Current.MainWindow.Visibility = Visibility.Visible;
                                    })).Wait();
                                }
                            }
                            this.tabControl.IsEnabled = true;
                            this.ctrlAutomatedChecks.SetElement(ec);

                            this.ElementContext = ec;

                            if (ec.DataContext.Mode == DataContextMode.Test)
                            {
                                tbiTabStop.Visibility = Visibility.Visible;
                                this.ctrlTabStop.SetElement(ec);
                            }
                            else
                            {
                                tbiTabStop.Visibility = Visibility.Collapsed;
                            }

                            var count = ec.DataContext?.GetRuleResultsViewModelList()?.Count ?? 0;
                            AutomationProperties.SetName(this, Invariant($"{Properties.Resources.detectedFailuresMessagePart1} {this.ElementContext.Element.Glimpse}. {count} {Properties.Resources.detectedFailuresMessagePart2}"));

                            if (!string.IsNullOrEmpty(warning))
                            {
                                MessageDialog.Show(warning);
                            }
                            ///Set focus on automated checks tab.
                            FireAsyncContentLoadedEvent(AsyncContentLoadedState.Completed);
                        }
                    });
                }
                catch (Exception)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Application.Current.MainWindow.Visibility = Visibility.Visible;
                        this.Clear();
                        selectionFailure = true;
                        EnableMenuButtons(false);
                        this.ElementContext = null;
                    });
                }
                finally
                {
                    Application.Current.Dispatcher.Invoke(() => {
                        this.ctrlProgressRing.Deactivate();
                        this.tbiAutomatedChecks.Focus();
                    });
                }
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Set the right view state : 
                if (selectionFailure)
                {
                    MainWin.HandleFailedSelectionReset();
                }
                else
                {
                    MainWin.SetCurrentViewAndUpdateUI(TestView.AutomatedTestResults);
                }
                //Application.Current.MainWindow.InvalidateVisual();
                //Application.Current.MainWindow.UpdateLayout();
                Application.Current.MainWindow.Visibility = Visibility.Visible;
                //Application.Current.MainWindow.Opacity = 1;
            });
        }

        /// <summary>
        /// Set test view with instructions to select an element first
        /// </summary>
        public void SetForNoSelection()
        {
            this.ctrlAutomatedChecks.ClearUI();
            this.EnableMenuButtons(false);
            this.tabControl.SelectedIndex = 1;
            this.tbiTabStop.Visibility = Visibility.Visible;
            this.tabControl.IsEnabled = false;
            this.ctrlAutomatedChecks.Visibility = Visibility.Collapsed;
            this.tbSelectElement.Visibility = Visibility.Visible;
            HighlightAction.GetDefaultInstance().Clear();
        }

        /// <summary>
        /// Notify FastPass data load completion.
        /// </summary>
        private void FireAsyncContentLoadedEvent(AsyncContentLoadedState state)
        {
            if (AutomationPeer.ListenerExists(AutomationEvents.AsyncContentLoaded))
            {
                UserControlAutomationPeer peer = UIElementAutomationPeer.FromElement(this) as UserControlAutomationPeer;
                if (peer != null)
                {
                    peer.RaiseAsyncContentLoadedEvent(new AsyncContentLoadedEventArgs(state, state == AsyncContentLoadedState.Beginning ? 0 : 100));
                }
            }
        }

        /// <summary>
        /// Clears listview
        /// </summary>
        public void Clear()
        {
            this.ctrlAutomatedChecks.ClearUI();
            this.ctrlTabStop.ClearUI();
        }

        // <summary>
        // Updates Window size with stored data
        // </summary>
        public void AdjustMainWindowSize()
        {

        }

        ///not implemented--nothing will copy
        public void CopyToClipboard()
        {
        }

        /// <summary>
        /// Refresh button is needed on main command bar
        /// if it is load mode(PlatformObject is null), disable it. 
        /// </summary>
        public bool IsRefreshEnabled { get; private set; }

        /// <summary>
        /// Save button is neeeded on main command bar
        /// if it is load mode(PlatformObject is null), disable it. 
        /// </summary>
        public bool IsSaveEnabled { get; private set; }

        /// <summary>
        /// Enable menu buttons
        /// - Save
        /// - Refresh
        /// </summary>
        /// <param name="enable"></param>
        private void EnableMenuButtons(bool enable)
        {
            this.IsSaveEnabled = enable;
            this.IsRefreshEnabled = enable;
        }

        /// <summary>
        /// Refresh
        /// </summary>
        public void Refresh()
        {
            // data will be asked to be captured again. so set the CurrentView right.
            MainWin.SetCurrentViewAndUpdateUI(TestView.CapturingData);

            HighlightImageAction.ClearDefaultInstance();

            var ecId = SelectAction.GetDefaultInstance().GetSelectedElementContextId().Value;
            SetDataAction.ReleaseDataContext(ecId);
#pragma warning disable CS4014
            // NOTE: We aren't awaiting this async call, so if you
            // touch it, consider if you need to add the await
            this.SetElement(ecId);
#pragma warning restore CS4014
        }

        /// <summary>
        /// Save
        /// </summary>
        public void Save()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = FileFilters.TestFileFilter;
            dlg.InitialDirectory = Configuration.TestReportPath;
            dlg.FileName = dlg.InitialDirectory.GetSuggestedFileName(FileType.TestResults);

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    SaveAction.SaveSnapshotZip(dlg.FileName, this.ElementContext.Id, null, A11yFileMode.Test);
                }
                catch (Exception ex)
                {
                    MessageDialog.Show(string.Format(CultureInfo.InvariantCulture, Properties.Resources.SaveException + " " + ex.Message));
                }
            }
        }

        /// <summary>
        /// Toggle highlight visibility
        /// </summary>
        /// <returns>Updated highlighter visibility</returns>
        public bool ToggleHighlighter()
        {
            if (this.ctrlTabStop.IsVisible)
            {
                this.ctrlTabStop.HighlightVisibility = !this.ctrlTabStop.HighlightVisibility;
                return this.ctrlTabStop.HighlightVisibility;
            }
            else
            {
                this.ctrlAutomatedChecks.HighlightVisibility = !this.ctrlAutomatedChecks.HighlightVisibility;
                return this.ctrlAutomatedChecks.HighlightVisibility;
            }
        }

        /// <summary>
        /// place holder for Size change. 
        /// </summary>
        public void UpdateConfigWithSize() { }

        /// <summary>
        /// understand the selection change and update view info. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // make sure that tab change is really reflected only when Test Mode is Visible. 
            if (this.Visibility == Visibility.Visible && MainWin != null && MainWin.CurrentView is TestView)
            {
                // make sure that mode is update only when it is relevant
                if (this.tbiAutomatedChecks.IsSelected && (TestView)MainWin.CurrentView == TestView.TabStop)
                {
                    MainWin.SetCurrentViewAndUpdateUI(TestView.AutomatedTestResults);
                }
                else if (this.tbiTabStop.IsSelected && (TestView)MainWin.CurrentView == TestView.AutomatedTestResults)
                {
                    MainWin.SetCurrentViewAndUpdateUI(TestView.TabStop);
                }
            }
        }

        /// <summary>
        /// Set focus on default control for mode
        /// </summary>
        public void SetFocusOnDefaultControl()
        {
            if (this.tabControl.IsEnabled)
            {
                this.tabControl.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
            else
            {
                this.tbSelectElement.Focus();
            }
        }
    }
}

