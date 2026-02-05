using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using static VSFastBuildVSIX.CommandBFFFiles;
using static VSFastBuildVSIX.CommandBuildProject;

namespace VSFastBuildVSIX
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildBFFFiles)]
    internal sealed class CommandBFFFiles : BaseCommand<CommandBFFFiles>
    {
        public enum Type
        {
            Menu,
            Button,
        }
        public class BFFFile
        {
            public string FilePath { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
        }

        private List<BFFFile> files_ = new List<BFFFile>();

        private void MenuCommandBeforeQueryStatus(object sender, EventArgs e)
        {

        }
        protected override Task InitializeCompletedAsync()
        {
            return base.InitializeCompletedAsync();
        }

        protected override void BeforeQueryStatus(EventArgs e)
        {
            VSFastBuildVSIXPackage package;
            if (!VSFastBuildVSIXPackage.TryGetPackage(out package))
            {
                return;
            }
            IServiceProvider serviceProvider = package;
            OleMenuCommandService commandService = serviceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null)
            {
                return;
            }
            CommandID commandID = new CommandID(Command.CommandID.Guid, Command.CommandID.ID + 1);
            if (null != commandService.FindCommand(commandID))
            {
                return;
            }
            OleMenuCommand menuCommand = new OleMenuCommand(MenuCommandBeforeQueryStatus, commandID);
            menuCommand.Text = "MenuCommand_Test";
            commandService.AddCommand(menuCommand);
        }
        private void GatherBFFs(string root, string solutionDir)
        {
            foreach (string path in System.IO.Directory.GetFiles(root, "*.bff"))
            {
                if (files_.Any(x => x.FilePath == path))
                {
                    continue;
                }
                string relativePath;
                string name;
                if (path.StartsWith(solutionDir))
                {
                    relativePath = path.Substring(solutionDir.Length + 1);
                    name = System.IO.Path.GetFileName(path);
                }
                else
                {
                    relativePath = string.Empty;
                    name = System.IO.Path.GetFileName(path);
                }
                BFFFile bff = new BFFFile();
                bff.FilePath = path;
                bff.RelativePath = relativePath;
                bff.FileName = name;
                files_.Add(bff);
            }
        }

        private void TraverseProjectItems(EnvDTE.Project project, string solutionDir)
        {
            foreach (EnvDTE.ProjectItem projectItem in project.ProjectItems)
            {
                EnvDTE.Project subProject = projectItem.Object as EnvDTE.Project;
                if (null == subProject)
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

#if false
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
#endif

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
#if false
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
#endif
        }
    }
}
