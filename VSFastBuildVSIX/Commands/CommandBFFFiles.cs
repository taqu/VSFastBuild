using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VSFastBuildVSIX.Options;
using static VSFastBuildVSIX.CommandBuildProject;
using static VSFastBuildVSIX.CommandBFFFiles;

namespace VSFastBuildVSIX
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildBFFFiles)]
    internal sealed class CommandBFFFiles : BaseDynamicCommand<CommandBFFFiles, BFFFile>
    {
        public class BFFFile
        {
            public string FilePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
        }

        private List<BFFFile> files_ = new List<BFFFile>();

        protected override void BeforeQueryStatus(OleMenuCommand menuItem, EventArgs e, BFFFile item)
        {
            menuItem.Text = item.FileName;
        }

        private void GatherBFFs(string root, string solutionDir)
        {
            foreach (string path in System.IO.Directory.GetFiles(root, "*.bff"))
            {
                if (files_.Any(x => x.FilePath == path))
                {
                    continue;
                }
                string name;
                if (path.StartsWith(solutionDir))
                {
                    name = path.Substring(solutionDir.Length + 1);
                }
                else
                {
                    name = System.IO.Path.GetFileName(path);
                }
                BFFFile bff = new BFFFile();
                bff.FilePath = path;
                bff.FileName = name;
                files_.Add(bff);
            }
        }

        private void TraverseProjectItems(EnvDTE.Project project, string solutionDir)
        {
            foreach(EnvDTE.ProjectItem projectItem in project.ProjectItems)
            {
                EnvDTE.Project subProject = projectItem.Object as EnvDTE.Project;
                if(null == subProject)
                {
                    continue;
                }
                if (ProjectTypes.ProjectFolders != subProject.Kind && !string.IsNullOrEmpty(subProject.FullName))
                {
                    string projectDir = System.IO.Path.GetDirectoryName(subProject.FullName);
                    GatherBFFs(projectDir, solutionDir);
                    continue;
                }
                if (ProjectTypes.ProjectFolders == subProject.Kind)
                {
                    TraverseProjectItems(subProject, solutionDir);
                    continue;
                }
            }
        }

        protected override IReadOnlyList<BFFFile> GetItems()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            files_.Clear();
            VSFastBuildVSIXPackage package;
            if(!VSFastBuildVSIXPackage.TryGetPackage(out package))
            {
                return files_;
            }
            EnvDTE80.DTE2 dte = package.DTE;
            if (null == dte.Solution || string.IsNullOrEmpty(dte.Solution.FullName))
            {
                return files_;
            }
            string solutionDir = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            GatherBFFs(solutionDir, solutionDir);
            foreach (EnvDTE.Project project in dte.Solution.Projects)
            {
                if(null == project)
                {
                    continue;
                }
                if (ProjectTypes.ProjectFolders != project.Kind && string.IsNullOrEmpty(project.FullName))
                {
                    string projectDir = System.IO.Path.GetDirectoryName(project.FullName);
                    GatherBFFs(projectDir, solutionDir);
                    continue;
                }
                if(ProjectTypes.ProjectFolders == project.Kind)
                {
                    TraverseProjectItems(project, solutionDir);
                    continue;
                }
            }
            return files_;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e, BFFFile bff)
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
            await Log.AddOutputPaneAsync(Log.PaneBuild);
            await Log.ClearPanelAsync(Log.PaneBuild);
            OptionsPage optionPage = VSFastBuildVSIXPackage.Options;
            bool openMonitor = optionPage.OpenMonitor;
            if (openMonitor)
                {
                    await CommandBuildProject.StartMonitorAsync(package, true);
                }
            try
            {
                Result result = new Result();
                await Log.OutputBuildAsync($"--- VSFastBuild begin running {bff.FileName}---");
                await RunProcessAsync(package, bff.FilePath);
                await Log.OutputBuildAsync($"--- VSFastBuild end running {bff.FileName}---");
            }
            catch (Exception ex)
            {
                await Log.OutputDebugAsync(ex.Message);
            }
            if (openMonitor)
            {
                await CommandBuildProject.StopMonitorAsync(package);
            }
            package.LeaveBuildProcess();
        }
    }
}
