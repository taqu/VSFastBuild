using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Threading;
using System.Collections.Generic;
using System.Runtime.Remoting.Contexts;
using static VSFastBuildVSIX.CommandBuildProject;

namespace VSFastBuildVSIX
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildSolution)]
    internal sealed class CommandBuildSolution : BaseCommand<CommandBuildSolution>
    {
        private string commandText_ = string.Empty;

        public static void TraverseProjectItems(List<EnvDTE.Project> targets, EnvDTE.ProjectItems projectItems, SolutionConfiguration2 solutionConfiguration, SolutionContexts solutionContexts)
        {
            foreach (EnvDTE.ProjectItem projectItem in projectItems)
            {
                EnvDTE.Project project = projectItem.Object as EnvDTE.Project;
                if(null == project)
                {
                    continue;
                }
                
                if(SupportedProject(project))
                {
                    if(ShouldBuild(project, solutionConfiguration, solutionContexts))
                    {
                        targets.Add(project);
                    }
                    continue;
                }
                if(ProjectTypes.ProjectFolders == project.Kind)
                {
                    TraverseProjectItems(targets, project.ProjectItems, solutionConfiguration, solutionContexts);
                    continue;
                }
            }
        }

        public static bool ShouldBuild(EnvDTE.Project project, SolutionConfiguration2 solutionConfiguration, SolutionContexts solutionContexts)
        {
            foreach (SolutionContext context in solutionContexts)
            {
                if (System.IO.Path.GetFileName(context.ProjectName) == System.IO.Path.GetFileName(project.FileName)
                            && context.ConfigurationName == solutionConfiguration.Name
                            && context.PlatformName == solutionConfiguration.PlatformName
                            && context.ShouldBuild)
                {
                    return true;
                }
            }
            return false;
        }

        protected override void BeforeQueryStatus(EventArgs e)
        {
            OptionsPage options = VSFastBuildVSIXPackage.Options;
            if(null == options)
            {
                return;
            }
            Command.Enabled = options.EnableGeneration;
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
                if(SupportedProject(project))
                {
                    if(ShouldBuild(project, solutionConfiguration, solutionContexts))
                    {
                        targets.Add(project);
                    }
                    continue;
                }
                if(ProjectTypes.ProjectFolders == project.Kind)
                {
                    TraverseProjectItems(targets, project.ProjectItems, solutionConfiguration, solutionContexts);
                    continue;
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
