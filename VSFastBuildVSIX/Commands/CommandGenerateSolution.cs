using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Threading;
using System.Collections.Generic;
using static VSFastBuildVSIX.CommandBuildProject;
using static VSFastBuildVSIX.CommandBuildSolution;

namespace VSFastBuildVSIX
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildGenerateSolution)]
    internal sealed class CommandGenerateSolution : BaseCommand<CommandGenerateSolution>
    {
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
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            EnvDTE80.DTE2 dte = package.DTE;
            if (null == dte.Solution)
            {
                return;
            }
            EnvDTE.Solution solution = dte.Solution;
            SolutionBuild solutionBuild = solution.SolutionBuild;
            SolutionConfiguration2 solutionConfiguration = solutionBuild.ActiveConfiguration as SolutionConfiguration2;
            SolutionContexts solutionContexts = solutionConfiguration.SolutionContexts;
            List<EnvDTE.Project> targets = new List<EnvDTE.Project>();
            foreach (EnvDTE.Project project in solution.Projects)
            {
                if (ProjectTypes.WindowsCPlusPlus == project.Kind)
                {
                    if (ShouldBuild(project, solutionConfiguration, solutionContexts))
                    {
                        targets.Add(project);
                    }
                    continue;
                }
                if (ProjectTypes.ProjectFolders == project.Kind)
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
                return;
            }
            await BuildProjectsAsync(package, targets, true, true);
        }
    }
}
