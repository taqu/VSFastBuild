using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.VCProjectEngine;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using VSFastBuildCommon;
using VSFastBuildVSIX.Options;

namespace VSFastBuildVSIX.Commands
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildSolution)]
    internal sealed class CommandBuildSolution : BaseCommand<CommandBuildSolution>
    {
        private string commandText_ = string.Empty;

        private void LeaveProcess(VSFastBuildVSIXPackage package)
        {
            System.Diagnostics.Debug.Assert(null != package);
            Command.Text = commandText_;
            package.LeaveBuildProcess();
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            VSFastBuildVSIXPackage package = await VSFastBuildVSIXPackage.GetPackageAsync();
            if (null == package)
            {
                return;
            }
            if (!package.EnterBuildProcess())
            {
                package.CancelBuildProcess();
                await CommandBuildProject.StopMonitor();
                return;
            }
            commandText_ = Command.Text;
            Command.Text = "Cancel " + commandText_;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            EnvDTE80.DTE2 dte = package.DTE;
            if (null == dte.Solution)
            {
                LeaveProcess(package);
                return;
            }
            EnvDTE.Solution solution = dte.Solution;
            SolutionBuild solutionBuild = solution.SolutionBuild;
            SolutionConfiguration2 solutionConfiguration = solutionBuild.ActiveConfiguration as SolutionConfiguration2;
            List<EnvDTE.Project> targets = new List<EnvDTE.Project>();
            foreach (EnvDTE.Project project in dte.Solution.Projects)
            {
                foreach (SolutionContext context in solutionConfiguration.SolutionContexts)
                {
                    if(System.IO.Path.GetFileName(context.ProjectName) == System.IO.Path.GetFileName(project.FileName)
                        && context.ConfigurationName == solutionConfiguration.Name
                        && context.PlatformName == solutionConfiguration.PlatformName
                        && context.ShouldBuild)
                    {
                        Log.OutputBuildLine(string.Format("Project {0} will be built.", project.Name));
                        targets.Add(project);
                    }
                }
            }

            if (targets.Count <= 0)
            {
                CommandBuildProject.LeaveProcess(package, Command, commandText_);
                return;
            }

            await CommandBuildProject.BuildAsync(package, targets, Command, commandText_);
        }
    }
}
