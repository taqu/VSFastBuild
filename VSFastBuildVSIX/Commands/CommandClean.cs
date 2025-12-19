using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace VSFastBuildVSIX.Commands
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildClean)]
    internal sealed class CommandClean : BaseCommand<CommandClean>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            VSFastBuildVSIXPackage package = await VSFastBuildVSIXPackage.GetPackageAsync();
            if (null == package)
            {
                return;
            }
            if (package.IsBuildProcessRunning())
            {
                return;
            }
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            EnvDTE80.DTE2 dte = package.DTE;
            if (null == dte.Solution)
            {
                return;
            }
            Log.AddOutputPaneAsync(Log.PaneDebug);
            Log.OutputDebug("--- VSFastBuild begin cleaning ---");
            string rootDirectory = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            foreach(string path in System.IO.Directory.GetFiles(rootDirectory, "*.bff"))
            {
                try
                {
                    System.IO.File.Delete(path);
                    Log.OutputDebug($"delete {path}");
                }
                catch { }
            }
            foreach (string path in System.IO.Directory.GetFiles(rootDirectory, "*.fdb"))
            {
                try
                {
                    System.IO.File.Delete(path);
                    Log.OutputDebug($"delete {path}");
                }
                catch { }
            }
            Log.OutputDebug("--- VSFastBuild end cleaning ---");
        }
    }
}

