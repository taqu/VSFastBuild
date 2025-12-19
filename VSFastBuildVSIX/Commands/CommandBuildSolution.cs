using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.VCProjectEngine;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using VSFastBuildCommon;
using VSFastBuildVSIX.Options;
using static VSFastBuildVSIX.CommandBuildProject;

namespace VSFastBuildVSIX.Commands
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildSolution)]
    internal sealed class CommandBuildSolution : BaseCommand<CommandBuildSolution>
    {
        private string commandText_ = string.Empty;

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
            List<EnvDTE.Project> targets = new List<EnvDTE.Project>();
            foreach (EnvDTE.Project project in dte.Solution.Projects)
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
            targets.Sort((x0, x1) =>
            {
                Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                return string.Compare(x0.Name, x1.Name);
            });
            string rootDirectory = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            string bffname = string.Format("fbuild_{0}_{1}.bff", solutionConfiguration.Name, solutionConfiguration.PlatformName);
            string bffpath = System.IO.Path.Combine(rootDirectory, bffname);
            Result result;
            try
            {
                await Log.OutputBuildAsync($"--- VSFastBuild begin building {bffname}---");
                result = await CommandBuildProject.BuildForSolutionAsync(package, targets, bffpath);

                if (!VSFastBuildVSIXPackage.Options.GenOnly)
                {
                    await Log.OutputBuildAsync($"--- VSFastBuild begin running {bffname}---");
                    await RunProcessAsync(result, package, bffpath);
                }
                await Log.OutputBuildAsync($"--- VSFastBuild end {bffname} ---");
            }
            catch (Exception ex)
            {
                await Log.OutputDebugLineAsync(ex.Message);
            }
            CommandBuildProject.LeaveProcess(package, Command, commandText_);
        }
    }
}
