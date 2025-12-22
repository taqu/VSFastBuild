using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSFastBuildVSIX.Commands
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildReload)]
    internal sealed class CommandReload : BaseCommand<CommandReload>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            VSFastBuildVSIXPackage package = null;
            if(!VSFastBuildVSIXPackage.TryGetPackage(out package))
            {
                return;
            }
            //OleMenuCommandService commandService = package.GetService<OleMenuCommandService,IMenuCommandService>() as OleMenuCommandService;
            //if(null != commandService)
            //{
            //    CommandID dynamicItemRootId = new CommandID(PackageGuids.VSFastBuildVSIX, PackageIds.CommandFBuildDynamicStart);
            //    //DynamicItemMenuCommand dynamicMenuCommand = new DynamicItemMenuCommand(
            //    //    dynamicItemRootId,
            //    //    IsValidDynamicItem,
            //    //    OnInvokedDynamicItem,
            //    //    OnBeforeQueryStatusDynamicItem);
            //    //commandService.AddCommand(dynamicMenuCommand);
            //}
        }

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
            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

            //EnvDTE80.DTE2 dte = package.DTE;
            //if (null == dte.Solution)
            //{
            //    return;
            //}
            //Log.AddOutputPaneAsync(Log.PaneDebug);
            //Log.OutputDebug("--- VSFastBuild begin cleaning ---");
            //string rootDirectory = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            //foreach(string path in System.IO.Directory.GetFiles(rootDirectory, "*.bff"))
            //{
            //    try
            //    {
            //        System.IO.File.Delete(path);
            //        Log.OutputDebug($"delete {path}");
            //    }
            //    catch { }
            //}
            //foreach (string path in System.IO.Directory.GetFiles(rootDirectory, "*.fdb"))
            //{
            //    try
            //    {
            //        System.IO.File.Delete(path);
            //        Log.OutputDebug($"delete {path}");
            //    }
            //    catch { }
            //}
            //Log.OutputDebug("--- VSFastBuild end cleaning ---");
        }
    }
}
