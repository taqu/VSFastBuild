using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace VSFastBuildVSIX.ToolWindows
{
    public class SystemPerformanceGraphsCanvas : Canvas
    {
        public Canvas parentCanvas_;
        private ToolWindowMonitorControl parent_;

        public int bigTimeUnit_ = 0;
        public int smallTimeUnit_ = 0;

        public float savedZoomFactor_ = 0.0f;
        public float savedBuildTime_ = 0.0f;

        public static Point _savedHorizontalViewport = new Point();

        public static bool _hasSelectedGraphPoint = false;

        public SystemPerformanceGraphsCanvas(Canvas parentCanvas, ToolWindowMonitorControl parent)
        {
            parentCanvas_ = parentCanvas;
            parent_ = parent;

            this.Width = parentCanvas_.Width;
            this.Height = cSystemGraphsHeight;

            parentCanvas_.Children.Add(this);

            SetVisibility((bool)parent_.SettingsGraphsCheckBox.IsChecked);
        }

        public struct Sample
        {
            public long _time = 0;
            public float _value = 0.0f;

            public Sample(long time, float value)
            {
                _time = time;
                _value = value;
            }
        }

        public class PerformanceCountersGroup
        {
            public enum PerformanceCounterType
            {
                RAM_Used = 0,
                CPU_Time,
                DISK_Read,
                DISK_Write,
                NET_Received,
                NET_Sent,
                Custom,
                Max
            }

            public class BasicPerformanceCounter
            {
                public static readonly SolidColorBrush[] Colors = new SolidColorBrush[] { Brushes.Blue,
                    Brushes.Red,
                    Brushes.Orange,
                    Brushes.Aquamarine,
                    Brushes.Black,
                    Brushes.Chocolate,
                    Brushes.Yellow,
                    Brushes.Green,
                    Brushes.Gray,
                    Brushes.Fuchsia,
                    Brushes.DarkGreen,
                    Brushes.MediumVioletRed,
                    Brushes.Cyan,
                    Brushes.Maroon,
                    Brushes.Salmon,
                    Brushes.DarkViolet,
                    Brushes.Brown,
                    Brushes.DarkGray
                };

                public static int colorsIndex_ = 0;

                protected ToolWindowMonitorControl parent_;

                // attributes
                public TreeViewItem _treeViewItem;
                public CheckBox _checkBox;
                public SolidColorBrush _colorBrush;

                public PerformanceCounterType _type;

                private PerformanceCounter _systemPerfCounter;

                protected bool _bInitialized = false;

                public List<Sample> samples_ = new List<Sample>();

                public string _description;
                public string _unitTag;

                public float _valueDivider = 1.0f;

                public BasicPerformanceCounter(PerformanceCounterType type, bool bEnabled, ToolWindowMonitorControl parent)
                {
                    _type = type;
                    _enabled = bEnabled;
                    parent_ = parent;
                }

                protected void InitializeTreeViewItem()
                {
                    _treeViewItem = new TreeViewItem();

                    StackPanel stackPanel = new StackPanel();
                    stackPanel.Orientation = Orientation.Horizontal;
                    _treeViewItem.Header = stackPanel;

                    _checkBox = new CheckBox();
                    _checkBox.IsChecked = _enabled;
                    stackPanel.Children.Add(_checkBox);

                    Canvas canvas = new Canvas() { Height = 10, Width = 10 };

                    if (colorsIndex_ < Colors.Length)
                    {
                        _colorBrush = Colors[colorsIndex_++];
                    }
                    else
                    {
                        _colorBrush = Brushes.Navy;
                    }

                    Line line = new Line() { Stroke = _colorBrush, StrokeThickness = 4, X1 = 0, Y1 = 5, X2 = 10, Y2 = 5 };
                    canvas.Children.Add(line);

                    stackPanel.Children.Add(canvas);

                    TextBlock textBlock = new TextBlock() { Text = _description };
                    stackPanel.Children.Add(textBlock);
                }

                public virtual void Initialize(System.Diagnostics.Process targetProcess, string description = "", string unitTag = "")
                {
                    _description = description;
                    _unitTag = unitTag;

                    switch (_type)
                    {
                        case PerformanceCounterType.CPU_Time:

                            _description = "Processor Time";
                            _unitTag = "% CPU";

                            if (targetProcess != null)
                            {
                                _systemPerfCounter = new PerformanceCounter("Process", "% Processor Time", targetProcess.ProcessName);
                            }
                            else
                            {
                                _systemPerfCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                            }

                            _bInitialized = true;
                            break;
                        case PerformanceCounterType.RAM_Used:

                            _description = "Memory Used";
                            _unitTag = "MB";

                            _valueDivider = 1024.0f * 1024.0f;

                            if (targetProcess != null)
                            {
                                _systemPerfCounter = new PerformanceCounter("Process", "Working Set", targetProcess.ProcessName);
                            }
                            else
                            {
                                _systemPerfCounter = new PerformanceCounter("Memory", "Committed Bytes", null);
                            }

                            _bInitialized = true;
                            break;
                        case PerformanceCounterType.DISK_Read:

                            _description = "Disk IO Read";
                            _unitTag = "MB/s";

                            _valueDivider = 1024.0f * 1024.0f;

                            if (targetProcess != null)
                            {
                                _systemPerfCounter = new PerformanceCounter("Process", "IO Read Bytes/sec", targetProcess.ProcessName);
                            }
                            else
                            {
                                _systemPerfCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                            }

                            _bInitialized = true;
                            break;
                        case PerformanceCounterType.DISK_Write:

                            _description = "Disk IO Write";
                            _unitTag = "MB/s";

                            _valueDivider = 1024.0f * 1024.0f;

                            if (targetProcess != null)
                            {
                                _systemPerfCounter = new PerformanceCounter("Process", "IO Write Bytes/sec", targetProcess.ProcessName);
                            }
                            else
                            {
                                _systemPerfCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
                            }

                            _bInitialized = true;
                            break;
                        case PerformanceCounterType.Custom:
                            _bInitialized = true;
                            break;

                        default:
                            Console.WriteLine("BasicPerformanceCounter was not able to handle PerformanceCounterType: " + (int)_type);
                            break;
                    }

                    InitializeTreeViewItem();
                }


                public bool HandleLogEvent(long eventTimeMS, float value)
                {
                    bool bSuccess = false;

                    if (_bInitialized)
                    {
                        Sample newSample = new Sample(eventTimeMS, value);

                        samples_.Add(newSample);

                        //Console.WriteLine("{0}: (time: {1} - value: {2} {3}", _description, newSample._time, newSample._value, _unitTag);

                        UpdateMaxValue(value);

                        bSuccess = true;
                    }

                    return bSuccess;
                }


                protected float _maxValue = 0.0f;

                protected void UpdateMaxValue(float newValue)
                {
                    if (newValue > _maxValue)
                    {
                        _maxValue = newValue;
                    }
                }

                public virtual bool CaptureNewSample()
                {
                    bool bSuccess = false;

                    if (_bInitialized)
                    {
                        Debug.Assert(_systemPerfCounter != null);

                        try
                        {
                            float newValue = (float)(Math.Round((double)_systemPerfCounter.NextValue(), 1)) / _valueDivider;

                            Sample newSample = new Sample(parent_.GetCurrentBuildTimeMS(), newValue);

                            samples_.Add(newSample);

                            //Console.WriteLine("{0}: (time: {1} - value: {2} {3}", _description, newSample._time, newSample._value, _unitTag);

                            UpdateMaxValue(newValue);

                            bSuccess = true;
                        }
                        catch (System.Exception ex)
                        {
                            Console.WriteLine("Exception during perf counters sampling" + ex.ToString());
                        }
                    }

                    return bSuccess;
                }

                // enabled state
                public bool _enabled = false;

                private int _lastSamplesCount = 0;

                private Point _lastMousePos = new Point(-1.0f, -1.0f);

                private GraphPoint _selectedGraphPoint = null;

                private bool IsGraphSelected(Point newMousePos)
                {
                    bool bSelected = false;

                    if (_points.Count >= 2)
                    {
                        for (int i = 0; i + 1 < _points.Count; ++i)
                        {
                            GraphPoint P1 = _points[i];
                            GraphPoint P2 = _points[i + 1];

                            GraphPoint selectedGraphPoint = IsMousePosWithinSegment(newMousePos, P1, P2);

                            if (selectedGraphPoint != null)
                            {
                                bSelected = true;
                                break;
                            }

                        }
                    }

                    return bSelected;
                }

                // returns if the state has changed and we need to update the geometry
                private bool UpdateIntenalState(Point newMousePos)
                {
                    bool bNeedsUpdateGeometry = false;

                    bool bNewEnabledState = (bool)_checkBox.IsChecked;
                    bool bNewSelectedState = false;

                    if (bNewEnabledState != _enabled || samples_.Count != _lastSamplesCount)
                    {
                        bNeedsUpdateGeometry = true;
                    }
                    else if (_enabled)
                    {
                        bool lastSelectedState = _selectedGraphPoint != null;

                        if (newMousePos != new Point(-1.0f, -1.0f))
                        {
                            bNewSelectedState = IsGraphSelected(newMousePos);
                        }

                        if (bNewSelectedState != lastSelectedState || (lastSelectedState && newMousePos != _lastMousePos))
                        {
                            bNeedsUpdateGeometry = true;
                        }
                    }

                    // refresh our internal state
                    _enabled = (bool)_checkBox.IsChecked;

                    _lastSamplesCount = samples_.Count;

                    _lastMousePos = newMousePos;

                    return bNeedsUpdateGeometry;
                }


                class GraphPoint
                {
                    public Point _coordinates;
                    public float _value;

                    public GraphPoint(Point coordinates, float value)
                    {
                        _coordinates = coordinates;
                        _value = value;
                    }
                }

                List<GraphPoint> _points = new List<GraphPoint>();

                private enum GraphMode
                {
                    AverageValues,
                    MaxValues,
                    MinValues
                }

                private void CalculateGraphPoints(GraphMode mode, double X, double Y, double zoomFactor, long timeStepMS)
                {
                    _points.Clear();

                    long totalTimeMS = 0;

                    long numSteps = parent_.GetCurrentBuildTimeMS() / ((long)timeStepMS);
                    long remainder = parent_.GetCurrentBuildTimeMS() % ((long)timeStepMS);

                    numSteps += remainder > 0 ? 2 : 1;

                    long timeLimitMS = numSteps * (long)timeStepMS;

                    float verticalPixPerUnit = cSystemGraphsHeight / Math.Max(_maxValue, 0.00001f);

                    int samplesIndex = 0;

                    while (totalTimeMS <= timeLimitMS && samplesIndex < samples_.Count)
                    {
                        int subStepSamplesCount = 0;
                        float subStepAvgValue = 0.0f;

                        while (samplesIndex < samples_.Count && samples_[samplesIndex]._time <= (totalTimeMS + timeStepMS))
                        {
                            subStepAvgValue += samples_[samplesIndex]._value;
                            samplesIndex++;
                            subStepSamplesCount++;

                            // validation code to make sure times are monotonic
                            //if (samplesIndex + 1 < samples_.Count)
                            //{
                            //    Debug.Assert(samples_[samplesIndex + 1]._time >= samples_[samplesIndex]._time);
                            //}
                        }

                        if (subStepSamplesCount > 0)
                        {
                            subStepAvgValue = subStepAvgValue / (float)subStepSamplesCount;

                            double x = X + zoomFactor * ToolWindowMonitorControl.PIX_PER_SECOND * (totalTimeMS + timeStepMS) / 1000.0f;
                            double y = cSystemGraphsHeight - (subStepAvgValue * verticalPixPerUnit);

                            y = Math.Max(0.0f, y);

                            _points.Add(new GraphPoint(new Point(x, y), subStepAvgValue));
                        }

                        totalTimeMS += timeStepMS;
                    }
                }

                private void DrawFilledSquare(StreamGeometryContext ctx, Point centerPoint, float size)
                {
                    Point p1 = new Point(centerPoint.X - size / 2.0f, centerPoint.Y - size / 2.0f);
                    Point p2 = new Point(centerPoint.X + size / 2.0f, centerPoint.Y - size / 2.0f);
                    Point p3 = new Point(centerPoint.X + size / 2.0f, centerPoint.Y + size / 2.0f);
                    Point p4 = new Point(centerPoint.X - size / 2.0f, centerPoint.Y + size / 2.0f);

                    ctx.BeginFigure(p1, true, false);
                    ctx.LineTo(p2, false, false);
                    ctx.LineTo(p3, false, false);
                    ctx.LineTo(p4, false, false);
                    ctx.LineTo(p1, false, false);
                }

                public bool UpdateGeometry(double X, double Y, Point mousePos, double zoomFactor, bool bForceUpdate, long timeStepMS)
                {
                    bool bUpdatedGeometry = false;

                    if (UpdateIntenalState(mousePos) || bForceUpdate)
                    {
                        bUpdatedGeometry = true;

                        if (_enabled && samples_.Count > 0)
                        {
                            CalculateGraphPoints(GraphMode.AverageValues, X, Y, zoomFactor, timeStepMS);

                            if (_points.Count >= 2)
                            {
                                // Clear old geometry
                                _geometry.Clear();

                                _selectedGraphPoint = null;

                                using (StreamGeometryContext ctx = _geometry.Open())
                                {
                                    for (int i = 0; i + 1 < _points.Count; ++i)
                                    {
                                        GraphPoint P1 = _points[i];
                                        GraphPoint P2 = _points[i + 1];
                                        if (IsPointVisible(P1._coordinates) || IsPointVisible(P2._coordinates))
                                        {
                                            GraphPoint selectedGraphPoint = IsMousePosWithinSegment(mousePos, P1, P2);

                                            if (selectedGraphPoint != null && SystemPerformanceGraphsCanvas._hasSelectedGraphPoint == false)
                                            {
                                                _selectedGraphPoint = selectedGraphPoint;

                                                DrawFilledSquare(ctx, _selectedGraphPoint._coordinates, 10);

                                                SystemPerformanceGraphsCanvas._hasSelectedGraphPoint = true;
                                            }

                                            ctx.BeginFigure(P1._coordinates, true /* is filled */, false /* is closed */);

                                            ctx.LineTo(P2._coordinates, true /* is stroked */, false /* is smooth join */);
                                        }
                                    }
                                }


                                if (_selectedGraphPoint != null)
                                {
                                    using (StreamGeometryContext ctx = _selectionLinesGeometry.Open())
                                    {
                                        ctx.BeginFigure(new Point(SystemPerformanceGraphsCanvas._savedHorizontalViewport.X, _selectedGraphPoint._coordinates.Y), false /* is filled */, false /* is closed */);

                                        ctx.LineTo(new Point(_selectedGraphPoint._coordinates.X, _selectedGraphPoint._coordinates.Y), true/* is stroked */, false /* is smooth join */);

                                        ctx.LineTo(new Point(_selectedGraphPoint._coordinates.X, SystemPerformanceGraphsCanvas.cSystemGraphsHeight), true /* is stroked */, false /* is smooth join */);
                                    }
                                }
                                else
                                {
                                    _selectionLinesGeometry.Clear();
                                }
                            }
                        }
                        else
                        {
                            // Clear old geometry
                            _geometry.Clear();
                        }
                    }

                    return bUpdatedGeometry;
                }


                private GraphPoint IsMousePosWithinSegment(Point mousePos, GraphPoint p1, GraphPoint p2)
                {
                    GraphPoint result = null;

                    if (mousePos.X >= p1._coordinates.X && mousePos.X <= p2._coordinates.X)
                    {
                        double aCoefCoords = (p1._coordinates.Y - p2._coordinates.Y) / (p1._coordinates.X - p2._coordinates.X);
                        double bCoefCoords = p2._coordinates.Y - (aCoefCoords * p2._coordinates.X);

                        double expectedY = aCoefCoords * mousePos.X + bCoefCoords;


                        double fError = Math.Abs(expectedY - mousePos.Y);

                        const double cCurveSelectionTolerance = 10.0f;

                        if (fError <= cCurveSelectionTolerance)
                        {
                            double aCoefValue = (p2._value - p1._value) / (p2._coordinates.X - p1._coordinates.X);

                            double value = aCoefValue * (mousePos.X - p1._coordinates.X) + p1._value;

                            result = new GraphPoint(new Point(mousePos.X, expectedY), (float)value);

                            //Console.WriteLine("Selected Curve ({0} - Error({1:00} pixels), time range: {2}s - {3}s, value: {4}{5}", _description, fError,p1._coordinates.X / FASTBuildMonitorControl.pix_per_second, p2._coordinates.X / FASTBuildMonitorControl.pix_per_second, value, _unitTag);
                        }
                    }

                    return result;
                }

                private bool IsPointVisible(Point p)
                {
                    return (p.X >= SystemPerformanceGraphsCanvas._savedHorizontalViewport.X && p.X <= SystemPerformanceGraphsCanvas._savedHorizontalViewport.Y);
                }

                StreamGeometry _geometry = new StreamGeometry();

                StreamGeometry _selectionLinesGeometry = new StreamGeometry();

                public void OnRender(DrawingContext dc)
                {
                    if (_enabled)
                    {
                        double thickness = _selectedGraphPoint != null ? 3.0f : 1.0f;
                        dc.DrawGeometry(_colorBrush, new Pen(_colorBrush, thickness), _geometry);

                        if (_selectedGraphPoint != null)
                        {
                            TextUtils.DrawText(dc, string.Format("{0}: {1:0.00}{2}", _description, _selectedGraphPoint._value, _unitTag), SystemPerformanceGraphsCanvas._savedHorizontalViewport.X, _selectedGraphPoint._coordinates.Y, 200, false, Brushes.Black);

                            dc.DrawGeometry(Brushes.Gray, new Pen(Brushes.Gray, 1), _selectionLinesGeometry);
                        }
                    }
                }
            }


            public class NetworkPerformanceCounter : BasicPerformanceCounter
            {
                public PerformanceCounter[] _counters;


                public NetworkPerformanceCounter(PerformanceCounterType type, bool bEnabled, ToolWindowMonitorControl parent) : base(type, bEnabled, parent)
                {
                }

                public override void Initialize(System.Diagnostics.Process targetProcess, string description = "", string unitTag = "")
                {
                    PerformanceCounterCategory performanceNetCounterCategory = new PerformanceCounterCategory("Network Interface");
                    string[] interfaces = null;

                    interfaces = performanceNetCounterCategory.GetInstanceNames();

                    int length = interfaces.Length;

                    if (length > 0)
                    {
                        _counters = new PerformanceCounter[length];

                        for (int i = 0; i < length; i++)
                        {
                            //Console.WriteLine("Name netInterface: {0}", performanceNetCounterCategory.GetInstanceNames()[i]);
                        }

                        _valueDivider = 1024.0f * 1024.0f;

                        switch (_type)
                        {
                            case PerformanceCounterType.NET_Received:

                                _description = "Network Traffic Received";
                                _unitTag = "MB/s";

                                for (int i = 0; i < length; i++)
                                {
                                    _counters[i] = new PerformanceCounter("Network Interface", "Bytes Received/sec", interfaces[i]);
                                }

                                _bInitialized = true;
                                break;
                            case PerformanceCounterType.NET_Sent:

                                _description = "Network Traffic Sent";
                                _unitTag = "MB/s";

                                for (int i = 0; i < length; i++)
                                {
                                    _counters[i] = new PerformanceCounter("Network Interface", "Bytes Sent/sec", interfaces[i]);
                                }

                                _bInitialized = true;
                                break;
                            default:
                                Console.WriteLine("NetworkPerformanceCounter was not able to handle PerformanceCounterType: " + (int)_type);
                                break;
                        }
                    }

                    InitializeTreeViewItem();
                }

                public override bool CaptureNewSample()
                {
                    bool bSuccess = false;

                    if (_bInitialized)
                    {
                        Debug.Assert(_counters != null);

                        float newValue = 0.0f;

                        foreach (var counter in _counters)
                        {
                            newValue += (float)(Math.Round((double)counter.NextValue(), 1));
                        }

                        newValue = (newValue / _valueDivider) / (float)_counters.Length;

                        Sample newSample = new Sample(parent_.GetCurrentBuildTimeMS(), newValue);

                        samples_.Add(newSample);

                        //Console.WriteLine("{0}: (time: {1} - value: {2} {3}", _description, newSample._time, newSample._value, _unitTag);

                        UpdateMaxValue(newValue);

                        bSuccess = true;
                    }

                    return bSuccess;
                }
            }

            private ToolWindowMonitorControl parent_;

            // attributes
            public List<BasicPerformanceCounter> _counters = new List<BasicPerformanceCounter>();

            public PerformanceCountersGroupsType _groupType = PerformanceCountersGroupsType.Custom;

            public string _groupName;

            public TreeViewItem _treeViewItem;

            public CheckBox _checkBox;

            private BasicPerformanceCounter CreatePerformanceCounter(PerformanceCounterType type)
            {
                BasicPerformanceCounter counter = null;

                switch (type)
                {
                    case PerformanceCounterType.CPU_Time:
                    case PerformanceCounterType.RAM_Used:
                    case PerformanceCounterType.DISK_Read:
                    case PerformanceCounterType.DISK_Write:
                    case PerformanceCounterType.Custom:
                        counter = new BasicPerformanceCounter(type, _enabled, parent_);
                        break;
                    case PerformanceCounterType.NET_Received:
                    case PerformanceCounterType.NET_Sent:
                        counter = new NetworkPerformanceCounter(type, _enabled, parent_);
                        break;
                }

                Debug.Assert(counter != null);

                _counters.Add(counter);

                return counter;
            }

            public PerformanceCountersGroup(ToolWindowMonitorControl parent, string groupName, bool bEnabled, TreeView parentTreeView, PerformanceCountersGroupsType groupType, PerformanceCounterType[] counterTypes, int targetProcessID)
            {
                Debug.Assert(!(groupType == PerformanceCountersGroupsType.Custom && counterTypes != null), "Pre-defined PerformanceCounterTypes cannot be used with a custom PerformanceCountersGroupsType");

                parent_ = parent;
                _groupType = groupType;

                _groupName = groupName;

                _enabled = bEnabled;

                System.Diagnostics.Process targetProcess = null;

                bool bCreateGroup = true;

                // first find our target process
                if (targetProcessID != 0)
                {
                    System.Diagnostics.Process[] processlist = System.Diagnostics.Process.GetProcesses();
                    foreach (System.Diagnostics.Process proc in processlist)
                    {
                        if (proc.Id == targetProcessID)
                        {
                            targetProcess = proc;
                            break;
                        }
                    }

                    if (targetProcess != null)
                    {
                        _groupName += string.Format("({0}.exe)", targetProcess.ProcessName);
                    }
                    else
                    {
                        bCreateGroup = false;

                        _enabled = false;
                    }
                }

                if (bCreateGroup)
                {
                    _treeViewItem = new TreeViewItem();

                    StackPanel stackPanel = new StackPanel();

                    _treeViewItem.Header = stackPanel;

                    stackPanel.Orientation = Orientation.Horizontal;

                    _checkBox = new CheckBox();

                    _checkBox.IsChecked = _enabled = bEnabled;

                    stackPanel.Children.Add(_checkBox);

                    TextBlock textBlock = new TextBlock() { Text = _groupName };

                    stackPanel.Children.Add(textBlock);

                    if (counterTypes != null)
                    {
                        for (int i = 0; i < counterTypes.Length; ++i)
                        {
                            BasicPerformanceCounter newCounter = CreatePerformanceCounter(counterTypes[i]);

                            newCounter.Initialize(targetProcess);

                            _treeViewItem.Items.Add(newCounter._treeViewItem);
                        }
                    }

                    parentTreeView.Items.Add(_treeViewItem);

                    _initialized = true;
                }
            }

            public bool CaptureNewSample()
            {
                bool bSuccess = false;

                for (int i = 0; i < _counters.Count; ++i)
                {
                    bSuccess = _counters[i].CaptureNewSample();

                    if (!bSuccess)
                    {
                        break;
                    }
                }

                return bSuccess;
            }


            public bool _enabled = false;

            public bool _initialized = false;

            public bool UpdateGeometry(double X, double Y, Point mousePos, double zoomFactor, bool bForceUpdate, long timeStepMS)
            {
                bool bUpdatedGeometry = false;

                if (_initialized)
                {
                    // first check if we have been enabled/disabled since the last frame
                    bUpdatedGeometry = (_enabled != (bool)_checkBox.IsChecked);

                    // reconcile our enabled state
                    _enabled = (bool)_checkBox.IsChecked;

                    if (_enabled)
                    {
                        foreach (var counter in _counters)
                        {
                            bUpdatedGeometry |= counter.UpdateGeometry(X, Y, mousePos, zoomFactor, bForceUpdate, timeStepMS);
                        }
                    }
                }

                return bUpdatedGeometry;
            }

            public void OnRender(DrawingContext dc)
            {
                if (_initialized && _enabled)
                {
                    foreach (var counter in _counters)
                    {
                        counter.OnRender(dc);
                    }
                }
            }

            public bool HandleLogEvent(long eventTimeMS, string counterName, string counterUnitTag, float value)
            {
                bool bSuccess = true;

                BasicPerformanceCounter perfCounter = null;

                foreach (var counter in _counters)
                {
                    if (counter._description == counterName)
                    {
                        perfCounter = counter;
                        break;
                    }
                }

                if (perfCounter == null)
                {
                    perfCounter = CreatePerformanceCounter(PerformanceCounterType.Custom);

                    perfCounter.Initialize(null, counterName, counterUnitTag);

                    _treeViewItem.Items.Add(perfCounter._treeViewItem);
                }

                perfCounter.HandleLogEvent(eventTimeMS, value);

                return bSuccess;
            }
        }
        // Performance Groups
        public enum PerformanceCountersGroupsType
        {
            System = 0,     // global counters for the local system (live group)
            TargetProcess,  // counters for a specific targeted process (live group)
            LiveGroups,     // This defines the number of live groups, all groups after this one will not be considered as live groups thus they won't be able to capture New Samples
            Custom,
            Max
        }

        public List<PerformanceCountersGroup> _performanceCountersGroups = new List<PerformanceCountersGroup>();

        public bool _bSessionOpen = false;

        public bool _liveSession = false;


        public PerformanceCountersGroup CreateNewGroup(string groupName, bool bEnabled, TreeView parentTreeView, PerformanceCountersGroupsType groupType = PerformanceCountersGroupsType.Custom, PerformanceCountersGroup.PerformanceCounterType[] counterTypes = null, int targetProcessID = 0)
        {
            Debug.Assert(!(groupType == PerformanceCountersGroupsType.Custom && counterTypes != null), "Pre-defined PerformanceCounterTypes cannot be used with a custom PerformanceCountersGroupsType");

            PerformanceCountersGroup newGroup = new PerformanceCountersGroup(parent_, groupName, bEnabled, parentTreeView, groupType, counterTypes, targetProcessID);

            _performanceCountersGroups.Add(newGroup);

            return newGroup;
        }

        public void OpenSession(bool bLiveSession, int targetProcessID)
        {
            // Reset our color selection counter;
            PerformanceCountersGroup.BasicPerformanceCounter.colorsIndex_ = 0;

            TreeView parentTreeView = parent_.GraphsSelectionTreeView;

            parentTreeView.Items.Clear();

            // Create the live session groups
            {
                CreateNewGroup("System", true, parentTreeView, PerformanceCountersGroupsType.System,
                    new PerformanceCountersGroup.PerformanceCounterType[] {
                                PerformanceCountersGroup.PerformanceCounterType.CPU_Time,
                                PerformanceCountersGroup.PerformanceCounterType.RAM_Used,
                                PerformanceCountersGroup.PerformanceCounterType.DISK_Read,
                                PerformanceCountersGroup.PerformanceCounterType.DISK_Write,
                                PerformanceCountersGroup.PerformanceCounterType.NET_Received,
                                PerformanceCountersGroup.PerformanceCounterType.NET_Sent});

                if (targetProcessID != 0)
                {
                    CreateNewGroup("Process", false, parentTreeView, PerformanceCountersGroupsType.TargetProcess,
                        new PerformanceCountersGroup.PerformanceCounterType[] {
                                PerformanceCountersGroup.PerformanceCounterType.CPU_Time,
                                PerformanceCountersGroup.PerformanceCounterType.RAM_Used,
                                PerformanceCountersGroup.PerformanceCounterType.DISK_Read,
                                PerformanceCountersGroup.PerformanceCounterType.DISK_Write},
                        targetProcessID);
                }
            }

            _liveSession = bLiveSession;

            _bSessionOpen = true;
        }


        public void CloseSession()
        {
            _lastSampleTimeMS = 0;

            _bSessionOpen = false;

            _liveSession = false;
        }

        public static long _lastSampleTimeMS = 0;
        private const long cSamplingPeriodMS = 1 * 1000;

        public void CaptureNewSamples()
        {
            if (_liveSession)
            {
                var currentTime = ToolWindowMonitorControl.GetCurrentSystemTimeMS();

                if ((currentTime - _lastSampleTimeMS) > cSamplingPeriodMS)
                {
                    foreach (var group in _performanceCountersGroups)
                    {
                        // Only capture samples for live groups. Other groups will have samples sent to them from the log events
                        if (group._groupType < PerformanceCountersGroupsType.LiveGroups)
                        {
                            group.CaptureNewSample();
                        }
                    }

                    _lastSampleTimeMS = currentTime;
                }
            }
        }


        public bool _visible = false;

        public const int cSystemGraphsHeight = 150;


        public void SetVisibility(bool visible)
        {
            TreeView parentTreeView = parent_.GraphsSelectionTreeView;

            if (visible)
            {
                parentTreeView.Visibility = Visibility = Visibility.Visible;
                this.Height = parentCanvas_.Height = cSystemGraphsHeight;
                this.Width = parentCanvas_.Width;
            }
            else
            {
                parentTreeView.Visibility = Visibility = Visibility.Collapsed;
                this.Height = parentCanvas_.Height = 0.0f;
                this.Width = parentCanvas_.Width = 0.0f;
            }

            _visible = visible;
        }


        protected override void OnRender(DrawingContext dc)
        {
            foreach (var group in _performanceCountersGroups)
            {
                group.OnRender(dc);
            }
        }

        private bool UpdateGeometry(double X, double Y, Point mousePos, double zoomFactor, bool bForceUpdate)
        {
            bool bUpdatedGeometry = false;

            foreach (var group in _performanceCountersGroups)
            {
                bUpdatedGeometry |= group.UpdateGeometry(X, Y, mousePos, zoomFactor, bForceUpdate, smallTimeUnit_ * 1000);
            }

            return bUpdatedGeometry;
        }

        // Returns if we must force a geometry update
        bool UpdateTimeUnits()
        {
            bool bNeedsToUpdateGeometry = false;

            const double pixChunkSize = 100.0f;

            double timePerChunk = pixChunkSize / (parent_.ZoomFactor * ToolWindowMonitorControl.PIX_PER_SECOND);

            int newBigTimeUnit = 0;
            int newSmallTimeUnit = 0;

            if (timePerChunk > 30.0f)
            {
                newBigTimeUnit = 60;
                newSmallTimeUnit = 10;
            }
            else if (timePerChunk > 10.0f)
            {
                newBigTimeUnit = 30;
                newSmallTimeUnit = 6;
            }
            else if (timePerChunk > 5.0f)
            {
                newBigTimeUnit = 10;
                newSmallTimeUnit = 2;
            }
            else
            {
                newBigTimeUnit = 5;
                newSmallTimeUnit = 1;
            }

            ScrollViewer eventScrollViewer = parent_.EventsScrollViewer;
            Point newHorizontalViewPort = new Point(eventScrollViewer.HorizontalOffset, eventScrollViewer.HorizontalOffset + eventScrollViewer.ViewportWidth);

            if (parent_.ZoomFactor != savedZoomFactor_ || parent_.GetCurrentBuildTimeMS() != savedBuildTime_ || newHorizontalViewPort != _savedHorizontalViewport)
            {
                bigTimeUnit_ = newBigTimeUnit;
                smallTimeUnit_ = newSmallTimeUnit;

                savedZoomFactor_ = parent_.ZoomFactor;

                savedBuildTime_ = parent_.GetCurrentBuildTimeMS();

                _savedHorizontalViewport = newHorizontalViewPort;

                bNeedsToUpdateGeometry = true;
            }

            return bNeedsToUpdateGeometry;
        }

        public void RenderUpdate(double X, double Y, double zoomFactor)
        {
            if (_bSessionOpen)
            {
                CaptureNewSamples();
            }

            if (_visible)
            {
                Width = parentCanvas_.Width;
                Height = parentCanvas_.Height;

                _hasSelectedGraphPoint = false;

                // Find out if the zoom has changed and if we need to force an update
                bool bForceGeometryUpdate = UpdateTimeUnits();

                Point mousePos = Mouse.GetPosition(this);

                bool bNeedToRedraw = UpdateGeometry(X, Y, mousePos, zoomFactor, bForceGeometryUpdate);

                if (bNeedToRedraw)
                {
                    InvalidateVisual();
                }
            }
        }

        public bool HandleLogEvent(long eventTimeMS, string groupName, string counterName, string counterUnitTag, float value)
        {
            PerformanceCountersGroup perfGroup = null;

            foreach (var g in _performanceCountersGroups)
            {
                if (g._groupName == groupName)
                {
                    perfGroup = g;
                    break;
                }
            }

            if (perfGroup == null)
            {
                TreeView parentTreeView = parent_.GraphsSelectionTreeView;

                perfGroup = CreateNewGroup(groupName, true, parentTreeView);
            }

            return perfGroup.HandleLogEvent(eventTimeMS, counterName, counterUnitTag, value);
        }


    }
}
