using Microsoft.VisualStudio.Shell.Interop;

namespace VSFastBuildVSIX.Commands
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildMonitor)]
    internal sealed class CommandOpenMonitor : BaseCommand<CommandOpenMonitor>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ToolWindowMonitor.ShowAsync();
        }
    }
}
