using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Threading;
using System.Collections.Generic;
using static VSFastBuildVSIX.CommandBuildProject;

namespace VSFastBuildVSIX.Commands
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildSolution)]
    internal sealed class CommandBuildSolution : BaseCommand<CommandBuildSolution>
    {
        private string commandText_ = string.Empty;

        private static async Task TraverseProjectItemsAsync(List<EnvDTE.Project> targets, ProjectItems projectItems)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            foreach (ProjectItem projectItem in projectItems)
            {
                if (ProjectTypes.WindowsCPlusPlus == projectItem.SubProject.Kind)
                {
                    EnvDTE.Project project = projectItem.Object as EnvDTE.Project;
                    targets.Add(project);
                    continue;
                }
                if (ProjectTypes.ProjectFolders == projectItem.SubProject.Kind)
                {
                    EnvDTE.Project project = projectItem.Object as EnvDTE.Project;
                    if(null == project)
                    {
                        continue;
                    }
                    await TraverseProjectItemsAsync(targets, project.ProjectItems);
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
                package.CancelBuildProcess();
                await CommandBuildProject.StopMonitorAsync(package);
                return;
            }
            commandText_ = Command.Text;
            Command.Text = "Cancel " + commandText_;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            EnvDTE80.DTE2 dte = package.DTE;
            if (null == dte.Solution)
            {
                CommandBuildProject.LeaveProcess(package, Command, commandText_);
                return;
            }
            EnvDTE.Solution solution = dte.Solution;
            SolutionBuild solutionBuild = solution.SolutionBuild;
            SolutionConfiguration2 solutionConfiguration = solutionBuild.ActiveConfiguration as SolutionConfiguration2;
            SolutionContexts solutionContexts = solutionConfiguration.SolutionContexts;
            List<EnvDTE.Project> targets = new List<EnvDTE.Project>();
            foreach (EnvDTE.Project project in solution.Projects)
            {
                if(ProjectTypes.WindowsCPlusPlus != project.Kind)
                {
                    continue;
                }

                foreach (SolutionContext context in solutionConfiguration.SolutionContexts)
                {
                    if (System.IO.Path.GetFileName(context.ProjectName) == System.IO.Path.GetFileName(project.FileName)
                        && context.ConfigurationName == solutionConfiguration.Name
                        && context.PlatformName == solutionConfiguration.PlatformName
                        && context.ShouldBuild)
                    {
                        targets.Add(project);
                    }
                }
            }

            targets.RemoveAll(
                x =>
                {
                    Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                    foreach (string exclude in CommandBuildProject.ExcludeProjects)
                    {
                        string uniqueName = x.UniqueName;
                        if (uniqueName.EndsWith(exclude))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            );

            if (targets.Count <= 0)
            {
                CommandBuildProject.LeaveProcess(package, Command, commandText_);
                return;
            }
            await BuildProjectsAsync(package, targets, true);
            LeaveProcess(package, Command, commandText_);
        }
    }
}
