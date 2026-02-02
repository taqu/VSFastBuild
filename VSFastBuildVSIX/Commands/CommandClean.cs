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
        private static async Task CleanAsync(string fullName)
        {
            string rootDirectory = System.IO.Path.GetDirectoryName(fullName);
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
        }

        private static async Task TraverseProjectItemsAsync(ProjectItems projectItems)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            foreach (ProjectItem projectItem in projectItems)
            {
                if (ProjectTypes.WindowsCPlusPlus == projectItem.SubProject.Kind)
                {
                    EnvDTE.Project project = projectItem.Object as EnvDTE.Project;
                    await CleanAsync(project.FileName);
                    continue;
                }
                if (ProjectTypes.ProjectFolders == projectItem.SubProject.Kind)
                {
                    EnvDTE.Project project = projectItem.Object as EnvDTE.Project;
                    if(null == project)
                    {
                        continue;
                    }
                    await TraverseProjectItemsAsync(project.ProjectItems);
                    continue;
                }
            }
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
            EnvDTE80.DTE2 dte = package.DTE;
            EnvDTE.Solution solution = dte.Solution;
            if (null == solution)
            {
                return;
            }
            await Log.AddOutputPaneAsync(Log.PaneBuild);
            await Log.OutputBuildLineAsync("--- VSFastBuild begin cleaning ---");

            // Clean solution directory
            await CleanAsync(System.IO.Path.GetDirectoryName(solution.FullName));

            //Traverse projects
            int Count = solution.Projects.Count;
            foreach (EnvDTE.Project project in solution.Projects)
            {
                if (ProjectTypes.WindowsCPlusPlus == project.Kind)
                {
                    await CleanAsync(System.IO.Path.GetDirectoryName(project.FullName));
                    continue;
                }
                if (ProjectTypes.ProjectFolders == project.Kind)
                {
                    await TraverseProjectItemsAsync(project.ProjectItems);
                    continue;
                }
            }
            await Log.OutputBuildLineAsync("--- VSFastBuild end cleaning ---");
        }
    }
}

