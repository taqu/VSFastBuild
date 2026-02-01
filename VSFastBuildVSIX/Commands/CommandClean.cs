using EnvDTE;
using EnvDTE80;
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
            await Log.AddOutputPaneAsync(Log.PaneBuild);
            await Log.OutputBuildLineAsync("--- VSFastBuild begin cleaning ---");

            // Clean solution directory
            string rootDirectory = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            foreach(string path in System.IO.Directory.GetFiles(rootDirectory, "fbuild_*.bff"))
            {
                try
                {
                    System.IO.File.Delete(path);
                    await Log.OutputBuildLineAsync($"delete {path}");
                }
                catch { }
            }
            foreach (string path in System.IO.Directory.GetFiles(rootDirectory, "fbuild_*.fdb"))
            {
                try
                {
                    System.IO.File.Delete(path);
                    await Log.OutputBuildLineAsync($"delete {path}");
                }
                catch { }
            }

            foreach (string path in System.IO.Directory.GetFiles(rootDirectory, "fbuild_*.bat"))
            {
                try
                {
                    System.IO.File.Delete(path);
                    await Log.OutputBuildLineAsync($"delete {path}");
                }
                catch { }
            }

            //Traverse projects
            foreach (EnvDTE.Project project in dte.Solution.Projects)
            {
                if (ProjectTypes.WindowsCPlusPlus != project.Kind)
                {
                    continue;
                }
                string projectDirectory = System.IO.Path.GetDirectoryName(project.FullName);
                foreach (string path in System.IO.Directory.GetFiles(projectDirectory, "fbuild_*.bff"))
                {
                    try
                    {
                        System.IO.File.Delete(path);
                        await Log.OutputBuildLineAsync($"delete {path}");
                    }
                    catch { }
                }
                foreach (string path in System.IO.Directory.GetFiles(projectDirectory, "fbuild_*.fdb"))
                {
                    try
                    {
                        System.IO.File.Delete(path);
                        await Log.OutputBuildLineAsync($"delete {path}");
                    }
                    catch { }
                }
                foreach (string path in System.IO.Directory.GetFiles(projectDirectory, "fbuild_*.bat"))
                {
                    try
                    {
                        System.IO.File.Delete(path);
                        await Log.OutputBuildLineAsync($"delete {path}");
                    }
                    catch { }
                }
            }
            await Log.OutputBuildLineAsync("--- VSFastBuild end cleaning ---");
        }
    }
}

