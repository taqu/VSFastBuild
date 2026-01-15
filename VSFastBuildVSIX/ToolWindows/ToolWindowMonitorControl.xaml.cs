using EnvDTE;
using EnvDTE80;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VSFastBuildVSIX.ToolWindows;
using static VSFastBuildVSIX.ToolWindows.BuildEvent;

namespace VSFastBuildVSIX
{
    public partial class ToolWindowMonitorControl : UserControl
    {
        public enum BuildRunningState
        {
            Ready = 0,
            Running,
        }
        public enum BuildStatus
        {
            AllClear = 0,
            HasWarnings,
            HasErrors,
        }

        public const int LOG_VERSION = 1;

        public const float PIX_SPACE_BETWEEN_EVENTS = 2.0f;
        public const float PIX_PER_SECOND = 20.0f;
        public const float PIX_HEIGHT = 20.0f;
        public const float PIX_LOD_THRESHOLD = 2.0f;
        public const float TIMESTEP_MS = 500.0f;

        public const int TextLabelOffset_X = 4;
        public const int TextLabelOffset_Y = 4;
        public const float MinTextLabelWidthThreshold = 50.0f; // The minimum element width to be eligible for text display
        public const float MinDotDotDotWidthThreshold = 20.0f; // The minimum element width to be eligible for a "..." display
        public const float RacingIconWidth = 20.0f;
        public const string LocalHostName = "local";
        public const string PrepareBuildStepsText = "Preparing Build Steps";
        public const long TargetPIDCheckPeriodMS = 1 * 1000;
        private const string FastBuildLogPath = @"\FastBuild\FastBuildLog.log";
        private const int MillisecondsForUpdate = 500;

        public static class CommandArgumentIndex
        {
            // Global arguments (apply to all commands)
            public const int TIME_STAMP = 0;
            public const int COMMAND_TYPE = 1;

            public const int START_BUILD_LOG_VERSION = 2;
            public const int START_BUILD_PID = 3;

            public const int START_JOB_HOST_NAME = 2;
            public const int START_JOB_EVENT_NAME = 3;

            public const int FINISH_JOB_RESULT = 2;
            public const int FINISH_JOB_HOST_NAME = 3;
            public const int FINISH_JOB_EVENT_NAME = 4;
            public const int FINISH_JOB_OUTPUT_MESSAGES = 5;

            public const int PROGRESS_STATUS_PROGRESS_PCT = 2;

            public const int GRAPH_GROUP_NAME = 2;
            public const int GRAPH_COUNTER_NAME = 3;
            public const int GRAPH_COUNTER_UNIT_TAG = 4;
            public const int GRAPH_COUNTER_VALUE = 5;
        }

        private enum BuildEventCommand
        {
            UNKNOWN = -1,
            START_BUILD = 0,
            STOP_BUILD,
            START_JOB,
            FINISH_JOB,
            PROGRESS_STATUS,
            GRAPH,
        }

        public static BitmapImage GetBitmapImage(System.Drawing.Bitmap bitmap)
        {
            BitmapImage bitmapImage = new BitmapImage();

            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
            }

            return bitmapImage;
        }

        // outputs a time string in the format: 00:00:00
        public static string GetTimeFormattedString(long timeMS)
        {
            long remainingTimeSeconds = timeMS / 1000;

            int hours = (int)(remainingTimeSeconds / (60 * 60));
            remainingTimeSeconds -= hours * 60 * 60;

            int minutes = (int)(remainingTimeSeconds / (60));
            remainingTimeSeconds -= minutes * 60;

            if (0 < hours)
            {
                return string.Format("{0}:{1:00}:{2:00}", hours, minutes, remainingTimeSeconds);
            }
            else
            {
                return string.Format("{0}:{1:00}", minutes, remainingTimeSeconds);
            }
        }

        // outputs a time string in the format: 0h 0m 0s
        public static string GetTimeFormattedString2(long timeMS)
        {
            long remainingTimeSeconds = timeMS / 1000;

            int hours = (int)(remainingTimeSeconds / (60 * 60));
            remainingTimeSeconds -= hours * 60 * 60;

            int minutes = (int)(remainingTimeSeconds / (60));
            remainingTimeSeconds -= minutes * 60;

            if (0 < hours)
            {
                return string.Format("{0}h {1}m {2}s", hours, minutes, remainingTimeSeconds);
            }
            else
            {
                return string.Format("{0}m {1}s", minutes, remainingTimeSeconds);
            }
        }

        public static long GetCurrentSystemTimeMS()
        {
            long currentTimeMS = DateTime.Now.ToFileTime() / (10 * 1000);

            return currentTimeMS;
        }

        public long BuildStartTimeMS { get { return buildStartTimeMS_; } }

        public long GetCurrentBuildTimeMS(bool bUseTimeStep = false)
        {
            long elapsedBuildTime = -buildStartTimeMS_;

            if (buildRunningState_ == BuildRunningState.Running)
            {
                long currentTimeMS = GetCurrentSystemTimeMS();

                elapsedBuildTime += currentTimeMS;

                if (bUseTimeStep)
                {
                    elapsedBuildTime = (long)(Math.Truncate(elapsedBuildTime / TIMESTEP_MS) * TIMESTEP_MS);

                    if (previousSteppedBuildTimeMS_ != elapsedBuildTime)
                    {
                        // if we have advanced in terms of stepped build Time than force a render update
                        SetConditionalRenderUpdateFlag(true);

                        previousSteppedBuildTimeMS_ = elapsedBuildTime;
                    }
                }
            }
            else
            {
                elapsedBuildTime += latestTimeStampMS_;
            }

            return elapsedBuildTime;
        }

        private long ConvertFileTimeToMS(long fileTime)
        {
            // FileTime: Contains a 64-bit value representing the number of 100-nanosecond intervals since January 1, 1601 (UTC).
            return fileTime / (10 * 1000);
        }

        private void SetConditionalRenderUpdateFlag(bool allowed)
        {
            doUpdateRender_ = allowed;
        }


        public static bool IsObjectVisible(Rect objectRect, Rect viewPort)
        {
            const double halfIncPct = 10.0f / (100.0f * 2.0f);

            double x = Math.Max(0.0f, viewPort.X - viewPort.Width * halfIncPct);
            double y = Math.Max(0.0f, viewPort.Y - viewPort.Height * halfIncPct);
            double w = viewPort.Width * (1.0 + halfIncPct);
            double h = viewPort.Height * (1.0 + halfIncPct);

            Rect largerViewport = new Rect(x, y, w, h);

            return largerViewport.IntersectsWith(objectRect) || largerViewport.Contains(objectRect);
        }

        public Rect ViewPort => viewPort_;
        public float MaxX => maxX_;
        public float MaxY => maxY_;
        public float ZoomFactor => zoomFactor_;

