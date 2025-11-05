using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using static VSFastBuildVSIX.ToolWindowMonitorControl;

namespace VSFastBuildVSIX.ToolWindows
{
    internal class CPUCore : Canvas
    {
        private class VisibleElement // Represents an element (event or LOD block) that has been successfully rendered in the last frame
            {
                public VisibleElement(Rect rect, string toolTipText, BuildEvent buildEvent)
                {
                    _rect = rect;
                    _toolTipText = toolTipText;
                    _buildEvent = buildEvent;
                }

                public bool HitTest(Point localMousePosition)
                {
                    return _rect.Contains(localMousePosition);
                }

                public Rect _rect;  // boundaries of the element
                public string _toolTipText;
                public BuildEvent _buildEvent;
            }

            public BuildHost _parent;
            public int _coreIndex = 0;
            public BuildEvent _activeEvent = null;
            public List<BuildEvent> _completedEvents = new List<BuildEvent>();

            public double _x = 0.0f;
            public double _y = 0.0f;

            //WPF stuff
            public TextBlock _textBlock = new TextBlock();
            public static Image _sLODImage = null;
            public ToolTip _toolTip = new ToolTip();
            public Line _lineSeparator = new Line();

            //LOD handling
            public bool _isLODBlockActive = false;
            public Rect _currentLODRect = new Rect();
            public int _currentLODCount = 0;


            public void StartNewLODBlock(Rect rect)
            {
                // Make sure the previous block is closed 
                Debug.Assert(_isLODBlockActive == false && _currentLODCount == 0);

                _currentLODRect = rect;
                _currentLODCount = 1;
                _isLODBlockActive = true;
            }

            public void CloseCurrentLODBlock()
            {
                // Make sure the current block has been started previously
                Debug.Assert(_isLODBlockActive == true && _currentLODCount > 0);

                _currentLODCount = 0;
                _isLODBlockActive = false;
            }

            public bool IsLODBlockActive()
            {
                return _isLODBlockActive;
            }

            public void UpdateCurrentLODBlock(double newWitdh)
            {
                // Make sure the current block has been started previously
                Debug.Assert(_isLODBlockActive == true && _currentLODCount > 0);

                _currentLODCount++;

                _currentLODRect.Width = newWitdh;
            }

            
            private List<VisibleElement> _visibleElements = new List<VisibleElement>();

        public CPUCore(BuildHost parent, int coreIndex)
            {
                _parent = parent;

                _coreIndex = coreIndex;

                _textBlock.Text = string.Format("{0} (Core # {1})", parent._name, _coreIndex);

                _StaticWindow.CoresCanvas.Children.Add(_textBlock);

                _StaticWindow.EventsCanvas.Children.Add(this);


                this.Height = pix_height;

                if (_sLODImage == null)
                {

                    _sLODImage = new Image();
                    _sLODImage.Source = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.LODBlock);
                }

                this.ToolTip = _toolTip;
            }

            public void AddVisibleElement(Rect rect, string toolTipText, BuildEvent buildEvent = null)
            {
                _visibleElements.Add(new VisibleElement(rect, toolTipText, buildEvent));
            }

            public void ClearAllVisibleElements()
            {
                _visibleElements.Clear();
            }

            
            public bool ScheduleEvent(BuildEvent ev)
            {
                bool bOK = _activeEvent == null;

                if (bOK)
                {
                    _activeEvent = ev;

                    _activeEvent.Start(this);
                }

                return bOK;
            }

            public bool UnScheduleEvent(Int64 timeCompleted, string eventName, BuildEventState jobResult, bool bIsLocalJob, string outputMessages, bool bForce = false)
            {
                bool bOK = (_activeEvent != null && (_activeEvent._name == eventName || bForce));

                if (bOK)
                {
                    if (!bForce && outputMessages.Length > 0)
                    {
                        _activeEvent.SetOutputMessages(outputMessages);
                    }

                    _activeEvent.Stop(timeCompleted, jobResult, bIsLocalJob);

                    _completedEvents.Add(_activeEvent);

                    _activeEvent = null;
                }

                return bOK;
            }

            protected override void OnRender(DrawingContext dc)
            {
                // First let's reset the list of visible elements since we will be recalculating it
                ClearAllVisibleElements();

                foreach (BuildEvent ev in _completedEvents)
                {
                    ev.OnRender(dc);
                }

                // we need to close the currently active LOD block before rendering the active event
                if (IsLODBlockActive())
                {
                    // compute the absolute Rect given the origin of the current core
                    Rect absoluteRect = new Rect(_x + _currentLODRect.X, _y + _currentLODRect.Y, _currentLODRect.Width, _currentLODRect.Height);

                    if (IsObjectVisible(absoluteRect))
                    {
                        VisualBrush brush = new VisualBrush();
                        brush.Visual = _sLODImage;
                        brush.Stretch = Stretch.None;
                        brush.TileMode = TileMode.Tile;
                        brush.AlignmentY = AlignmentY.Top;
                        brush.AlignmentX = AlignmentX.Left;
                        brush.ViewportUnits = BrushMappingMode.Absolute;
                        brush.Viewport = new Rect(0, 0, 40, 20);

                        dc.DrawRectangle(brush, new Pen(Brushes.Black, 1), _currentLODRect);

                        AddVisibleElement(_currentLODRect, string.Format("{0} events", _currentLODCount));
                    }

                    CloseCurrentLODBlock();
                }

                if (_activeEvent != null)
                {
                    _activeEvent.OnRender(dc);
                }
            }

            public HitTestResult HitTest(Point localMousePosition)
            {
                foreach (VisibleElement element in _visibleElements)
                {
                    if (element.HitTest(localMousePosition))
                    {
                        return new HitTestResult(this._parent, this, element._buildEvent);
                    }
                }

                return null;
            }

            public bool UpdateToolTip(Point localMousePosition)
            {
                foreach (VisibleElement element in _visibleElements)
                {
                    if (element.HitTest(localMousePosition))
                    {
                        _toolTip.Content = element._toolTipText;

                        return true;
                    }
                }

                return false;
            }

            public void RenderUpdate(ref double X, ref double Y)
            {
                // WPF Layout update
                Canvas.SetLeft(_textBlock, X);
                Canvas.SetTop(_textBlock, Y + 2);

                if (_x != X)
                {
                    Canvas.SetLeft(this, X);
                    _x = X;
                }

                if (_y != Y)
                {
                    Canvas.SetTop(this, Y);
                    _y = Y;
                }

                double relX = 0.0f;

                foreach (BuildEvent ev in _completedEvents)
                {
                    ev.RenderUpdate(ref relX, 0);
                }

                if (_activeEvent != null)
                {
                    _activeEvent.RenderUpdate(ref relX, 0);
                }


                X = this.Width = X + relX + 40.0f;

                Y += 25;
            }
        }
}
