using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using static VSFastBuildVSIX.ToolWindowMonitorControl;

namespace VSFastBuildVSIX.ToolWindows
{
    internal enum BuildEventState
    {
        UNKOWN = 0,
        IN_PROGRESS,
        FAILED,
        SUCCEEDED_COMPLETED,
        SUCCEEDED_COMPLETED_WITH_WARNINGS,
        SUCCEEDED_CACHED,
        SUCCEEDED_PREPROCESSED,
        TIMEOUT
    }

    internal class BuildEvent
    {
        // Constants
        private const int TextLavelOffset = 4;
        private const float MinTextLabelWidthThreshold = 50.0f;
        private const float MinDotDotDotWidthThreshold = 20.0f;
        private const float RacingIconWidth = 20.0f;

        // Attributes
        public CPUCore core_;

        public Int64 timeStarted_;      // in ms
        public Int64 timeFinished_;     // in ms

        public bool isRacingJob_ = false;   // tells us if this is a local job that is racing a remote one
        public bool wonRace_ = true;

        public string name_;
        public string fileName_; // extracted from the full name

        public BuildEventState state_;

        public string outputMessages_;

        public string toolTipText_;

        // WPF rendering stuff
        public ImageBrush brush_ = null;

        // Coordinates
        public System.Windows.Rect bordersRect_;
        public System.Windows.Rect progressRect_;

        // LOD/Culling
        public bool _isInLowLOD = false;
        public bool _isDirty = false;

        // Static Members
        public static bool _sbInitialized = false;
        public static ImageBrush _sSuccessCodeBrush = new ImageBrush();
        public static ImageBrush _sSuccessNonCodeBrush = new ImageBrush();
        public static ImageBrush _sSuccessPreprocessedBrush = new ImageBrush();
        public static ImageBrush _sSuccessCachedBrush = new ImageBrush();
        public static ImageBrush _sFailedBrush = new ImageBrush();
        public static ImageBrush _sTimeoutBrush = new ImageBrush();
        public static ImageBrush _sRunningBrush = new ImageBrush();
        public static ImageBrush _sRacingIconBrush = new ImageBrush();
        public static ImageBrush _sRacingWinIconBrush = new ImageBrush();
        public static ImageBrush _sRacingLostIconBrush = new ImageBrush();