        // UI components
        Timebar timebar_;
        SystemPerformanceGraphsCanvas systemPerformanceGraphs_;

        // States
        private BuildRunningState buildRunningState_;
        private BuildStatus buildStatus_;
        private bool doUpdateRender_ = true;   // Controls the update of the rendered elements 

        /* Time management */
        private long previousSteppedBuildTimeMS_ = 0;
        private long buildStartTimeMS_ = 0;
        private long latestTimeStampMS_ = 0;

        private Rect viewPort_ = new Rect();
        private float maxX_ = 0.0f;
        private float maxY_ = 0.0f;
        private float zoomFactor_ = 1.0f;
        private float zoomFactorOld_ = 0.1f;
        private bool autoScrolling_ = true;
        private bool isPanning_ = false;
        private Point panReferencePosition_;
        private bool preparingBuildsteps_ = false;
        private Dictionary<string, BuildHost> buildHosts_ = new Dictionary<string, BuildHost>();
        private BuildHost localHost_;
        private ObservableCollection<OutputFilterItem> outputComboBoxFilters_ = new ObservableCollection<OutputFilterItem>();
        private bool outputTextBoxPendingLayoutUpdate_ = false;
        private DispatcherTimer timer_;

        //Input file I/O
        private FileStream fileStream_;
        private long fileStreamPosition_;
        private List<byte> fileBuffer_ = new System.Collections.Generic.List<byte>();
        private int lastProcessedPosition_;

        private int targetPID_;
        private bool isLiveSession_;
        private long lastTargetPIDCheckTimeMS_ = 0;

        private float currentProgressPCT_;
        private ToolTip statusBarProgressToolTip_ = new ToolTip();

        public ToolWindowMonitorControl()
        {
            InitializeComponent();

            // Initialize text rendering
            TextUtils.StaticInitialize();
            ToolImages.Initialize();

            // Time bar display
            timebar_ = new Timebar(TimeBarCanvas, this);

            // System Graphs display
            systemPerformanceGraphs_ = new SystemPerformanceGraphsCanvas(SystemGraphsCanvas, this);

            // Events
            this.Loaded += ToolWindowMonitorControl_Loaded;

            EventsScrollViewer.PreviewMouseWheel += MainWindow_MouseWheel;
            EventsScrollViewer.MouseWheel += MainWindow_MouseWheel;
            MouseWheel += MainWindow_MouseWheel;
            EventsCanvas.MouseWheel += MainWindow_MouseWheel;

            EventsScrollViewer.PreviewMouseLeftButtonDown += EventsScrollViewer_MouseDown;
            EventsScrollViewer.MouseDown += EventsScrollViewer_MouseDown;
            MouseDown += EventsScrollViewer_MouseDown;
            EventsCanvas.MouseDown += EventsScrollViewer_MouseDown;

            EventsScrollViewer.PreviewMouseLeftButtonUp += EventsScrollViewer_MouseUp;
            EventsScrollViewer.MouseUp += EventsScrollViewer_MouseUp;
            MouseUp += EventsScrollViewer_MouseUp;
            EventsCanvas.MouseUp += EventsScrollViewer_MouseUp;

            EventsScrollViewer.PreviewMouseDoubleClick += EventsScrollViewer_MouseDoubleClick;
            EventsScrollViewer.MouseDoubleClick += EventsScrollViewer_MouseDoubleClick;

            OutputTextBox.PreviewMouseDoubleClick += OutputTextBox_PreviewMouseDoubleClick;
            OutputTextBox.MouseDoubleClick += OutputTextBox_PreviewMouseDoubleClick;
            OutputTextBox.PreviewKeyDown += OutputTextBox_KeyDown;
            OutputTextBox.KeyDown += OutputTextBox_KeyDown;
            OutputTextBox.LayoutUpdated += OutputTextBox_LayoutUpdated;

            OutputWindowComboBox.SelectionChanged += OutputWindowComboBox_SelectionChanged;
        }

