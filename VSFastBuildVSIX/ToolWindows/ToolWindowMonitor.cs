using Microsoft.VisualStudio.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VSFastBuildVSIX
{
    public class ToolWindowMonitor : BaseToolWindow<ToolWindowMonitor>
    {
        public override string GetTitle(int toolWindowId) => "ToolWindowMonitor";

        public override Type PaneType => typeof(Pane);

        public override Task<FrameworkElement> CreateAsync(int toolWindowId, CancellationToken cancellationToken)
        {
            return Task.FromResult<FrameworkElement>(new ToolWindowMonitorControl());
        }

        [Guid("093e0d46-7d2c-4dd9-8d38-ff91e2ef7b07")]
        internal class Pane : ToolkitToolWindowPane
        {
            public Pane()
            {
                Caption = "FASTBuild Monitor";
                BitmapImageMoniker = KnownMonikers.ToolWindow;
            }
        }
    }
}
