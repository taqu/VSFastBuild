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
                await CommandBuildProject.StopMonitor(package);
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
                return string.Compare(x0.Name, x1.Name);
            });
            string rootDirectory = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            string bffname = string.Format("fbuild_{0}_{1}.bff", solutionConfiguration.Name, solutionConfiguration.PlatformName);
            string bffpath = System.IO.Path.Combine(rootDirectory, bffname);
            bool result = await CommandBuildProject.BuildForSolutionAsync(package, targets, bffpath);
            if (result)
            {
                OptionsPage optionPage = VSFastBuildVSIXPackage.Options;
                string fbuildPath = optionPage.Path;
                string fbuldArgs = optionPage.Arguments;
                bool openMonitor = optionPage.OpenMonitor;
                System.Diagnostics.Process process = CommandBuildProject.CreateProcessFromBffFile(bffpath, rootDirectory, fbuildPath, fbuldArgs);
                try
                {
                    ToolWindowMonitorControl.TruncateLogFile();
                    if (process.Start())
                    {
                        if (openMonitor) {
                            await CommandBuildProject.StartMonitor(package, true);
                        }
                        await process.WaitForExitAsync(package.CancellationToken);
                    }
                }
                catch(Exception ex)
                {
                    Log.OutputDebugLine(ex.Message);
                }
                finally
                {
                    await CommandBuildProject.StopMonitor(package);
                }
            }
            CommandBuildProject.LeaveProcess(package, Command, commandText_);
        }
    }
}
