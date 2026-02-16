using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using static VSFastBuildVSIX.ToolWindowMonitorControl;

namespace VSFastBuildVSIX.ToolWindows
{
    public class CPUCore : Canvas
    {
        private class VisibleElement // Represents an element (event or LOD block) that has been successfully rendered in the last frame
        {
            public VisibleElement(Rect rect, string toolTipText, BuildEvent buildEvent)
            {
                rect_ = rect;
                toolTipText_ = toolTipText;
                buildEvent_ = buildEvent;
            }

            public bool HitTest(Point localMousePosition)
            {
                return rect_.Contains(localMousePosition);
            }

            public Rect rect_;  // boundaries of the element
            public string toolTipText_;
            public BuildEvent buildEvent_;
        }
        private ToolWindowMonitorControl parent_;
        public BuildHost buildHost_;
        public int coreIndex_ = 0;
        public BuildEvent activeEvent_ = null;
        public List<BuildEvent> completedEvents_ = new List<BuildEvent>();

        public double x_ = 0.0f;
        public double y_ = 0.0f;

        //WPF stuff
        public TextBlock textBlock_ = new TextBlock();
        public static Image sLODImage_ = null;
        public ToolTip toolTip_ = new ToolTip();
        public Line lineSeparator_ = new Line();

        //LOD handling
        public bool isLODBlockActive_ = false;
        public Rect currentLODRect_ = new Rect();
        public int currentLODCount_ = 0;

        private List<VisibleElement> visibleElements_ = new List<VisibleElement>();

        public CPUCore(ToolWindowMonitorControl parent, BuildHost host, int coreIndex)
        {
            parent_ = parent;
            buildHost_ = host;

            coreIndex_ = coreIndex;

            textBlock_.Text = string.Format("{0} (Core # {1})", buildHost_._name, coreIndex_);
            textBlock_.FontSize = ToolWindowMonitorControl.FontSize;

            parent_.CoresCanvas.Children.Add(textBlock_);

            parent_.EventsCanvas.Children.Add(this);


            this.Height = ToolWindowMonitorControl.PIX_HEIGHT;

            if (sLODImage_ == null)
            {

                sLODImage_ = new Image();
                sLODImage_.Source = GetBitmapImage(VSFastBuildVSIX.Resources.Images.lod);
            }

            this.ToolTip = toolTip_;
        }

        public void StartNewLODBlock(Rect rect)
        {
            // Make sure the previous block is closed 
            Debug.Assert(isLODBlockActive_ == false && currentLODCount_ == 0);

            currentLODRect_ = rect;
            currentLODCount_ = 1;
            isLODBlockActive_ = true;
        }

        public void CloseCurrentLODBlock()
        {
            // Make sure the current block has been started previously
            Debug.Assert(isLODBlockActive_ == true && currentLODCount_ > 0);

            currentLODCount_ = 0;
            isLODBlockActive_ = false;
        }

        public bool IsLODBlockActive()
        {
            return isLODBlockActive_;
        }

        public void UpdateCurrentLODBlock(double newWitdh)
        {
            // Make sure the current block has been started previously
            Debug.Assert(isLODBlockActive_ == true && currentLODCount_ > 0);

            currentLODCount_++;

            currentLODRect_.Width = newWitdh;
        }

        public void AddVisibleElement(Rect rect, string toolTipText, BuildEvent buildEvent = null)
        {
            visibleElements_.Add(new VisibleElement(rect, toolTipText, buildEvent));
        }

        public void ClearAllVisibleElements()
        {
            visibleElements_.Clear();
        }


        public bool ScheduleEvent(BuildEvent ev)
        {
            bool bOK = activeEvent_ == null;

            if (bOK)
            {
                activeEvent_ = ev;

                activeEvent_.Start(this);
            }

            return bOK;
        }

        public bool UnScheduleEvent(long timeCompleted, string eventName, BuildEventState jobResult, bool bIsLocalJob, string outputMessages, bool bForce = false)
        {
            bool bOK = (activeEvent_ != null && (activeEvent_.name_ == eventName || bForce));

            if (bOK)
            {
                if (!bForce && outputMessages.Length > 0)
                {
                    activeEvent_.SetOutputMessages(outputMessages);
                }

                activeEvent_.Stop(timeCompleted, jobResult, bIsLocalJob);

                completedEvents_.Add(activeEvent_);

                activeEvent_ = null;
            }

            return bOK;
        }

        protected override void OnRender(DrawingContext dc)
        {
            // First let's reset the list of visible elements since we will be recalculating it
            ClearAllVisibleElements();

            foreach (BuildEvent ev in completedEvents_)
            {
                ev.OnRender(dc);
            }

            // we need to close the currently active LOD block before rendering the active event
            if (IsLODBlockActive())
            {
                // compute the absolute Rect given the origin of the current core
                Rect absoluteRect = new Rect(x_ + currentLODRect_.X, y_ + currentLODRect_.Y, currentLODRect_.Width, currentLODRect_.Height);

                if (IsObjectVisible(absoluteRect, parent_.ViewPort))
                {
                    VisualBrush brush = new VisualBrush();
                    brush.Visual = sLODImage_;
                    brush.Stretch = Stretch.None;
                    brush.TileMode = TileMode.Tile;
                    brush.AlignmentY = AlignmentY.Top;
                    brush.AlignmentX = AlignmentX.Left;
                    brush.ViewportUnits = BrushMappingMode.Absolute;
                    brush.Viewport = new Rect(0, 0, 40, 20);

                    dc.DrawRectangle(brush, new Pen(Brushes.Black, 1), currentLODRect_);

                    AddVisibleElement(currentLODRect_, string.Format("{0} events", currentLODCount_));
                }

                CloseCurrentLODBlock();
            }

            if (activeEvent_ != null)
            {
                activeEvent_.OnRender(dc);
            }
        }

        public HitTest HitTest(Point localMousePosition)
        {
            foreach (VisibleElement element in visibleElements_)
            {
                if (element.HitTest(localMousePosition))
                {
                    return new HitTest(this.buildHost_, this, element.buildEvent_);
                }
            }

            return null;
        }

        public bool UpdateToolTip(Point localMousePosition)
        {
            foreach (VisibleElement element in visibleElements_)
            {
                if (element.HitTest(localMousePosition))
                {
                    toolTip_.Content = element.toolTipText_;

                    return true;
                }
            }

            return false;
        }

        public void RenderUpdate(ref double X, ref double Y)
        {
            // WPF Layout update
            Canvas.SetLeft(textBlock_, X);
            Canvas.SetTop(textBlock_, Y + 2);

            if (x_ != X)
            {
                Canvas.SetLeft(this, X);
                x_ = X;
            }

            if (y_ != Y)
            {
                Canvas.SetTop(this, Y);
                y_ = Y;
            }

            double relX = 0.0f;

            foreach (BuildEvent ev in completedEvents_)
            {
                ev.RenderUpdate(ref relX, 0);
            }

            if (activeEvent_ != null)
            {
                activeEvent_.RenderUpdate(ref relX, 0);
            }


            X = this.Width = X + relX + 40.0f;

            Y += 25;
        }
    }
}
