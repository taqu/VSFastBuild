using System.Diagnostics;
using System.IO.Packaging;
using System.Windows.Media;
using static VSFastBuildVSIX.ToolWindowMonitorControl;

namespace VSFastBuildVSIX.ToolWindows
{
    public enum BuildEventState
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

    public class BuildEvent
    {
        public enum TAB
        {
            TimeLine = 0,
            Output,
            Settings,
        }

        // Constants
        private const int TextLavelOffset = 4;
        private const float MinTextLabelWidthThreshold = 50.0f;
        private const float MinDotDotDotWidthThreshold = 20.0f;
        private const float RacingIconWidth = 20.0f;

        private ToolWindowMonitorControl parent_;
        public CPUCore core_;

        public long timeStarted_;      // in ms
        public long timeFinished_;     // in ms

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
        public bool isInLowLOD_ = false;
        public bool isDirty_ = false;

        public BuildEvent(ToolWindowMonitorControl parent, string name, long timeStarted)
        {
            parent_ = parent;
            name_ = name;
            toolTipText_ = name_.Replace("\"", string.Empty);
            fileName_ = System.IO.Path.GetFileName(name_.Replace("\"", string.Empty));
            timeStarted_ = timeStarted;
            state_ = BuildEventState.IN_PROGRESS;
        }

        public void SetOutputMessages(string outputMessages)
        {
            char[] newLineSymbol = new char[1];
            newLineSymbol[0] = (char)12;
            outputMessages_ = outputMessages.Replace(new string(newLineSymbol), Environment.NewLine);
        }

        public void Start(CPUCore core)
        {
            core_ = core;

            brush_ = ToolImages.RunningBrush;

            toolTipText_ = "BUILDING: " + name_.Replace("\"", string.Empty);
        }

