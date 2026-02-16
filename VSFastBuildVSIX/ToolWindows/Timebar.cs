using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;

namespace VSFastBuildVSIX.ToolWindows
{
    public class Timebar : Canvas
    {
        private Canvas parentCanvas_;
        private ToolWindowMonitorControl parent_;
        private List<TextTag> textTags_ = new List<TextTag>();

        private StreamGeometry geometry_ = new StreamGeometry();

        private int bigTimeUnit_ = 0;
        private int smallTimeUnit_ = 0;

        private float savedZoomFactor_ = 0.0f;
        private float savedBuildTime_ = 0.0f;
        private System.Windows.Point savedTimebarViewPort_ = new System.Windows.Point();

        public Timebar(Canvas parentCanvas, ToolWindowMonitorControl parent)
        {
            parentCanvas_ = parentCanvas;
            parent_ = parent;

            this.Width = parentCanvas_.Width;
            this.Height = parentCanvas_.Height;

            parentCanvas_.Children.Add(this);
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, 1), geometry_);

            textTags_.ForEach(tag => TextUtils.DrawText(dc, tag.text_, tag.x_, tag.y_, 100, false, Brushes.Black));
        }

        void UpdateGeometry(double X, double Y, double zoomFactor)
        {
            // Clear old geometry
            geometry_.Clear();

            textTags_.Clear();

            // Open a StreamGeometryContext that can be used to describe this StreamGeometry 
            // object's contents.
            using (StreamGeometryContext ctx = geometry_.Open())
            {
                long totalTimeMS = 0;

                long numSteps = parent_.GetCurrentBuildTimeMS() / (bigTimeUnit_ * 1000);
                long remainder = parent_.GetCurrentBuildTimeMS() % (bigTimeUnit_ * 1000);

                numSteps += remainder > 0 ? 2 : 1;

                long timeLimitMS = numSteps * bigTimeUnit_ * 1000;

                while (totalTimeMS <= timeLimitMS)
                {
                    bool bDrawBigMarker = totalTimeMS % (bigTimeUnit_ * 1000) == 0;

                    double x = X + zoomFactor * ToolWindowMonitorControl.PIX_PER_SECOND * totalTimeMS / 1000.0f;

                    // TODO: activate culling optimization
                    //if (x >= _savedTimebarViewPort.X && x <= _savedTimebarViewPort.Y)
                    {
                        double height = bDrawBigMarker ? 3.0f : 1.5f;

                        ctx.BeginFigure(new System.Windows.Point(x, Y), true /* is filled */, false /* is closed */);

                        // Draw a line to the next specified point.
                        ctx.LineTo(new System.Windows.Point(x, Y + height), true /* is stroked */, false /* is smooth join */);

                        if (bDrawBigMarker)
                        {
                            string formattedText = ToolWindowMonitorControl.GetTimeFormattedString(totalTimeMS);

                            System.Windows.Point textSize = TextUtils.ComputeTextSize(formattedText);

                            double horizontalCorrection = textSize.X / 2.0f;

                            TextTag newTag = new TextTag(formattedText, (float)(x - horizontalCorrection), (float)(Y + height + 2));

                            textTags_.Add(newTag);
                        }
                    }

                    totalTimeMS += smallTimeUnit_ * 1000;
                }
            }
        }

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
            System.Windows.Point newTimebarViewPort = new System.Windows.Point(eventScrollViewer.HorizontalOffset, eventScrollViewer.HorizontalOffset + eventScrollViewer.ViewportWidth);

            if (parent_.ZoomFactor != savedZoomFactor_ || parent_.GetCurrentBuildTimeMS() != savedBuildTime_ || newTimebarViewPort != savedTimebarViewPort_)
            {
                bigTimeUnit_ = newBigTimeUnit;
                smallTimeUnit_ = newSmallTimeUnit;

                savedZoomFactor_ = parent_.ZoomFactor;

                savedBuildTime_ = parent_.GetCurrentBuildTimeMS();

                savedTimebarViewPort_ = newTimebarViewPort;

                this.InvalidateVisual();

                bNeedsToUpdateGeometry = true;
            }

            return bNeedsToUpdateGeometry;
        }

        public void RenderUpdate(double X, double Y, double zoomFactor)
        {
            if (UpdateTimeUnits())
            {
                this.InvalidateVisual();

                UpdateGeometry(X, Y, zoomFactor);
            }
        }

        private struct TextTag
        {
            public TextTag(string text, float x, float y)
            {
                text_ = text;
                x_ = x;
                y_ = y;
            }

            public string text_;
            public float x_;
            public float y_;
        }
            }
}
