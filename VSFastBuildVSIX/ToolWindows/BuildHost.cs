using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using static VSFastBuildVSIX.ToolWindowMonitorControl;

namespace VSFastBuildVSIX.ToolWindows
{
        internal class BuildHost
        {
        public const string LocalostName = "local";

            public string _name;
            public List<CPUCore> _cores = new List<CPUCore>();
            public bool bLocalHost = false;

            //WPF stuff
            public Line _lineSeparator = new Line();

            public BuildHost(string name, ToolWindowMonitorControl parent)
            {
                _name = name;

                bLocalHost = name.Contains(LocalostName);

                // Add line separator
                _StaticWindow.CoresCanvas.Children.Add(_lineSeparator);

                _lineSeparator.Stroke = new SolidColorBrush(Colors.LightGray);
                _lineSeparator.StrokeThickness = 1;
                DoubleCollection dashes = new DoubleCollection();
                dashes.Add(2);
                dashes.Add(2);
                _lineSeparator.StrokeDashArray = dashes;

                _lineSeparator.X1 = 10;
                _lineSeparator.X2 = 300;
            }

            public void OnStartEvent(BuildEvent newEvent)
            {
                bool bAssigned = false;
                for (int i = 0; i < _cores.Count; ++i)
                {
                    if (_cores[i].ScheduleEvent(newEvent))
                    {
                        //Console.WriteLine("START {0} (Core {1}) [{2}]", _name, i, newEvent._name);
                        bAssigned = true;
                        break;
                    }
                }

                // we discovered a new core
                if (!bAssigned)
                {
                    CPUCore core = new CPUCore(this, _cores.Count);

                    core.ScheduleEvent(newEvent);

                    //Console.WriteLine("START {0} (Core {1}) [{2}]", _name, _cores.Count, newEvent._name);

                    _cores.Add(core);
                }
            }

            public void OnCompleteEvent(Int64 timeCompleted, string eventName, string hostName, BuildEventState jobResult, string outputMessages)
            {
				bool bLocalJob = (hostName == _name);	// determine if we own the job that's about to be completed

                for (int i = 0; i < _cores.Count; ++i)
                {
                    if (_cores[i].UnScheduleEvent(timeCompleted, eventName, jobResult, bLocalJob, outputMessages))
                    {
                        break;
                    }
                }
            }

			public bool FindAndFlagRacingEvents(string eventName)
			{
				bool bFoundRacingEvents = false;

				foreach (CPUCore core in _cores)
				{
					if (core._activeEvent != null && core._activeEvent._name == eventName) 
					{
						core._activeEvent.isRacingJob_ = true;

						bFoundRacingEvents = true;

						break;
					}
				}

				return bFoundRacingEvents;
			}

            public HitTestResult HitTest(Point mousePosition)
            {
                HitTestResult result = null;

                foreach (CPUCore core in _cores)
                {
                    double x = Canvas.GetLeft(core);
                    double y = Canvas.GetTop(core);

                    Rect rect = new Rect(x, y, core.Width, core.Height);

                    if (rect.Contains(mousePosition))
                    {
                        Point localMousePosition = new Point(mousePosition.X - x, mousePosition.Y - y);
                        result = core.HitTest(localMousePosition);

                        break;
                    }
                }

                return result;
            }

            public bool UpdateToolTip(Point mousePosition)
            {
                foreach (CPUCore core in _cores)
                {
                    double x = Canvas.GetLeft(core);
                    double y = Canvas.GetTop(core);

                    Rect rect = new Rect(x, y, core.Width, core.Height);

                    if (rect.Contains(mousePosition))
                    {
                        Point localMousePosition = new Point(mousePosition.X - x, mousePosition.Y - y);
                        return core.UpdateToolTip(localMousePosition);
                    }
                }

                return false;

            }

            public void RenderUpdate(double X, ref double Y)
            {
                double maxX = 0.0f;

                //update all cores
                foreach (CPUCore core in _cores)
                {
                    double localX = X;

                    core.RenderUpdate(ref localX, ref Y);

                    maxX = Math.Max(maxX, localX);
                }

                //adjust the dynamic line separator
                _lineSeparator.Y1 = _lineSeparator.Y2 = Y + 10;

                Y += 20;

                UpdateEventsCanvasMaxSize(X, Y);
            }
        }
}