        public bool Start()
        {
            if (null != timer_)
            {
                return false;
            }
            Reset();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                //update timer
                timer_ = new DispatcherTimer();
                timer_.Tick += HandleTick;
                timer_.Interval = new TimeSpan(TimeSpan.TicksPerMillisecond * MillisecondsForUpdate);
                timer_.Start();
            }));
            return true;
        }

        public void Stop()
        {
        }

        public void Reset()
        {
            if (null != fileStream_)
            {
                fileStream_.Close();
                fileStream_ = null;
            }
            fileStreamPosition_ = 0;
            fileBuffer_.Clear();

            buildRunningState_ = BuildRunningState.Ready;
            buildStatus_ = BuildStatus.AllClear;

            buildStartTimeMS_ = GetCurrentSystemTimeMS();
            latestTimeStampMS_ = 0;

            buildHosts_.Clear();
            localHost_ = null;

            lastProcessedPosition_ = 0;
            preparingBuildsteps_ = false;

            EventsCanvas.Children.Clear();
            CoresCanvas.Children.Clear();

            preparingBuildsteps_ = true;

            // Reset the Output window text
            OutputTextBox.Text = string.Empty;

            // progress status
            UpdateBuildProgress(0.0f);
            StatusBarProgressBar.Foreground = ToolImages.StatusInitialBrush;

            // target pid
            targetPID_ = 0;
            lastTargetPIDCheckTimeMS_ = 0;

            // live build session state
            isLiveSession_ = false;

            // graphs
            SystemGraphsCanvas.Children.Clear();

            // allow a free render update on the first frame after the reset
            SetConditionalRenderUpdateFlag(true);

            // reset the cached SteppedBuildTime value
            previousSteppedBuildTimeMS_ = 0;
        }

        public void UpdateEventsCanvasMaxSize(float x, float y)
        {
            maxX_ = maxX_ < x ? x : maxX_;
            maxY_ = maxY_ < y ? y : maxY_;
        }

        private void ToolWindowMonitorControl_Loaded(object sender, RoutedEventArgs e)
        {
            StatusBarRunning.Source = ToolImages.IconRunning.ImageSource;
#if false
            Image image = new Image();
            image.Source = GetBitmapImage(VSFastBuildVSIX.Resources.Images.TimeLineTabIcon);
            image.Margin = new Thickness(5, 5, 5, 5);
            image.Width = 20.0f;
            image.Height = 20.0f;
            image.ToolTip = new ToolTip();
            ((ToolTip)image.ToolTip).Content = "Events TimeLine";
            TabItemTimeBar.Header = image;

            image = new Image();
            image.Source = GetBitmapImage(VSFastBuildVSIX.Resources.Images.TextOutputTabIcon);
            image.Margin = new Thickness(5, 5, 5, 5);
            image.Width = 20.0f;
            image.Height = 20.0f;
            image.ToolTip = new ToolTip();
            ((ToolTip)image.ToolTip).Content = "Output Window";
            TabItemOutput.Header = image;

            image = new Image();
            image.Source = GetBitmapImage(VSFastBuildVSIX.Resources.Images.SettingsTabIcon);
            image.Margin = new Thickness(5, 5, 5, 5);
            image.Width = 20.0f;
            image.Height = 20.0f;
            image.ToolTip = new ToolTip();
            ((ToolTip)image.ToolTip).Content = "Settings";
            TabItemSettings.Header = image;
#endif
        }

        #region Mouse Wheel Zooming
        private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            //handle the case where we can receive many events between 2 frames
            if (zoomFactorOld_ == zoomFactor_)
            {
                zoomFactorOld_ = zoomFactor_;
            }

            float zoomMultiplier = 1.0f;

            if (zoomFactor_ > 3.0f)
            {
                if (zoomFactor_ < 7.0f)
                {
                    zoomMultiplier = 3.0f;
                }
                else
                {
                    zoomMultiplier = 6.0f;
                }
            }
            else if (zoomFactor_ < 0.5f)
            {
                if (zoomFactor_ > 0.1f)
                {
                    zoomMultiplier = 0.3f;
                }
                else
                {
                    zoomMultiplier = 0.05f;
                }
            }

            //Accumulate some value
            float oldZoomValue = zoomFactor_;

            zoomFactor_ += zoomMultiplier * e.Delta / 1000.0f;
            zoomFactor_ = Math.Min(zoomFactor_, 30.0f);
            zoomFactor_ = Math.Max(zoomFactor_, 0.05f);

            if (oldZoomValue != zoomFactor_)
            {
                // if the zoom has changed the kick a new render update
                SetConditionalRenderUpdateFlag(true);
            }

            //disable auto-scrolling when we are zooming
            autoScrolling_ = false;

            e.Handled = true;
        }
        #endregion

        #region Mouse Panning
        private void EventsScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;

            if (e.ChangedButton == MouseButton.Left)
            {
                if (MainTabControl.SelectedIndex == (int)TAB.TimeLine)
                {
                    Rect viewPort = new Rect(0.0f, 0.0f, EventsScrollViewer.ViewportWidth, EventsScrollViewer.ViewportHeight);

                    Point mousePosition = e.GetPosition(EventsScrollViewer);

                    if (viewPort.Contains(mousePosition))
                    {
                        panReferencePosition_ = mousePosition;

                        StartPanning();

                        e.Handled = true;
                    }
                }
            }
        }

        private void EventsScrollViewer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;

            if (e.ChangedButton == MouseButton.Left && isPanning_)
            {
                Rect viewPort = new Rect(0.0f, 0.0f, EventsScrollViewer.ViewportWidth, EventsScrollViewer.ViewportHeight);

                Point mousePosition = e.GetPosition(EventsScrollViewer);

                if (viewPort.Contains(mousePosition))
                {
                    StopPanning();

                    e.Handled = true;
                }
            }
        }

        private void StartPanning()
        {
            this.Cursor = Cursors.SizeAll;
            isPanning_ = true;
        }

        private void StopPanning()
        {
            this.Cursor = Cursors.Arrow;
            isPanning_ = false;
        }

        private void UpdateMousePanning()
        {
            if (isPanning_)
            {
                Point currentMousePosition = Mouse.GetPosition(EventsScrollViewer);

                Rect viewPort = new Rect(0.0f, 0.0f, EventsScrollViewer.ViewportWidth, EventsScrollViewer.ViewportHeight);

                if (viewPort.Contains(currentMousePosition))
                {
                    Vector posDelta = (currentMousePosition - panReferencePosition_) * -1.0f;

                    panReferencePosition_ = currentMousePosition;

                    double newVerticalOffset = EventsScrollViewer.VerticalOffset + posDelta.Y;
                    newVerticalOffset = Math.Min(newVerticalOffset, EventsCanvas.Height - EventsScrollViewer.ViewportHeight);
                    newVerticalOffset = Math.Max(0.0f, newVerticalOffset);

                    double newHorizontaOffset = EventsScrollViewer.HorizontalOffset + posDelta.X;
                    newHorizontaOffset = Math.Min(newHorizontaOffset, EventsCanvas.Width - EventsScrollViewer.ViewportWidth);
                    newHorizontaOffset = Math.Max(0.0f, newHorizontaOffset);

                    EventsScrollViewer.ScrollToHorizontalOffset(newHorizontaOffset);
                    TimeBarScrollViewer.ScrollToHorizontalOffset(newHorizontaOffset);
                    SystemGraphsScrollViewer.ScrollToHorizontalOffset(newHorizontaOffset);

                    EventsScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                    CoresScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                }
                else
                {
                    StopPanning();
                }
            }
        }
        #endregion

        #region Output TextBox Double-Click Handling
        private HitTest HitTest(Point mousePosition)
        {
            HitTest result = null;

            foreach (KeyValuePair<string, BuildHost> entry in buildHosts_)
            {
                BuildHost host = entry.Value;
                result = host.HitTest(mousePosition);
                if (result != null)
                {
                    break;
                }
            }

            return result;
        }

        private void EventsScrollViewer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (MainTabControl.SelectedIndex == (int)TAB.TimeLine)
                {
                    Point mousePosition = e.GetPosition(EventsScrollViewer);

                    mousePosition.X += EventsScrollViewer.HorizontalOffset;
                    mousePosition.Y += EventsScrollViewer.VerticalOffset;

                    HitTest result = HitTest(mousePosition);

                    if (result != null && result.event_ != null)
                    {
                        string filename = result.event_.name_.Substring(1, result.event_.name_.Length - 2);

                        result.event_.HandleDoubleClickEvent();

                        e.Handled = true;
                    }
                }
            }
        }
        #endregion

        #region Output TextBox Handling
        public void ChangeOutputWindowComboBoxSelection(BuildEvent buildEvent)
        {
            int index = 0;
            foreach (OutputFilterItem filter in outputComboBoxFilters_)
            {
                if (filter.BuildEvent == buildEvent)
                {
                    OutputWindowComboBox.SelectedIndex = index;
                    break;
                }
                ++index;
            }
        }

        void ResetOutputWindowCombox()
        {
            if (null != outputComboBoxFilters_)
            {
                outputComboBoxFilters_.Clear();
            }
            else
            {
                outputComboBoxFilters_ = new ObservableCollection<OutputFilterItem>();
            }

            outputComboBoxFilters_.Add(new OutputFilterItem("ALL"));

            OutputWindowComboBox.ItemsSource = outputComboBoxFilters_;
            OutputWindowComboBox.SelectedIndex = 0;
        }


        public void AddOutputWindowFilterItem(BuildEvent buildEvent)
        {
            OutputFilterItem outputFilterItem = new OutputFilterItem(buildEvent);
            outputComboBoxFilters_.Add(outputFilterItem);
            if (0 <= OutputWindowComboBox.SelectedIndex)
            {
                OutputFilterItem selectedFilter = outputComboBoxFilters_[OutputWindowComboBox.SelectedIndex];

                if (outputFilterItem.BuildEvent != null && (selectedFilter.BuildEvent == null || outputFilterItem.BuildEvent == selectedFilter.BuildEvent))
                {
                    OutputTextBox.AppendText(outputFilterItem.BuildEvent.outputMessages_ + "\n");
                }
            }
        }

        private void OutputWindowComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            RefreshOutputTextBox();
        }

        private void RefreshOutputTextBox()
        {
            OutputTextBox.Clear();


            if (0 <= OutputWindowComboBox.SelectedIndex)
            {
                OutputFilterItem selectedFilter = outputComboBoxFilters_[OutputWindowComboBox.SelectedIndex];

                foreach (OutputFilterItem filter in outputComboBoxFilters_)
                {
                    if (filter.BuildEvent != null && (selectedFilter.BuildEvent == null || filter.BuildEvent == selectedFilter.BuildEvent))
                    {
                        OutputTextBox.AppendText(filter.BuildEvent.outputMessages_ + "\n");
                    }
                }
            }

            // Since we changed the text inside the text box we now require a layout update to refresh
            // the internal state of the UIControl
            outputTextBoxPendingLayoutUpdate_ = true;

            OutputTextBox.UpdateLayout();
        }

        /* OutputTextBox double click */
        private void OutputTextBox_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                TextBox tb = sender as TextBox;
                String doubleClickedWord = tb.SelectedText;

                if (tb.SelectionStart >= 0 && tb.SelectionLength > 0)
                {
                    try
                    {
                        string text = tb.Text;
                        int startLineIndex = text.LastIndexOf(Environment.NewLine, tb.SelectionStart) + Environment.NewLine.Length;
                        int endLineIndex = tb.Text.IndexOf(Environment.NewLine, tb.SelectionStart);

                        string selectedLineText = tb.Text.Substring(startLineIndex, endLineIndex - startLineIndex);
                        //Console.WriteLine("SelectedLine {0}", selectedLineText);

                        int startParenthesisIndex = selectedLineText.IndexOf('(');
                        int endParenthesisIndex = selectedLineText.IndexOf(')');

                        if (startParenthesisIndex > 0 && endParenthesisIndex > 0)
                        {
                            string filePath = selectedLineText.Substring(0, startParenthesisIndex);
                            string lineString = selectedLineText.Substring(startParenthesisIndex + 1, endParenthesisIndex - startParenthesisIndex - 1);

                            Int32 lineNumber = Int32.Parse(lineString);

                            VSFastBuildVSIXPackage package;
                            if (!VSFastBuildVSIXPackage.TryGetPackage(out package))
                            {
                                return;
                            }
                            Microsoft.VisualStudio.Shell.VsShellUtilities.OpenDocument(package, filePath);

                            DTE2 dte2 = package.DTE;

                            //Console.WriteLine("Window: {0}", _dte.ActiveWindow.Caption);

                            EnvDTE.TextSelection sel = dte2.ActiveDocument.Selection as EnvDTE.TextSelection;

                            sel.StartOfDocument(false);
                            sel.EndOfDocument(true);
                            sel.GotoLine(lineNumber);

                            try
                            {
                                sel.ActivePoint.TryToShow(vsPaneShowHow.vsPaneShowCentered, null);
                            }
                            catch (System.Exception ex)
                            {
                                //Console.WriteLine("Exception! " + ex.ToString());
                            }

                        }
                    }
                    catch (System.Exception ex)
                    {
                        //Console.WriteLine("Exception! " + ex.ToString());
                    }
                }
            }
        }

        public struct OutputFilterItem
        {
            private string name_ = string.Empty;
            private BuildEvent buildEvent_;

            public OutputFilterItem(string name)
            {
                name_ = name;
            }

            public OutputFilterItem(BuildEvent buildEvent)
            {
                buildEvent_ = buildEvent;
            }

            public BuildEvent BuildEvent
            {
                get { return buildEvent_; }
                private set { buildEvent_ = value; }
            }

            public string Name
            {
                get
                {
                    string result;

                    if (buildEvent_ != null)
                    {
                        result = buildEvent_.name_.Substring(1, buildEvent_.name_.Length - 2);
                    }
                    else
                    {
                        // fallback
                        result = name_;
                    }

                    const int charactersToDisplay = 50;

                    if (result.Length > charactersToDisplay)
                    {
                        result = result.Substring(result.IndexOf('\\', result.Length - charactersToDisplay));
                    }

                    return result;
                }

                set
                {
                    name_ = value;
                }
            }
        }

        private void OutputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            if (e.Key == Key.Space)
            {
                if (OutputWindowComboBox.SelectedIndex != 0)
                {
                    OutputWindowComboBox.SelectedIndex = 0;
                }
            }
            else if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                Clipboard.SetText(OutputTextBox.SelectedText);
            }
        }

        private void OutputTextBox_LayoutUpdated(object sender, EventArgs e)
        {
            outputTextBoxPendingLayoutUpdate_ = false;
        }

        private void OnClick_OutputTextBox_SelectAll(object sender, RoutedEventArgs args)
        {
            args.Handled = true;
            OutputTextBox.SelectAll();
        }

        private void OnClick_OutputTextBox_Clear(object sender, RoutedEventArgs args)
        {
            args.Handled = true;
            OutputTextBox.Clear();
        }
        #endregion

        #region Timer Tick Handling
        private void HandleTick(object sender, EventArgs e)
        {
            try
            {
                // Process the input log for new events
                ProcessInputFileStream();

                // Handling Mouse panning, we do it here because it does not necessitate a RenderUpdate
                UpdateMousePanning();

                // Call the non-expensive Render Update every frame
                RenderUpdate();

                // Call the Conditional Render Update only when needed since it is expensive
                if (IsConditionalRenderUpdateAllowed())
                {
                    ConditionalRenderUpdate();
                    UpdateStatusBar();
                    SetConditionalRenderUpdateFlag(false);
                }

            }
            catch (System.Exception ex)
            {
                //Console.WriteLine("Exception detected... Restarting! details: " + ex.ToString());
                ResetState();
            }
        }

        private bool IsConditionalRenderUpdateAllowed()
        {
            return doUpdateRender_;
        }

        private bool CanRead()
        {
            return fileStream_ != null && fileStream_.CanRead;
        }

        private byte[] buffer_;

        private bool HasFileContentChanged()
        {
            bool bFileChanged = false;

            if (fileStream_.Length < fileStreamPosition_)
            {
                // detect if the current file has been overwritten with less data
                bFileChanged = true;
            }
            else if (0 < fileBuffer_.Count)
            {
                // detect if the current file has been overwritten with different data

                int numBytesToCompare = Math.Min(fileBuffer_.Count, 256);

                if (null == buffer_ || buffer_.Length < numBytesToCompare)
                {
                    buffer_ = new byte[numBytesToCompare];
                }

                fileStream_.Seek(0, SeekOrigin.Begin);

                int numBytesRead = fileStream_.Read(buffer_, 0, numBytesToCompare);
                if (0 < numBytesRead)
                {
                    Debug.Assert(numBytesRead == numBytesToCompare, "Could not read the expected amount of data from the log file...!");

                    for (int i = 0; i < numBytesToCompare; ++i)
                    {
                        if (buffer_[i] != fileBuffer_[i])
                        {
                            bFileChanged = true;
                            break;
                        }
                    }
                }
            }

            return bFileChanged;
        }

        private bool BuildRestarted()
        {
            return CanRead() && HasFileContentChanged();
        }

        private void ResetState()
        {
            fileStreamPosition_ = 0;
            fileStream_.Seek(0, SeekOrigin.Begin);

            fileBuffer_.Clear();

            buildRunningState_ = BuildRunningState.Ready;
            buildStatus_ = BuildStatus.AllClear;

            buildStartTimeMS_ = GetCurrentSystemTimeMS();
            latestTimeStampMS_ = 0;

            buildHosts_.Clear();
            localHost_ = null;

            lastProcessedPosition_ = 0;
            preparingBuildsteps_ = false;

            EventsCanvas.Children.Clear();
            CoresCanvas.Children.Clear();

            // Start by adding a local host
            localHost_ = new BuildHost(LocalHostName, this);
            buildHosts_.Add(LocalHostName, localHost_);

            // Always add the prepare build steps event first
            BuildEvent buildEvent = new BuildEvent(this, PrepareBuildStepsText, buildStartTimeMS_);
            localHost_.OnStartEvent(buildEvent);
            preparingBuildsteps_ = true;

            // Reset the Output window text
            OutputTextBox.Text = string.Empty;

            // Change back the tabcontrol to the TimeLine automatically
            MainTabControl.SelectedIndex = (int)TAB.TimeLine;

            ResetOutputWindowCombox();

            // progress status
            UpdateBuildProgress(0.0f);
            StatusBarProgressBar.Foreground = ToolImages.StatusInitialBrush;

            // reset to autoscrolling ON
            autoScrolling_ = true;

            // reset our zoom levels
            zoomFactor_ = 1.0f;
            zoomFactorOld_ = 0.1f;

            // target pid
            targetPID_ = 0;
            lastTargetPIDCheckTimeMS_ = 0;

            // live build session state
            isLiveSession_ = false;

            // graphs
            SystemGraphsCanvas.Children.Clear();
            systemPerformanceGraphs_ = new SystemPerformanceGraphsCanvas(SystemGraphsCanvas, this);

            // allow a free render update on the first frame after the reset
            SetConditionalRenderUpdateFlag(true);

            // reset the cached SteppedBuildTime value
            previousSteppedBuildTimeMS_ = 0;
        }

        private void BuildRestart()
        {
            fileStreamPosition_ = 0;
            fileStream_.Seek(0, SeekOrigin.Begin);

            fileBuffer_.Clear();

            buildRunningState_ = BuildRunningState.Ready;
            buildStatus_ = BuildStatus.AllClear;

            if (0 == buildStartTimeMS_)
            {
                buildStartTimeMS_ = GetCurrentSystemTimeMS();
            }
            buildHosts_.Clear();
            localHost_ = null;

            lastProcessedPosition_ = 0;
            preparingBuildsteps_ = false;

            // Start by adding a local host
            localHost_ = new BuildHost(LocalHostName, this);
            buildHosts_.Add(LocalHostName, localHost_);

            // Always add the prepare build steps event first
            BuildEvent buildEvent = new BuildEvent(this, PrepareBuildStepsText, GetCurrentSystemTimeMS());
            localHost_.OnStartEvent(buildEvent);
            preparingBuildsteps_ = true;

            // progress status
            UpdateBuildProgress(0.0f);
            StatusBarProgressBar.Foreground = ToolImages.StatusInitialBrush;

            // target pid
            targetPID_ = 0;
            lastTargetPIDCheckTimeMS_ = 0;

            // live build session state
            isLiveSession_ = false;

            // allow a free render update on the first frame after the reset
            SetConditionalRenderUpdateFlag(true);
        }

        private void UpdateStatusBar()
        {
            switch (buildRunningState_)
            {
                case BuildRunningState.Ready:
                    StatusBarBuildStatus.Text = "Ready";
                    break;
                case BuildRunningState.Running:
                    StatusBarBuildStatus.Text = "Running";
                    break;
            }

            int numCores = 0;
            foreach (KeyValuePair<string, BuildHost> entry in buildHosts_)
            {
                BuildHost host = entry.Value as BuildHost;

                if (host._name.Contains(LocalHostName))
                {
                    numCores += host._cores.Count;
                }
                else
                {
                    numCores += host._cores.Count - 1;
                }
            }

            StatusBarDetails.Text = string.Format("{0} Agents - {1} Cores", buildHosts_.Count, numCores);
        }

        private void UpdateBuildProgress(float progressPCT, bool buildStop=false)
        {
            currentProgressPCT_ = progressPCT;

            StatusBarBuildTime.Text = string.Format("Duration: {0}", GetTimeFormattedString2(GetCurrentBuildTimeMS()));

            StatusBarProgressBar.Value = currentProgressPCT_;
            StatusBarProgressBar.ToolTip = statusBarProgressToolTip_;
            if (buildStop)
            {
                switch (buildStatus_)
                {
                    case BuildStatus.HasErrors:
                        StatusBarProgressBar.Foreground = Brushes.Red;
                        break;
                    case BuildStatus.HasWarnings:
                        StatusBarProgressBar.Foreground = Brushes.Yellow;
                        break;
                    default:
                        StatusBarProgressBar.Foreground = ToolImages.StatusInitialBrush;
                        break;
                }
                ToolImages.StatusProgressBrush.Viewbox = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            }
            else
            {
                float rate = currentProgressPCT_/100.0f;
                ToolImages.StatusProgressBrush.Viewbox = new Rect(0.0f, 0.0f, rate, 1.0f);
                StatusBarProgressBar.Foreground = ToolImages.StatusProgressBrush;
            }

            statusBarProgressToolTip_.Content = string.Format("{0:0.00}%", currentProgressPCT_);
        }

        private void UpdateZoomTargetPosition()
        {
            if (zoomFactorOld_ != zoomFactor_)
            {
                Point mouseScreenPosition = Mouse.GetPosition(EventsCanvas);

                //Find out the time position the mouse (canvas relative) was at pre-zoom
                double mouseTimePosition = mouseScreenPosition.X / (zoomFactorOld_ * PIX_PER_SECOND);

                double screenSpaceMousePositionX = mouseScreenPosition.X - EventsScrollViewer.HorizontalOffset;

                //Determine the new canvas relative mouse position post-zoom
                double newMouseCanvasPosition = mouseTimePosition * zoomFactor_ * PIX_PER_SECOND;

                double newHorizontalScrollOffset = Math.Max(0.0f, newMouseCanvasPosition - screenSpaceMousePositionX);

                EventsScrollViewer.ScrollToHorizontalOffset(newHorizontalScrollOffset);
                TimeBarScrollViewer.ScrollToHorizontalOffset(newHorizontalScrollOffset);
                SystemGraphsScrollViewer.ScrollToHorizontalOffset(newHorizontalScrollOffset);

                zoomFactorOld_ = zoomFactor_;
            }
        }

        private void UpdateViewport()
        {
            Rect newViewport = new Rect(EventsScrollViewer.HorizontalOffset, EventsScrollViewer.VerticalOffset, EventsScrollViewer.ViewportWidth, EventsScrollViewer.ViewportHeight);
            if (!ViewPort.Equals(newViewport))
            {
                foreach (KeyValuePair<string, BuildHost> entry in buildHosts_)
                {
                    BuildHost host = entry.Value;
                    foreach (CPUCore core in host._cores)
                    {
                        core.InvalidateVisual();
                    }
                }

                viewPort_ = newViewport;
            }
        }

        private void ConditionalRenderUpdate()
        {
            // Resolve ViewPort center/size in case of zoom in/out event
            UpdateZoomTargetPosition();

            // Update the viewport and decide if we have to redraw the Events canvas
            UpdateViewport();

            maxX_ = 0.0f;
            maxY_ = 0.0f;

            double X = 10;
            double Y = 10;

            // Always draw the local host first
            if (localHost_ != null)
            {
                localHost_.RenderUpdate(X, ref Y);
            }

            foreach (KeyValuePair<string, BuildHost> entry in buildHosts_)
            {
                BuildHost host = entry.Value as BuildHost;

                if (host != localHost_)
                {
                    host.RenderUpdate(X, ref Y);
                }
            }

            EventsCanvas.Width = TimeBarCanvas.Width = SystemGraphsCanvas.Width = maxX_ + ViewPort.Width * 0.25f;
            EventsCanvas.Height = CoresCanvas.Height = maxY_;

#if ENABLE_RENDERING_STATS
        _numShapesDrawn = 0;
        _numTextElementsDrawn = 0;
#endif
        }


        private void RenderUpdate()
        {
            timebar_.RenderUpdate(10, 0, ZoomFactor);
            systemPerformanceGraphs_.RenderUpdate(10, 0, ZoomFactor);

            // Update the tooltips
            Point mousePosition = Mouse.GetPosition(EventsScrollViewer);

            mousePosition.X += EventsScrollViewer.HorizontalOffset;
            mousePosition.Y += EventsScrollViewer.VerticalOffset;

            foreach (KeyValuePair<string, BuildHost> entry in buildHosts_)
            {
                BuildHost host = entry.Value;

                if (host.UpdateToolTip(mousePosition))
                {
                    break;
                }
            }
        }

        private long RegisterNewTimeStamp(long fileTime)
        {
            latestTimeStampMS_ = ConvertFileTimeToMS(fileTime);

            return latestTimeStampMS_;
        }

        private BuildEventCommand TranslateBuildEventCommand(string commandString)
        {
            BuildEventCommand output = BuildEventCommand.UNKNOWN;

            switch (commandString)
            {
                case "START_BUILD":
                    output = BuildEventCommand.START_BUILD;
                    break;
                case "STOP_BUILD":
                    output = BuildEventCommand.STOP_BUILD;
                    break;
                case "START_JOB":
                    output = BuildEventCommand.START_JOB;
                    break;
                case "FINISH_JOB":
                    output = BuildEventCommand.FINISH_JOB;
                    break;
                case "PROGRESS_STATUS":
                    output = BuildEventCommand.PROGRESS_STATUS;
                    break;
                case "GRAPH":
                    output = BuildEventCommand.GRAPH;
                    break;
            }

            return output;
        }

        /* Commands parsing feature */
        private BuildEventState TranslateBuildEventState(string eventString)
        {
            BuildEventState output = BuildEventState.UNKOWN;

            switch (eventString)
            {
                case "FAILED":
                case "ERROR":
                    output = BuildEventState.FAILED;
                    break;
                case "SUCCESS":
                case "SUCCESS_COMPLETE":
                    output = BuildEventState.SUCCEEDED_COMPLETED;
                    break;
                case "SUCCESS_CACHED":
                    output = BuildEventState.SUCCEEDED_CACHED;
                    break;
                case "SUCCESS_PREPROCESSED":
                    output = BuildEventState.SUCCEEDED_PREPROCESSED;
                    break;
                case "TIMEOUT":
                    output = BuildEventState.TIMEOUT;
                    break;
            }

            return output;
        }

        private void UpdateBuildStatus(BuildEventState jobResult)
        {
            BuildStatus newBuildStatus = buildStatus_;

            switch (jobResult)
            {
                case BuildEventState.FAILED:
                    newBuildStatus = BuildStatus.HasErrors;
                    break;

                case BuildEventState.TIMEOUT:
                case BuildEventState.SUCCEEDED_COMPLETED_WITH_WARNINGS:
                    if ((int)buildStatus_ < (int)BuildStatus.HasWarnings)
                    {
                        newBuildStatus = BuildStatus.HasWarnings;
                    }
                    break;
            }

            if (buildStatus_ != newBuildStatus)
            {
                switch (newBuildStatus)
                {
                    case BuildStatus.HasErrors:
                        StatusBarProgressBar.Foreground = Brushes.Red;
                        break;
                    case BuildStatus.HasWarnings:
                        StatusBarProgressBar.Foreground = Brushes.Yellow;
                        break;
                    default:
                        StatusBarProgressBar.Foreground = ToolImages.StatusInitialBrush;
                        break;
                }
                buildStatus_ = newBuildStatus;
            }
        }

        public static void TruncateLogFile()
        {
            string path = System.Environment.GetEnvironmentVariable("TEMP") + FastBuildLogPath;
            try
            {
                using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Write))
                {
                    fileStream.SetLength(0);
                    fileStream.Flush();
                }
            }
            catch
            {
            }
        }

        private void ProcessInputFileStream()
        {
            if (fileStream_ == null)
            {
                string path = System.Environment.GetEnvironmentVariable("TEMP") + @"\FastBuild\FastBuildLog.log";

                if (!Directory.Exists(System.IO.Path.GetDirectoryName(path)))
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                }

                try
                {
                    fileStream_ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    ResetState();
                }
                catch (System.Exception ex)
                {
                    //Console.WriteLine("Exception! " + ex.ToString());
                    // the log file does not exist, bail out...
                    return;
                }
            }

            // The file has been emptied so we must reset our state and start over
            if (BuildRestarted())
            {
                BuildRestart();
                return;
            }

            // Read all the new data and append it to our _fileBuffer
            int numBytesToRead = (int)(fileStream_.Length - fileStreamPosition_);

            if (numBytesToRead > 0)
            {
                byte[] buffer = new byte[numBytesToRead];

                fileStream_.Seek(fileStreamPosition_, SeekOrigin.Begin);

                int numBytesRead = fileStream_.Read(buffer, 0, numBytesToRead);

                Debug.Assert(numBytesRead == numBytesToRead, "Could not read the expected amount of data from the log file...!");

                fileStreamPosition_ += numBytesRead;

                fileBuffer_.AddRange(buffer);

                //Scan the current buffer looking for the last line position
                int newPayloadStart = lastProcessedPosition_;
                int newPayLoadSize = -1;
                for (int i = fileBuffer_.Count - 1; i > lastProcessedPosition_; --i)
                {
                    if (fileBuffer_[i] == '\n')
                    {
                        newPayLoadSize = i - newPayloadStart;
                        break;
                    }
                }

                if (newPayLoadSize > 0)
                {
                    // we received new events, allow the render update to kick
                    SetConditionalRenderUpdateFlag(true);

                    string newEventsRaw = System.Text.Encoding.Default.GetString(fileBuffer_.GetRange(lastProcessedPosition_, newPayLoadSize).ToArray());
                    string[] newEvents = newEventsRaw.Split(new char[] { '\n' });

                    foreach (string eventString in newEvents)
                    {
                        string[] tokens = Regex.Matches(eventString, @"[\""].+?[\""]|[^ ]+")
                                         .Cast<Match>()
                                         .Select(m => m.Value)
                                         .ToList().ToArray();

                        // TODO More error handling...
                        if (2 <= tokens.Length)
                        {
                            // let's get the command timestamp and update our internal time reference
                            long eventFileTime = long.Parse(tokens[CommandArgumentIndex.TIME_STAMP]);
                            long eventLocalTimeMS = RegisterNewTimeStamp(eventFileTime);

                            // parse the command
                            string commandString = tokens[CommandArgumentIndex.COMMAND_TYPE];
                            BuildEventCommand command = TranslateBuildEventCommand(commandString);

                            switch (command)
                            {
                                case BuildEventCommand.START_BUILD:
                                    if (buildRunningState_ == BuildRunningState.Ready)
                                    {
                                        ExecuteCommandStartBuild(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.STOP_BUILD:
                                    if (buildRunningState_ == BuildRunningState.Running)
                                    {
                                        ExecuteCommandStopBuild(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.START_JOB:
                                    if (buildRunningState_ == BuildRunningState.Running)
                                    {
                                        ExecuteCommandStartJob(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.FINISH_JOB:
                                    if (buildRunningState_ == BuildRunningState.Running)
                                    {
                                        ExecuteCommandFinishJob(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.PROGRESS_STATUS:
                                    if (buildRunningState_ == BuildRunningState.Running)
                                    {
                                        ExecuteCommandProgressStatus(tokens);
                                    }
                                    break;
                                case BuildEventCommand.GRAPH:
                                    if (buildRunningState_ == BuildRunningState.Running)
                                    {
                                        ExecuteCommandGraph(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                default:
                                    // Skipping unknown commands
                                    break;
                            }
                        }
                    }

                    lastProcessedPosition_ += newPayLoadSize;
                }
            }
            else if (buildRunningState_ == BuildRunningState.Running && PollIsTargetProcessRunning() == false)
            {
                // Detect canceled builds
                latestTimeStampMS_ = GetCurrentSystemTimeMS();

                ExecuteCommandStopBuild(null, latestTimeStampMS_);
            }
        }

        private static bool IsTargetProcessRunning(int pid)
        {
            System.Diagnostics.Process[] processlist = System.Diagnostics.Process.GetProcesses();
            foreach (System.Diagnostics.Process proc in processlist)
            {
                if (proc.Id == pid)
                {
                    return true;
                }
            }
            return false;
        }

        private bool PollIsTargetProcessRunning()
        {
            // assume the process is running
            bool bIsRunning = true;
            if (targetPID_ != 0 && buildRunningState_ == BuildRunningState.Running)
            {
                long currentTimeMS = GetCurrentSystemTimeMS();

                if (TargetPIDCheckPeriodMS < (currentTimeMS - lastTargetPIDCheckTimeMS_))
                {
                    bIsRunning = IsTargetProcessRunning(targetPID_);
                    lastTargetPIDCheckTimeMS_ = currentTimeMS;
                }
            }
            return bIsRunning;
        }

        // Commands handling
        private void ExecuteCommandStartBuild(string[] tokens, long eventLocalTimeMS)
        {
            int logVersion = int.Parse(tokens[CommandArgumentIndex.START_BUILD_LOG_VERSION]);

            if (logVersion == LOG_VERSION)
            {
                int targetPID = int.Parse(tokens[CommandArgumentIndex.START_BUILD_PID]);

                // remember our valid targetPID
                targetPID_ = targetPID;

                // determine if we are in a live session (target PID is running when we receive a start build command)
                isLiveSession_ = IsTargetProcessRunning(targetPID_);

                systemPerformanceGraphs_.OpenSession(isLiveSession_, targetPID_);

                // Record the start time
                //buildStartTimeMS_ = eventLocalTimeMS;

                buildRunningState_ = BuildRunningState.Running;

                // start the gif "building" animation

                ToolTip newToolTip = new ToolTip();
                StatusBarRunning.ToolTip = newToolTip;
                newToolTip.Content = "Build in Progress...";
                if (preparingBuildsteps_)
                {
                    localHost_.OnCompleteEvent(eventLocalTimeMS, PrepareBuildStepsText, string.Empty, BuildEventState.SUCCEEDED_COMPLETED, string.Empty);
                    preparingBuildsteps_ = false;
                }
            }
        }

        private void ExecuteCommandStopBuild(string[] tokens, long eventLocalTimeMS)
        {
            long timeStamp = eventLocalTimeMS;//Math.Max(eventLocalTimeMS - buildStartTimeMS_, 0);

            if (preparingBuildsteps_)
            {
                localHost_.OnCompleteEvent(timeStamp, PrepareBuildStepsText, string.Empty, BuildEventState.SUCCEEDED_COMPLETED, string.Empty);
                preparingBuildsteps_ = false;
            }

            // Stop all the active events currently running
            foreach (var entry in buildHosts_)
            {
                BuildHost host = entry.Value;
                foreach (CPUCore core in host._cores)
                {
                    core.UnScheduleEvent(timeStamp, PrepareBuildStepsText, BuildEventState.TIMEOUT, false, string.Empty, true);
                }
            }

            buildRunningState_ = BuildRunningState.Ready;

            StatusBarRunning.ToolTip = null;

            UpdateBuildProgress(100.0f, true);

            if (isLiveSession_)
            {
                systemPerformanceGraphs_.CloseSession();
                isLiveSession_ = false;
            }
        }

        private void ExecuteCommandStartJob(string[] tokens, long eventLocalTimeMS)
        {
            long timeStamp = eventLocalTimeMS;//Math.Max(eventLocalTimeMS - buildStartTimeMS_, 0);

            string hostName = tokens[CommandArgumentIndex.START_JOB_HOST_NAME];
            string eventName = tokens[CommandArgumentIndex.START_JOB_EVENT_NAME];

            if (preparingBuildsteps_)
            {
                localHost_.OnCompleteEvent(timeStamp, PrepareBuildStepsText, hostName, BuildEventState.SUCCEEDED_COMPLETED, string.Empty);

                preparingBuildsteps_ = false;
            }

            BuildEvent newEvent = new BuildEvent(this, eventName, timeStamp);

            BuildHost host = null;
            if (buildHosts_.ContainsKey(hostName))
            {
                host = buildHosts_[hostName];
            }
            else
            {
                // discovered a new host!
                host = new BuildHost(hostName, this);
                buildHosts_.Add(hostName, host);
            }


            // Find out if this new Job is local and is racing another remote one
            if (host.bLocalHost)
            {
                foreach (var entry in buildHosts_)
                {
                    BuildHost otherHost = entry.Value;

                    if (otherHost != host)
                    {
                        if (otherHost.FindAndFlagRacingEvents(eventName))
                        {
                            if (!newEvent.isRacingJob_)
                            {
                                newEvent.isRacingJob_ = true;
                            }
                        }
                    }
                }
            }

            host.OnStartEvent(newEvent);
        }

        private void ExecuteCommandFinishJob(string[] tokens, long eventLocalTimeMS)
        {
            long timeStamp = eventLocalTimeMS;//Math.Max(eventLocalTimeMS - buildStartTimeMS_, 0);

            string jobResultString = tokens[CommandArgumentIndex.FINISH_JOB_RESULT];
            string hostName = tokens[CommandArgumentIndex.FINISH_JOB_HOST_NAME];
            string eventName = tokens[CommandArgumentIndex.FINISH_JOB_EVENT_NAME];

            string eventOutputMessages = "";

            // Optional parameters
            if (tokens.Length > CommandArgumentIndex.FINISH_JOB_OUTPUT_MESSAGES)
            {
                eventOutputMessages = tokens[CommandArgumentIndex.FINISH_JOB_OUTPUT_MESSAGES].Substring(1, tokens[CommandArgumentIndex.FINISH_JOB_OUTPUT_MESSAGES].Length - 2);
            }

            BuildEventState jobResult = TranslateBuildEventState(jobResultString);

            foreach (var entry in buildHosts_)
            {
                BuildHost host = entry.Value;

                host.OnCompleteEvent(timeStamp, eventName, hostName, jobResult, eventOutputMessages);
            }

            UpdateBuildStatus(jobResult);
        }

        private void ExecuteCommandProgressStatus(string[] tokens)
        {
            float progressPCT = float.Parse(tokens[CommandArgumentIndex.PROGRESS_STATUS_PROGRESS_PCT], CultureInfo.InvariantCulture);

            // Update the build status after each job's result
            UpdateBuildProgress(progressPCT);
        }


        private void ExecuteCommandGraph(string[] tokens, long eventLocalTimeMS)
        {
            long timeStamp = Math.Max(eventLocalTimeMS - buildStartTimeMS_, 0);

            string groupName = tokens[CommandArgumentIndex.GRAPH_GROUP_NAME];
            string counterName = tokens[CommandArgumentIndex.GRAPH_COUNTER_NAME].Substring(1, tokens[CommandArgumentIndex.GRAPH_COUNTER_NAME].Length - 2); // Remove the quotes at the start and end 
            string counterUnitTag = tokens[CommandArgumentIndex.GRAPH_COUNTER_UNIT_TAG];
            float value = float.Parse(tokens[CommandArgumentIndex.GRAPH_COUNTER_VALUE], CultureInfo.InvariantCulture);

            systemPerformanceGraphs_.HandleLogEvent(timeStamp, groupName, counterName, counterUnitTag, value);
        }
        #endregion

        private void MainTabControll_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (e.Source is TabControl)
            {
                TabControl tabControl = e.Source as TabControl;

                if (tabControl.SelectedIndex == (int)TAB.Output)
                {
                    OutputTextBox.UpdateLayout();

                    outputTextBoxPendingLayoutUpdate_ = true;
                }
            }
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentWidthChange == 0)
            {
                if (EventsScrollViewer.HorizontalOffset == EventsScrollViewer.ScrollableWidth)
                {
                    autoScrolling_ = true;
                }
                else
                {
                    autoScrolling_ = false;
                }
            }

            if (autoScrolling_ && e.ExtentWidthChange != 0)
            {
                EventsScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.ExtentWidth);

                TimeBarScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.ExtentWidth);

                SystemGraphsScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.ExtentWidth);
            }

            if (e.VerticalChange != 0)
            {
                CoresScrollViewer.ScrollToVerticalOffset(EventsScrollViewer.VerticalOffset);

                UpdateViewport();
            }

            if (e.HorizontalChange != 0)
            {
                TimeBarScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.HorizontalOffset);

                SystemGraphsScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.HorizontalOffset);

                UpdateViewport();
            }
        }

        private void CheckBox_SystemGraph_Checked(object sender, RoutedEventArgs e)
        {
            systemPerformanceGraphs_.SetVisibility((bool)(sender as CheckBox).IsChecked);
        }

        private void CheckBox_SystemGraph_Unchecked(object sender, RoutedEventArgs e)
        {
            systemPerformanceGraphs_.SetVisibility((bool)(sender as CheckBox).IsChecked);

        }

        private void OnClick_SettingsAutoScroll(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            autoScrolling_ = true;
        }
    }
}
