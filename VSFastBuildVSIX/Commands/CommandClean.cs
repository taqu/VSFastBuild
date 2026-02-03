using EnvDTE;
using System.Collections.Generic;
using static VSFastBuildVSIX.CommandBuildProject;

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
            foreach (string path in System.IO.Directory.GetFiles(rootDirectory, "fbuild_*.out"))
            {
                try
                {
                    System.IO.File.Delete(path);
                    await Log.OutputBuildLineAsync($"delete {path}");
                }
                catch { }
            }
        }

        private static void TraverseProjectItems(List<string> targets, ProjectItems projectItems)
        {
            foreach (ProjectItem projectItem in projectItems)
            {
                EnvDTE.Project project = projectItem.Object as EnvDTE.Project;
                    if(null ==project)
                    {
                        continue;
                }
                if (ProjectTypes.WindowsCPlusPlus == project.Kind)
                {
                    targets.Add(project.FileName);
                    continue;
                }
                if (ProjectTypes.ProjectFolders == project.Kind)
                {
                    TraverseProjectItems(targets, project.ProjectItems);
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
            await Log.ClearPanelAsync(Log.PaneBuild);
            await Log.OutputBuildLineAsync("--- VSFastBuild begin cleaning ---");

            RunSolutionClear(solution);
            List<string> targets = new List<string>();
            // Clean solution directory
            targets.Add(solution.FullName);

            //Traverse projects
            int Count = solution.Projects.Count;
            foreach (EnvDTE.Project project in solution.Projects)
            {
                if (ProjectTypes.WindowsCPlusPlus == project.Kind)
                {
                    targets.Add(project.FullName);
                    continue;
                }
                if (ProjectTypes.ProjectFolders == project.Kind)
                {
                    TraverseProjectItems(targets, project.ProjectItems);
                    continue;
                }
            }
            foreach(string path in targets)
            {
                await CleanAsync(path);
            }
            await Log.OutputBuildLineAsync("--- VSFastBuild end cleaning ---");
        }
    }
}