        public void Stop(long timeFinished, BuildEventState jobResult, bool bIsLocalJob)
        {
            timeFinished_ = timeFinished;

            double totalTimeSeconds = Math.Max((timeFinished_ - timeStarted_) / 1000.0, 0.0);

            Debug.Assert(0.0f<=totalTimeSeconds);

            toolTipText_ = string.Format("{0}", name_.Replace("\"", string.Empty)) + "\nStatus: ";

            state_ = jobResult;

            switch (state_)
            {
                case BuildEventState.SUCCEEDED_COMPLETED:
                    {
                        if (name_.Contains(".obj"))
                        {
                            brush_ = ToolImages.SuccessCodeBrush;
                        }
                        else
                        {
                            brush_ = ToolImages.SuccessNonCodeBrush;
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
                        brush_ = ToolImages.SuccessCachedBrush;
                        toolTipText_ += "Success(Cached)";
                    }
                    break;

                case BuildEventState.SUCCEEDED_PREPROCESSED:
                    {
                        brush_ = ToolImages.SuccessPreprocessedBrush;
                        toolTipText_ += "Success(Preprocess)";
                    }
                    break;

                case BuildEventState.FAILED:
                    {
                        brush_ = ToolImages.FailedBrush;
                        toolTipText_ += "Errors";
                    }
                    break;

                case BuildEventState.TIMEOUT:
                    {
                        brush_ = ToolImages.TimeoutBrush;
                        toolTipText_ += "Timeout";
                    }
                    break;

                default:
                    break;
            }

            toolTipText_ += "\nDuration: " + GetTimeFormattedString(timeFinished_ - timeStarted_);
            toolTipText_ += "\nStart Time: " + GetTimeFormattedString(timeStarted_);
            toolTipText_ += "\nEnd Time: " + GetTimeFormattedString(timeFinished_);

            if (!string.IsNullOrEmpty(outputMessages_))
            {
                // show only an extract of the errors so we don't flood the visual
                int textLength = Math.Min(outputMessages_.Length, 100);

                toolTipText_ += "\n" + outputMessages_.Substring(0, textLength);
                toolTipText_ += "... [Double-Click on the event to see more details]";

                outputMessages_ = string.Format("[Output {0}]: {1}", name_.Replace("\"", string.Empty), Environment.NewLine) + outputMessages_;
            }
            else
            {
                outputMessages_ = string.Format("{0} ", name_, state_);
            }
            parent_.AddOutputWindowFilterItem(this);
        }

        public bool JumpToEventLineInOutputBox()
        {
            bool bSuccess = false;

            int index = parent_.OutputTextBox.Text.IndexOf(name_.Replace("\"",@string.Empty));

            int lineNumber = parent_.OutputTextBox.Text.Substring(0, index).Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length;

            parent_.OutputTextBox.ScrollToLine(lineNumber - 1);

            int position = parent_.OutputTextBox.GetCharacterIndexFromLineIndex(lineNumber - 1);
            if (position >= 0)
            {
                int lineEnd = parent_.OutputTextBox.Text.IndexOf(Environment.NewLine, position);
                if (lineEnd < 0)
                {
                    lineEnd = parent_.OutputTextBox.Text.Length;
                }

                parent_.OutputTextBox.Select(position, lineEnd - position);
            }

            return bSuccess;
        }

        public bool HandleDoubleClickEvent()
        {
            bool bHandled = true;

            if (state_ != BuildEventState.IN_PROGRESS && outputMessages_ != null && outputMessages_.Length > 0)
            {
                // Switch to the Output Window Tab item
                parent_.MainTabControl.SelectedIndex = (int)TAB.Output;

                parent_.ChangeOutputWindowComboBoxSelection(this);
            }

            return bHandled;
        }

        public HitTest HitTest(System.Windows.Point localMousePosition)
        {
            HitTest result = null;

            if (bordersRect_.Contains(localMousePosition))
            {
                result = new HitTest(this.core_.buildHost_, this.core_, this);
            }

            return result;
        }

        public void RenderUpdate(ref double X, double Y)
        {
            long duration = 1;

            bool bIsCompleted = false;

            double OriginalWidthInPixels = 0.0;
            double AdjustedWidthInPixels = 0.0;

            double borderRectWidth = 0.0;

            if (state_ == BuildEventState.IN_PROGRESS)
            {
                // Event is in progress
                duration = Math.Max(1, parent_.GetCurrentBuildTimeMS(true) - timeStarted_);

                System.Windows.Point textSize = TextUtils.ComputeTextSize(fileName_);

                OriginalWidthInPixels = AdjustedWidthInPixels = parent_.ZoomFactor * ToolWindowMonitorControl.PIX_PER_SECOND * (double)duration / (double)1000;

                borderRectWidth = OriginalWidthInPixels + ToolWindowMonitorControl.PIX_PER_SECOND * ToolWindowMonitorControl.TIMESTEP_MS / 1000.0f;

                borderRectWidth = Math.Max(Math.Min(MinTextLabelWidthThreshold * 2, textSize.X), borderRectWidth);

                toolTipText_ = "BUILDING: " + name_.Replace("\"", "") + "\nTime Elapsed: " + GetTimeFormattedString(duration);
            }
            else
            {
                // Event is completed
                bIsCompleted = true;
                duration = Math.Max(1, timeFinished_ - timeStarted_);

                // Handle the zoom factor
                OriginalWidthInPixels = parent_.ZoomFactor * PIX_PER_SECOND * (double)duration / (double)1000;

                // Try to compensate for the pixels lost with the spacing introduced between events
                AdjustedWidthInPixels = Math.Max(0.0f, OriginalWidthInPixels - PIX_SPACE_BETWEEN_EVENTS);

                borderRectWidth = AdjustedWidthInPixels;
            }

            // Adjust the start time position if possible
            long timeStarted = Math.Max(0, timeStarted_ - parent_.BuildStartTimeMS);
            double desiredX = parent_.ZoomFactor * PIX_PER_SECOND * (double)timeStarted / 1000.0;
            if (desiredX > X)
            {
                X = desiredX;
            }

            // Are we a Low LOD candidate?
            bool isInLowLOD = (AdjustedWidthInPixels <= PIX_LOD_THRESHOLD) && bIsCompleted;

            // Update the element size and figure out of anything changed since the last update
            System.Windows.Rect newBorderRect = new System.Windows.Rect(X, Y, borderRectWidth, PIX_HEIGHT);
            System.Windows.Rect newProgressRect = new System.Windows.Rect(X, Y, AdjustedWidthInPixels, PIX_HEIGHT);

            isDirty_ = !bordersRect_.Equals(newBorderRect) || !progressRect_.Equals(newProgressRect) || isInLowLOD != isInLowLOD_;

            isInLowLOD_ = isInLowLOD;
            bordersRect_ = newBorderRect;
            progressRect_ = newProgressRect;

            // Update our horizontal position on the time-line
            X = X + OriginalWidthInPixels;

            // Make sure we update our Canvas boundaries
            parent_.UpdateEventsCanvasMaxSize((float)X, (float)Y);
        }

        public bool IsObjectVisibleInternal(System.Windows.Rect localRect)
        {
            System.Windows.Rect absoluteRect = new System.Windows.Rect(core_.x_ + localRect.X, core_.y_ + localRect.Y, localRect.Width, localRect.Height);

            return ToolWindowMonitorControl.IsObjectVisible(absoluteRect, parent_.ViewPort);
        }

        public void OnRender(DrawingContext dc)
        {
            // if the current event is in lowLOD mode
            if (isInLowLOD_)
            {
                bool bStartNewLODBlock = false;

                if (core_.IsLODBlockActive())
                {
                    // calculate the distance (in pixels) between the end of the current LOD block and the start of the next block
                    double distance = bordersRect_.X - (core_.currentLODRect_.X + core_.currentLODRect_.Width);

                    if (distance > 5.0f)
                    {
                        // if the distance is above the threshold close the current LOD block and start a new one
                        VisualBrush brush = new VisualBrush();
                        brush.Visual = CPUCore.sLODImage_;
                        brush.Stretch = Stretch.None;
                        brush.TileMode = TileMode.Tile;
                        brush.AlignmentY = AlignmentY.Top;
                        brush.AlignmentX = AlignmentX.Left;
                        brush.ViewportUnits = BrushMappingMode.Absolute;
                        brush.Viewport = new System.Windows.Rect(0, 0, 40, 6);

                        if (IsObjectVisibleInternal(core_.currentLODRect_))
                        {
#if ENABLE_RENDERING_STATS
                                parent_._numShapesDrawn++;
#endif
                            dc.DrawRectangle(brush, new Pen(Brushes.Gray, 1), core_.currentLODRect_);

                            core_.AddVisibleElement(core_.currentLODRect_, string.Format("{0} events", core_.currentLODCount_));
                        }

                        core_.CloseCurrentLODBlock();

                        // start a new LOD block
                        bStartNewLODBlock = true;
                    }
                    else
                    {
                        // if an LOD block is currently active then append the current event to it
                        core_.UpdateCurrentLODBlock(Math.Max(bordersRect_.X + bordersRect_.Width - core_.currentLODRect_.X, 0.0f));
                    }
                }
                else
                {
                    bStartNewLODBlock = true;
                }

                if (bStartNewLODBlock)
                {
                    core_.StartNewLODBlock(new System.Windows.Rect(bordersRect_.X, bordersRect_.Y, 0.0f, bordersRect_.Height));
                }
            }
            else
            {
                if (core_.IsLODBlockActive())
                {
                    VisualBrush brush = new VisualBrush();
                    brush.Visual = CPUCore.sLODImage_;
                    brush.Stretch = Stretch.None;
                    brush.TileMode = TileMode.Tile;
                    brush.AlignmentY = AlignmentY.Top;
                    brush.AlignmentX = AlignmentX.Left;
                    brush.ViewportUnits = BrushMappingMode.Absolute;
                    brush.Viewport = new System.Windows.Rect(0, 0, 40, 6);

                    if (IsObjectVisibleInternal(core_.currentLODRect_))
                    {
#if ENABLE_RENDERING_STATS
                        parent_._numShapesDrawn++;
#endif
                        dc.DrawRectangle(brush, new Pen(Brushes.Gray, 1), core_.currentLODRect_);

                        core_.AddVisibleElement(core_.currentLODRect_, string.Format("{0} events", core_.currentLODCount_));
                    }

                    core_.CloseCurrentLODBlock();
                }

                if (IsObjectVisibleInternal(bordersRect_))
                {

                    core_.AddVisibleElement(bordersRect_, toolTipText_, this);

#if ENABLE_RENDERING_STATS
                    parent_._numShapesDrawn++;
#endif
                    dc.DrawImage(brush_.ImageSource, progressRect_);

                    SolidColorBrush colorBrush = Brushes.Black;

                    if (state_ == BuildEventState.IN_PROGRESS)
                    {
                        // Draw an open rectangle
                        System.Windows.Point P0 = new System.Windows.Point(bordersRect_.X, bordersRect_.Y);
                        System.Windows.Point P1 = new System.Windows.Point(bordersRect_.X + bordersRect_.Width, bordersRect_.Y);
                        System.Windows.Point P2 = new System.Windows.Point(bordersRect_.X + bordersRect_.Width, bordersRect_.Y + bordersRect_.Height);
                        System.Windows.Point P3 = new System.Windows.Point(bordersRect_.X, bordersRect_.Y + bordersRect_.Height);

                        Pen pen = new Pen(Brushes.Gray, 1);

                        dc.DrawLine(pen, P0, P1);
                        dc.DrawLine(pen, P0, P3);
                        dc.DrawLine(pen, P3, P2);

                        if (isRacingJob_ && RacingIconWidth<=progressRect_.Width)
                        {
                            System.Windows.Rect racingIconRect = new System.Windows.Rect(progressRect_.X, progressRect_.Y, RacingIconWidth, progressRect_.Height);

                            dc.DrawImage(ToolImages.RacingIconBrush.ImageSource, racingIconRect);
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

                        if (isRacingJob_ && progressRect_.Width >= RacingIconWidth)
                        {
                            System.Windows.Rect racingIconRect = new System.Windows.Rect(progressRect_.X, progressRect_.Y, RacingIconWidth, progressRect_.Height);

                            if (wonRace_)
                            {
                                dc.DrawImage(ToolImages.RacingWinIconBrush.ImageSource, racingIconRect);
                            }
                            else
                            {
                                dc.DrawImage(ToolImages.RacingLostIconBrush.ImageSource, racingIconRect);
                            }
                        }
                    }

                    string textToDisplay = null;

                    double textWidthThreshold = MinTextLabelWidthThreshold + (isRacingJob_ ? RacingIconWidth : 0.0f);

                    if (bordersRect_.Width > textWidthThreshold)
                    {
                        textToDisplay = fileName_;
                    }

                    if (textToDisplay != null)
                    {
#if ENABLE_RENDERING_STATS
                        parent_._numTextElementsDrawn++;
#endif
                        double allowedTextWidth = Math.Max(0.0f, bordersRect_.Width - 2 * TextLabelOffset_X - (isRacingJob_ ? RacingIconWidth : 0.0f));

                        double textXOffset = bordersRect_.X + TextLabelOffset_X + (isRacingJob_ ? RacingIconWidth : 0.0f);

                        TextUtils.DrawText(dc, textToDisplay, textXOffset, bordersRect_.Y + TextLabelOffset_Y, allowedTextWidth, true, colorBrush);
                    }
                }
            }
        }
    }


}
