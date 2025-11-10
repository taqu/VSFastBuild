using Community.VisualStudio.Toolkit;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.VCProjectEngine;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSFastBuildCommon;
using VSFastBuildVSIX.Options;

namespace VSFastBuildVSIX
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildProject)]
    internal sealed class CommandBuildProject : BaseCommand<CommandBuildProject>
    {
        private string commandText_ = string.Empty;

        public static void LeaveProcess(VSFastBuildVSIXPackage package, OleMenuCommand command, string originalText)
        {
            System.Diagnostics.Debug.Assert(null != package);
            System.Diagnostics.Debug.Assert(null != command);
            command.Text = originalText;
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
                await StopMonitor(package);
                return;
            }
            commandText_ = Command.Text;
            Command.Text = "Cancel " + commandText_;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            EnvDTE80.DTE2 dte = package.DTE;
            SelectedItems selectedItems = dte.SelectedItems;
            if (null == selectedItems || selectedItems.Count <= 0)
            {
                LeaveProcess(package, Command, commandText_);
                return;
            }
            List<EnvDTE.Project> targets = new List<EnvDTE.Project>();
            foreach (SelectedItem item in selectedItems)
            {
                if (item.Project is EnvDTE.Project)
                {
                    targets.Add(item.Project);
                }
            }
            if (targets.Count <= 0)
            {
                Array ActiveProjects = dte.ActiveSolutionProjects as Array;
                foreach (EnvDTE.Project p in ActiveProjects)
                {
                    if (null != p)
                    {
                        targets.Add(p);
                    }
                }
                if (targets.Count <= 0)
                {
                    LeaveProcess(package, Command, commandText_);
                    return;
                }
            }
            await BuildAsync(package, targets, Command, commandText_);
        }

        public static void GatherProjectFiles(List<ProjectInSolution> projects, ProjectInSolution project, List<ProjectInSolution> projectsInSolution)
        {
            foreach (string p in project.Dependencies)
            {
                ProjectInSolution proj = projects.Find((x) => x.ProjectGuid == p);
                if(null == proj) {
                    proj = projectsInSolution.Find((x) => x.ProjectGuid == p);
                    GatherProjectFiles(projects, proj, projectsInSolution);
                }
            }
            if(null == projects.Find((x) => x.ProjectGuid == project.ProjectGuid)){
                projects.Add(project);
            }
        }

        public static async Task BuildAsync(VSFastBuildVSIXPackage package, List<EnvDTE.Project> targets, OleMenuCommand command, string originalText)
        {
            System.Diagnostics.Debug.Assert(null != package);
            System.Diagnostics.Debug.Assert(null != targets);
            System.Diagnostics.Debug.Assert(null != command);
            List<string> projectsToBuild = new List<string>();
            {
                EnvDTE80.DTE2 dte = package.DTE;
                List<ProjectInSolution> solutionProjects = SolutionFile.Parse(dte.Solution.FullName).ProjectsInOrder.Where(x => x.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat).ToList();
                List<ProjectInSolution> projects = new List<ProjectInSolution>();
                foreach (ProjectInSolution project in solutionProjects)
                {
                    EnvDTE.Project p = targets.Find((x) => x.FullName == project.AbsolutePath);
                    if (null != p)
                    {
                        projects.Add(project);
                    }
                }

                List<ProjectInSolution> lastProjects = new List<ProjectInSolution>();
                foreach (ProjectInSolution projectInSolution in projects)
                {
                    GatherProjectFiles(lastProjects, projectInSolution, solutionProjects);
                }
                lastProjects.Sort((x, y) =>
                {
                    if (x.Dependencies.Contains(y.ProjectGuid)) return 1;
                    if (y.Dependencies.Contains(x.ProjectGuid)) return -1;
                    return 0;
                });
                projectsToBuild = lastProjects.ConvertAll(el => el.AbsolutePath);
            }
            #if DEBUG
            foreach(string project in projectsToBuild)
            {
                Log.OutputBuildLine(string.Format("Project to build: {0}", project));
            }
            #endif

            OptionsPage options = VSFastBuildVSIXPackage.Options;
            if (null == options)
            {
                LeaveProcess(package, command, originalText);
                return;
            }
            EnvDTE.Solution solution = targets[0].DTE.Solution;
            SolutionBuild solutionBuild = solution.SolutionBuild;
            SolutionConfiguration2 solutionConfiguration = solutionBuild.ActiveConfiguration as SolutionConfiguration2;

            VSFastBuild vsFastBuild = new VSFastBuild()
            {
                Configuration = solutionConfiguration.Name,
                Platform = solutionConfiguration.PlatformName,
                FBuildPath = options.Path,
                FBuildArgs = options.Arguments,
                GenerateOnly = options.GenOnly,
                UnityBuild = options.Unity
            };
            vsFastBuild.RootDirectory = System.IO.Path.GetDirectoryName(solution.FullName);
            vsFastBuild.ProjectFiles.AddRange(projectsToBuild);
            if (!vsFastBuild.GenerateOnly)
            {
                foreach (System.Diagnostics.Process process in vsFastBuild.Build())
                {
                    try
                    {
                        await StartMonitor(package);
                        using (System.Diagnostics.Process proc = process)
                        {
                            proc.EnableRaisingEvents = true;
                            proc.OutputDataReceived += (sender, ev) =>
                            {
                                if (null != ev.Data)
                                {
                                    Log.OutputBuildLine(ev.Data);
                                }
                            };
                            proc.ErrorDataReceived += (sender, ev) =>
                            {
                                if (null != ev.Data)
                                {
                                    Log.OutputBuildLine(ev.Data);
                                }
                            };
                            proc.Start();
                            proc.BeginErrorReadLine();
                            proc.BeginOutputReadLine();
                            await proc.WaitForExitAsync(package.CancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        await Log.OutputBuildLineAsync(ex.Message);
                    }
                }
                await Log.OutputBuildLineAsync("Build completed");
            }
            await StopMonitor(package);
            LeaveProcess(package, command, originalText);
        }

        public static async Task StartMonitor(ToolkitPackage package)
        {
            ToolkitToolWindowPane pane = await package.FindToolWindowAsync(typeof(ToolWindowMonitor.Pane), 0, false, new CancellationToken()) as ToolkitToolWindowPane;
            if(null != pane)
            {
                ToolWindowMonitorControl toolWindowMonitorControl = pane.Content as ToolWindowMonitorControl;
                if(null != toolWindowMonitorControl)
                {
                    toolWindowMonitorControl.StartTimer();
                }
            }
        }

        public static async Task StopMonitor(ToolkitPackage package)
        {
            ToolkitToolWindowPane pane = await package.FindToolWindowAsync(typeof(ToolWindowMonitor.Pane), 0, false, new CancellationToken()) as ToolkitToolWindowPane;
            if(null != pane)
            {
                ToolWindowMonitorControl toolWindowMonitorControl = pane.Content as ToolWindowMonitorControl;
                if(null != toolWindowMonitorControl)
                {
                    toolWindowMonitorControl.StopTimer();
                }
            }
        }
#if false
        private static IVsHierarchy GetVsHierarchyForProject(EnvDTE.Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsHierarchy vsHierarchy = null;
            IVsSolution vsSolution = (IVsSolution)ServiceProvider.GlobalProvider.GetService(typeof(IVsSolution));
            vsSolution.GetProjectOfUniqueName(project.UniqueName, out vsHierarchy);
            return vsHierarchy;
        }
#endif
    }
}