        public static void StaticInitialize()
        {
            _sSuccessCodeBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Success_code);
            _sSuccessNonCodeBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Success_noncode);
            _sSuccessPreprocessedBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Success_preprocessed);
            _sSuccessCachedBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Success_cached);
            _sFailedBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Failed);
            _sTimeoutBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Timeout);
            _sRunningBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Running);
            _sRacingIconBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.race_flag);
            _sRacingWinIconBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.race_flag_win);
            _sRacingLostIconBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.race_flag_lost);

            _sbInitialized = true;
        }

        public BuildEvent(string name, Int64 timeStarted)
        {
            // Lazy initialize static resources
            if (!_sbInitialized)
            {
                StaticInitialize();
            }

            name_ = name;

            toolTipText_ = name_.Replace("\"", "");

            fileName_ = System.IO.Path.GetFileName(name_.Replace("\"", ""));

            timeStarted_ = timeStarted;

            state_ = BuildEventState.IN_PROGRESS;
        }

        public void SetOutputMessages(string outputMessages)
        {
            char[] newLineSymbol = new char[1];
            newLineSymbol[0] = (char)12;

            // Todo: Remove this crap!
            outputMessages_ = outputMessages.Replace(new string(newLineSymbol), Environment.NewLine);
        }

        public void Start(CPUCore core)
        {
            core_ = core;

            brush_ = _sRunningBrush;

            toolTipText_ = "BUILDING: " + name_.Replace("\"", "");
        }

        public void Stop(Int64 timeFinished, BuildEventState jobResult, bool bIsLocalJob)
        {
            timeFinished_ = timeFinished;

            double totalTimeSeconds = (timeFinished_ - timeStarted_) / 1000.0f;

            // uncomment to catch negative times
            Debug.Assert(totalTimeSeconds >= 0.0f);

            //if (totalTimeSeconds <=0.0f)
            //{
            //    totalTimeSeconds = 0.001f;
            //}

            toolTipText_ = string.Format("{0}", name_.Replace("\"", "")) + "\nStatus: ";

            state_ = jobResult;

            switch (state_)
            {
                case BuildEventState.SUCCEEDED_COMPLETED:
                    {
                        if (name_.Contains(".obj"))
                        {
                            brush_ = _sSuccessCodeBrush;
                        }
                        else
                        {
                            brush_ = _sSuccessNonCodeBrush;
                        }
                        toolTipText_ += "Success";

                        if (bIsLocalJob)
                        {
                            toolTipText_ += " (Won Race!)";

                            wonRace_ = true;
                        }
                        else
                        {
                            toolTipText_ += " (Lost Race!)";

                            wonRace_ = false;
                        }
                    }
                    break;

                case BuildEventState.SUCCEEDED_CACHED:
                    {
                        brush_ = _sSuccessCachedBrush;
                        toolTipText_ += "Success(Cached)";
                    }
                    break;

                case BuildEventState.SUCCEEDED_PREPROCESSED:
                    {
                        brush_ = _sSuccessPreprocessedBrush;
                        toolTipText_ += "Success(Preprocess)";
                    }
                    break;

                case BuildEventState.FAILED:
                    {
                        brush_ = _sFailedBrush;
                        toolTipText_ += "Errors";
                    }
                    break;

                case BuildEventState.TIMEOUT:
                    {
                        brush_ = _sTimeoutBrush;
                        toolTipText_ += "Timeout";
                    }
                    break;

                default:
                    break;
            }

            toolTipText_ += "\nDuration: " + GetTimeFormattedString(timeFinished_ - timeStarted_);
            toolTipText_ += "\nStart Time: " + GetTimeFormattedString(timeStarted_);
            toolTipText_ += "\nEnd Time: " + GetTimeFormattedString(timeFinished_);

            if (null != outputMessages_ && outputMessages_.Length > 0)
            {
                // show only an extract of the errors so we don't flood the visual
                int textLength = Math.Min(outputMessages_.Length, 100);

                toolTipText_ += "\n" + outputMessages_.Substring(0, textLength);
                toolTipText_ += "... [Double-Click on the event to see more details]";

                outputMessages_ = string.Format("[Output {0}]: {1}", name_.Replace("\"", ""), Environment.NewLine) + outputMessages_;

                _StaticWindow.AddOutputWindowFilterItem(this);
            }
        }

        public bool JumpToEventLineInOutputBox()
        {
            bool bSuccess = false;

            int index = _StaticWindow.OutputTextBox.Text.IndexOf(name_.Replace("\"", ""));

            int lineNumber = _StaticWindow.OutputTextBox.Text.Substring(0, index).Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length;

            _StaticWindow.OutputTextBox.ScrollToLine(lineNumber - 1);

            int position = _StaticWindow.OutputTextBox.GetCharacterIndexFromLineIndex(lineNumber - 1);
            if (position >= 0)
            {
                int lineEnd = _StaticWindow.OutputTextBox.Text.IndexOf(Environment.NewLine, position);
                if (lineEnd < 0)
                {
                    lineEnd = _StaticWindow.OutputTextBox.Text.Length;
                }

                _StaticWindow.OutputTextBox.Select(position, lineEnd - position);
            }

            return bSuccess;
        }

        public bool HandleDoubleClickEvent()
        {
            bool bHandled = true;

            if (state_ != BuildEventState.IN_PROGRESS && outputMessages_ != null && outputMessages_.Length > 0)
            {
                // Switch to the Output Window Tab item
                _StaticWindow.MyTabControl.SelectedIndex = (int)eTABs.TAB_OUTPUT;

                _StaticWindow.ChangeOutputWindowComboBoxSelection(this);
            }

            return bHandled;
        }

        public HitTestResult HitTest(Point localMousePosition)
        {
            HitTestResult result = null;

            if (bordersRect_.Contains(localMousePosition))
            {
                result = new HitTestResult(this.core_._parent, this.core_, this);
            }

            return result;
        }

        public void RenderUpdate(ref double X, double Y)
        {
            long duration = 0;

            bool bIsCompleted = false;

            double OriginalWidthInPixels = 0.0f;
            double AdjustedWidthInPixels = 0.0f;

            double borderRectWidth = 0.0f;

            if (state_ == BuildEventState.IN_PROGRESS)
            {
                // Event is in progress
                duration = (long)Math.Max(0.0f, GetCurrentBuildTimeMS(true) - timeStarted_);

                Point textSize = TextUtils.ComputeTextSize(fileName_);

                OriginalWidthInPixels = AdjustedWidthInPixels = _zoomFactor * pix_per_second * (double)duration / (double)1000;

                borderRectWidth = OriginalWidthInPixels + pix_per_second * cTimeStepMS / 1000.0f;

                borderRectWidth = Math.Max(Math.Min(_cMinTextLabelWidthThreshold * 2, textSize.X), borderRectWidth);

                toolTipText_ = "BUILDING: " + name_.Replace("\"", "") + "\nTime Elapsed: " + GetTimeFormattedString(duration);
            }
            else
            {
                // Event is completed
                bIsCompleted = true;
                duration = timeFinished_ - timeStarted_;

                // Handle the zoom factor
                OriginalWidthInPixels = _zoomFactor * pix_per_second * (double)duration / (double)1000;

                // Try to compensate for the pixels lost with the spacing introduced between events
                AdjustedWidthInPixels = Math.Max(0.0f, OriginalWidthInPixels - pix_space_between_events);

                borderRectWidth = AdjustedWidthInPixels;
            }

            // Adjust the start time position if possible
            double desiredX = _zoomFactor * pix_per_second * (double)timeStarted_ / (double)1000;
            if (desiredX > X)
            {
                X = desiredX;
            }

            // Are we a Low LOD candidate?
            bool isInLowLOD = (AdjustedWidthInPixels <= pix_LOD_Threshold) && bIsCompleted;

            // Update the element size and figure out of anything changed since the last update
            Rect newBorderRect = new Rect(X, Y, borderRectWidth, pix_height);
            Rect newProgressRect = new Rect(X, Y, AdjustedWidthInPixels, pix_height);

            _isDirty = !bordersRect_.Equals(newBorderRect) || !progressRect_.Equals(newProgressRect) || isInLowLOD != _isInLowLOD;

            _isInLowLOD = isInLowLOD;
            bordersRect_ = newBorderRect;
            progressRect_ = newProgressRect;

            // Update our horizontal position on the time-line
            X = X + OriginalWidthInPixels;

            // Make sure we update our Canvas boundaries
            UpdateEventsCanvasMaxSize(X, Y);
        }

        public bool IsObjectVisibleInternal(Rect localRect)
        {
            Rect absoluteRect = new Rect(core_._x + localRect.X, core_._y + localRect.Y, localRect.Width, localRect.Height);

            return IsObjectVisible(absoluteRect);
        }

        public void OnRender(DrawingContext dc)
        {
            // if the current event is in lowLOD mode
            if (_isInLowLOD)
            {
                bool bStartNewLODBlock = false;

                if (core_.IsLODBlockActive())
                {
                    // calculate the distance (in pixels) between the end of the current LOD block and the start of the next block
                    double distance = bordersRect_.X - (core_._currentLODRect.X + core_._currentLODRect.Width);

                    if (distance > 5.0f)
                    {
                        // if the distance is above the threshold close the current LOD block and start a new one
                        VisualBrush brush = new VisualBrush();
                        brush.Visual = CPUCore._sLODImage;
                        brush.Stretch = Stretch.None;
                        brush.TileMode = TileMode.Tile;
                        brush.AlignmentY = AlignmentY.Top;
                        brush.AlignmentX = AlignmentX.Left;
                        brush.ViewportUnits = BrushMappingMode.Absolute;
                        brush.Viewport = new Rect(0, 0, 40, 6);

                        if (IsObjectVisibleInternal(core_._currentLODRect))
                        {
#if ENABLE_RENDERING_STATS
                                _StaticWindow._numShapesDrawn++;
#endif
                            dc.DrawRectangle(brush, new Pen(Brushes.Gray, 1), core_._currentLODRect);

                            core_.AddVisibleElement(core_._currentLODRect, string.Format("{0} events", core_._currentLODCount));
                        }

                        core_.CloseCurrentLODBlock();

                        // start a new LOD block
                        bStartNewLODBlock = true;
                    }
                    else
                    {
                        // if an LOD block is currently active then append the current event to it
                        core_.UpdateCurrentLODBlock(Math.Max(bordersRect_.X + bordersRect_.Width - core_._currentLODRect.X, 0.0f));
                    }
                }
                else
                {
                    bStartNewLODBlock = true;
                }

                if (bStartNewLODBlock)
                {
                    core_.StartNewLODBlock(new Rect(bordersRect_.X, bordersRect_.Y, 0.0f, bordersRect_.Height));
                }
            }
            else
            {
                if (core_.IsLODBlockActive())
                {
                    VisualBrush brush = new VisualBrush();
                    brush.Visual = CPUCore._sLODImage;
                    brush.Stretch = Stretch.None;
                    brush.TileMode = TileMode.Tile;
                    brush.AlignmentY = AlignmentY.Top;
                    brush.AlignmentX = AlignmentX.Left;
                    brush.ViewportUnits = BrushMappingMode.Absolute;
                    brush.Viewport = new Rect(0, 0, 40, 6);

                    if (IsObjectVisibleInternal(core_._currentLODRect))
                    {
#if ENABLE_RENDERING_STATS
                        _StaticWindow._numShapesDrawn++;
#endif
                        dc.DrawRectangle(brush, new Pen(Brushes.Gray, 1), core_._currentLODRect);

                        core_.AddVisibleElement(core_._currentLODRect, string.Format("{0} events", core_._currentLODCount));
                    }

                    core_.CloseCurrentLODBlock();
                }

                if (IsObjectVisibleInternal(bordersRect_))
                {

                    core_.AddVisibleElement(bordersRect_, toolTipText_, this);

#if ENABLE_RENDERING_STATS
                    _StaticWindow._numShapesDrawn++;
#endif
                    dc.DrawImage(brush_.ImageSource, progressRect_);

                    SolidColorBrush colorBrush = Brushes.Black;

                    if (state_ == BuildEventState.IN_PROGRESS)
                    {
                        // Draw an open rectangle
                        Point P0 = new Point(bordersRect_.X, bordersRect_.Y);
                        Point P1 = new Point(bordersRect_.X + bordersRect_.Width, bordersRect_.Y);
                        Point P2 = new Point(bordersRect_.X + bordersRect_.Width, bordersRect_.Y + bordersRect_.Height);
                        Point P3 = new Point(bordersRect_.X, bordersRect_.Y + bordersRect_.Height);

                        Pen pen = new Pen(Brushes.Gray, 1);

                        dc.DrawLine(pen, P0, P1);
                        dc.DrawLine(pen, P0, P3);
                        dc.DrawLine(pen, P3, P2);

                        if (isRacingJob_ && progressRect_.Width >= _cRacingIconWidth)
                        {
                            Rect racingIconRect = new Rect(progressRect_.X, progressRect_.Y, _cRacingIconWidth, progressRect_.Height);

                            dc.DrawImage(_sRacingIconBrush.ImageSource, racingIconRect);
                        }
                    }
                    else
                    {
                        switch (state_)
                        {
                            case BuildEventState.SUCCEEDED_PREPROCESSED:
                                //case BuildEventState.FAILED:
                                colorBrush = Brushes.PaleTurquoise;
                                break;
                        }

                        dc.DrawRectangle(new VisualBrush(), new Pen(Brushes.Gray, 1), bordersRect_);

                        if (isRacingJob_ && progressRect_.Width >= _cRacingIconWidth)
                        {
                            Rect racingIconRect = new Rect(progressRect_.X, progressRect_.Y, _cRacingIconWidth, progressRect_.Height);

                            if (wonRace_)
                            {
                                dc.DrawImage(_sRacingWinIconBrush.ImageSource, racingIconRect);
                            }
                            else
                            {
                                dc.DrawImage(_sRacingLostIconBrush.ImageSource, racingIconRect);
                            }
                        }
                    }

                    string textToDisplay = null;

                    double textWidthThreshold = _cMinTextLabelWidthThreshold + (isRacingJob_ ? _cRacingIconWidth : 0.0f);

                    if (bordersRect_.Width > textWidthThreshold)
                    {
                        textToDisplay = fileName_;
                    }
                    //else if (_bordersRect.Width > _cMinDotDotDotWidthThreshold)
                    //{
                    //    textToDisplay = "...";
                    //}

                    if (textToDisplay != null)
                    {
#if ENABLE_RENDERING_STATS
                        _StaticWindow._numTextElementsDrawn++;
#endif
                        double allowedTextWidth = Math.Max(0.0f, bordersRect_.Width - 2 * _cTextLabeloffset_X - (isRacingJob_ ? _cRacingIconWidth : 0.0f));

                        double textXOffset = bordersRect_.X + _cTextLabeloffset_X + (isRacingJob_ ? _cRacingIconWidth : 0.0f);

                        TextUtils.DrawText(dc, textToDisplay, textXOffset, bordersRect_.Y + _cTextLabeloffset_Y, allowedTextWidth, true, colorBrush);
                    }
                }
            }
        }
    }


}
