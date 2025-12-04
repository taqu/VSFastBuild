using Community.VisualStudio.Toolkit;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Utilities;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.VCProjectEngine;
using SharpCompress;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using VSFastBuildCommon;
using VSFastBuildVSIX.Options;

namespace VSFastBuildVSIX
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildProject)]
    internal sealed class CommandBuildProject : BaseCommand<CommandBuildProject>
    {
        private string commandText_ = string.Empty;

        public static int GetNumberOfCores()
        {
            int logicalCores = System.Environment.ProcessorCount;
            return Math.Max(1, logicalCores / 2);
        }

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
                LeaveProcess(package, Command, commandText_);
                return;
#if false
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
#endif
            }
            SolutionBuild2 solutionBuild = package.DTE.Solution.SolutionBuild as SolutionBuild2;
            SolutionConfiguration2 solutionConfiguration = solutionBuild.ActiveConfiguration as SolutionConfiguration2;
            string rootDirectory = System.IO.Path.GetDirectoryName(package.DTE.Solution.FullName);
            foreach (EnvDTE.Project p in targets)
            {
                string bffname = string.Format("fbuild_{0}_{1}_{2}.bff", p.Name, solutionConfiguration.Name, solutionConfiguration.PlatformName);
                await BuildForSolutionAsync(package, targets, rootDirectory, bffname, Command, commandText_);
            }
            LeaveProcess(package, Command, commandText_);
        }

        public static void GatherProjectFiles(List<ProjectInSolution> projects, ProjectInSolution project, List<ProjectInSolution> projectsInSolution)
        {
            foreach (string p in project.Dependencies)
            {
                ProjectInSolution proj = projects.Find((x) => x.ProjectGuid == p);
                if (null == proj)
                {
                    proj = projectsInSolution.Find((x) => x.ProjectGuid == p);
                    GatherProjectFiles(projects, proj, projectsInSolution);
                }
            }
            if (null == projects.Find((x) => x.ProjectGuid == project.ProjectGuid))
            {
                projects.Add(project);
            }
        }

        private struct VSFastProjectInSolution
        {
            public EnvDTE.Project project_;
            public ProjectInSolution projectInSolution_;
        }

#if false
        public static async Task BuildAsync(VSFastBuildVSIXPackage package, List<EnvDTE.Project> targets, OleMenuCommand command, string originalText)
        {
            System.Diagnostics.Debug.Assert(null != package);
            System.Diagnostics.Debug.Assert(null != targets);
            System.Diagnostics.Debug.Assert(null != command);
            List<string> projectsToBuild = new List<string>();
            {
                EnvDTE80.DTE2 dte = package.DTE;
                List<ProjectInSolution> solutionProjects = SolutionFile.Parse(dte.Solution.FullName).ProjectsInOrder.Where(x => x.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat).ToList();
                List<VSFastProjectInSolution> projects = new List<VSFastProjectInSolution>();
                foreach (ProjectInSolution project in solutionProjects)
                {
                    EnvDTE.Project p = targets.Find((x) => x.FullName == project.AbsolutePath);
                    if (null != p)
                    {
                        projects.Add(new VSFastProjectInSolution(){project_ = p, projectInSolution_ = project});
                    }
                }
                if(projects.Count <= 0){
                    return;
                }
                List<ProjectInSolution> lastProjects = new List<ProjectInSolution>();
                foreach (VSFastProjectInSolution project in projects)
                {
                    GatherProjectFiles(lastProjects, project.projectInSolution_, solutionProjects);
                }
                lastProjects.RemoveAll(
                    x =>
                    {
                        return x.AbsolutePath.EndsWith("ZERO_CHECK.vcxproj")
                        || x.AbsolutePath.EndsWith("INSTALL.vcxproj")
                        || x.AbsolutePath.EndsWith("ALL_BUILD.vcxproj")
                        || x.AbsolutePath.EndsWith("RUN_TESTS.vcxproj");
                    }
                 );
#if false
                List<ProjectInSolution> lastProjects2 = new List<ProjectInSolution>(lastProjects.Count);
                for(int i=0; i<lastProjects.Count; ++i)
                {
                    lastProjects2.Add(lastProjects[i]);
                }
                lastProjects.Sort((x, y) =>
                {
                    if (x.Dependencies.Contains(y.ProjectGuid)) return 1;
                    if (y.Dependencies.Contains(x.ProjectGuid)) return -1;
                    return 0;
                });
                TopologicalSort(lastProjects2);
#if DEBUG
                for(int i=0; i<lastProjects.Count; ++i)
                {
                    ProjectInSolution projectInSolution = lastProjects[i];
                    ProjectInSolution projectInSolution2 = lastProjects2[i];
                    Log.OutputBuildLine(string.Format("Project to build: {0} {1}", projectInSolution.AbsolutePath, projectInSolution2.AbsolutePath));
                }
#endif
#else
                TopologicalSort(lastProjects);
#if DEBUG
                for (int i = 0; i < lastProjects.Count; ++i)
                {
                    ProjectInSolution projectInSolution = lastProjects[i];
                    Log.OutputBuildLine(string.Format("Project to build: {0}", projectInSolution.AbsolutePath));
                }
#endif
#endif
                projectsToBuild = lastProjects.ConvertAll(el => el.AbsolutePath);
            }

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
                await Build(package, vsFastBuild);
            }
            await StopMonitor(package);
            LeaveProcess(package, command, originalText);
        }

        public static async Task Build(VSFastBuildVSIXPackage package, VSFastBuild vsFastBuild)
        {
            int numCores = GetNumberOfCores();
            await ResetMonitor(package);
            foreach (System.Diagnostics.Process process in vsFastBuild.Build())
            {
                try
                {
                    await StartMonitor(package);
                    using (System.Diagnostics.Process proc = process)
                    {
#if DEBUG
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
#endif
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
#endif
        public static async Task StartMonitor(ToolkitPackage package)
        {
            ToolkitToolWindowPane pane = await package.FindToolWindowAsync(typeof(ToolWindowMonitor.Pane), 0, false, new CancellationToken()) as ToolkitToolWindowPane;
            if (null != pane)
            {
                ToolWindowMonitorControl toolWindowMonitorControl = pane.Content as ToolWindowMonitorControl;
                if (null != toolWindowMonitorControl)
                {
                    toolWindowMonitorControl.StartTimer();
                }
            }
        }

        public static async Task StopMonitor(ToolkitPackage package)
        {
            ToolkitToolWindowPane pane = await package.FindToolWindowAsync(typeof(ToolWindowMonitor.Pane), 0, false, new CancellationToken()) as ToolkitToolWindowPane;
            if (null != pane)
            {
                ToolWindowMonitorControl toolWindowMonitorControl = pane.Content as ToolWindowMonitorControl;
                if (null != toolWindowMonitorControl)
                {
                    toolWindowMonitorControl.StopTimer();
                }
            }
        }

        public static async Task ResetMonitor(ToolkitPackage package)
        {
            ToolkitToolWindowPane pane = await package.FindToolWindowAsync(typeof(ToolWindowMonitor.Pane), 0, false, new CancellationToken()) as ToolkitToolWindowPane;
            if (null != pane)
            {
                ToolWindowMonitorControl toolWindowMonitorControl = pane.Content as ToolWindowMonitorControl;
                if (null != toolWindowMonitorControl)
                {
                    toolWindowMonitorControl.Reset();
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

#if false
        private class SortProject
        {
            public ProjectInSolution project_;
            public bool visited_;
        };

        private static void TopologicalSort(List<ProjectInSolution> projects)
        {
            List<SortProject> targets = new List<SortProject>();
            foreach (ProjectInSolution project in projects)
            {
                targets.Add(new SortProject() { project_ = project, visited_ = false });
            }
            projects.Clear();
            foreach (SortProject target in targets)
            {
                TopologicalVisit(projects, targets, target);
            }
            projects.Reverse();
        }

        private static void TopologicalVisit(List<ProjectInSolution> sorted, List<SortProject> projects, SortProject project)
        {
            if (!project.visited_)
            {
                project.visited_ = true;
                foreach (string dependency in project.project_.Dependencies)
                {
                    SortProject proj = projects.Find((x) => x.project_.ProjectGuid == dependency);
                    if (null != proj)
                    {
                        TopologicalVisit(sorted, projects, proj);
                    }
                }
                sorted.Insert(0, project.project_);
            }
        }
#endif

        private struct VSFastProject
        {
            public EnvDTE.Project project_;
            public List<EnvDTE.Project> dependencies_;
        }

        private class SortProject
        {
            public VSFastProject project_;
            public bool visited_;
        };

        private static void TopologicalSort(List<VSFastProject> projects)
        {
            List<SortProject> sortProjects = new List<SortProject>(projects.Count);
            foreach (VSFastProject project in projects)
            {
                sortProjects.Add(new SortProject() { project_ = project, visited_ = false });
            }
            projects.Clear();
            foreach (SortProject target in sortProjects)
            {
                TopologicalVisit(projects, sortProjects, target);
            }
            projects.Reverse();
        }

        private static void TopologicalVisit(List<VSFastProject> sorted, List<SortProject> projects, SortProject project)
        {
            if (!project.visited_)
            {
                project.visited_ = true;
                foreach (EnvDTE.Project dependency in project.project_.dependencies_)
                {
                    SortProject proj = projects.Find((x) => x.project_.project_ == dependency);
                    if (null != proj)
                    {
                        TopologicalVisit(sorted, projects, proj);
                    }
                }
                sorted.Insert(0, project.project_);
            }
        }

        private static readonly string[] ExcludeProjects = new string[]
        {
            "ZERO_CHECK.vcxproj",
            "INSTALL.vcxproj",
            "ALL_BUILD.vcxproj",
            "RUN_TESTS.vcxproj",
        };

        private static Assembly GetCPPTaskAssembly(string VCTargetsPath)
        {
            string BuildDllName = "Microsoft.Build.CPPTasks.Common.dll";
            string BuildDllPath = System.IO.Path.Combine(VCTargetsPath, BuildDllName);
            Assembly CPPTasksAssembly = null;
            if (System.IO.File.Exists(BuildDllPath))
            {
                CPPTasksAssembly = Assembly.LoadFrom(BuildDllPath);
                if (null != CPPTasksAssembly)
                {
                    Type typeCL = CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.CL");
                    Type typeRC = CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.RC");
                    Type typeLink = CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.Link");
                    Type typeLIB = CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.LIB");
                    if (null != typeCL && null != typeRC && null != typeLink && null != typeLIB)
                    {
                        return CPPTasksAssembly;
                    }
                    CPPTasksAssembly = null;
                }
            }
            return CPPTasksAssembly;
        }

        private static void AddExtraDlls(StringBuilder stringBuilder, string rootDir, string pattern)
        {
            string[] dllFiles = System.IO.Directory.GetFiles(rootDir, pattern);
            foreach (string dllFile in dllFiles)
            {
                stringBuilder.AppendLine($"    '$Root$/{System.IO.Path.GetFileName(dllFile)}',");
            }
        }

        private class BuildContext
        {
            public Assembly CppTaskAssembly_;
            //public ToolTask CLTask_;
            //public ToolTask LIBTask_;
            //public ToolTask RCTask_;
            //public ToolTask LINKTask_;
            //public MethodInfo CLGenerateCommandLine_;
            //public MethodInfo LIBGenerateCommandLine_;
            //public MethodInfo RCGenerateCommandLine_;
            //public MethodInfo LINKGenerateCommandLine_;
            public string VCTargetsPath_ = string.Empty;
            public string VCTargetsPathEffective_ = string.Empty;
            public string VC_IncludePath_ = string.Empty;
            public string VC_LibraryPath_ = string.Empty;
            public string VC_ExecutablePath_ = string.Empty;
            public string WindowsSDK_IncludePat_ = string.Empty;
            public string WindowsSDK_IncludePath_ = string.Empty;
            public string WindowsSDK_LibraryPath_ = string.Empty;
            public string WindowsSDK_ExecutablePath_ = string.Empty;
            public string WindowsSDKDir_ = string.Empty;
            public string LibrarianPath_ = string.Empty;
            public VSFastBuildCommon.VSEnvironment vsEnvironment_;
            public StringBuilder stringBuilder_;
            public StringBuilder optionBuilder_;
            public IDictionary<string, string> globalProperties_;
        }

        private class FBCompileItem
        {
            public const string Emtpry = "";

            public string Compiler => compiler_;
            public string CompilerOptions => compilerOptions_;
            public string CompilerOutputExtension => compilerOutputExtension_;
            public List<string> ComiplerInputFiles => compilerInputFiles_;

            private string compiler_;
            private string compilerOptions_;
            private string compilerOutputExtension_;
            private List<string> compilerInputFiles_;

            public FBCompileItem(string inputFile, string compiler, string compilerOptions, string compilerOutputExtension = Emtpry)
            {
                compilerInputFiles_ = new List<string>(16) { inputFile };
                compiler_ = compiler;
                compilerOptions_ = compilerOptions;
                compilerOutputExtension_ = compilerOutputExtension;

            }

            public bool AddIfMatches(string inputFile, string compiler, string compilerOptions)
            {
                if (compiler_ == compiler && compilerOptions_ == compilerOptions)
                {
                    compilerInputFiles_.Add(inputFile);
                    return true;
                }
                return false;
            }
        }

        private class FBResourceItem
        {
            public const string Emtpry = "";

            public string CompilerOptions => compilerOptions_;
            public List<string> ComiplerInputFiles => compilerInputFiles_;

            private string compilerOptions_;
            private List<string> compilerInputFiles_;

            public FBResourceItem(string inputFile, string compilerOptions)
            {
                compilerInputFiles_ = new List<string>(16) { inputFile };
                compilerOptions_ = compilerOptions;

            }

            public bool AddIfMatches(string inputFile, string compilerOptions)
            {
                if (compilerOptions_ == compilerOptions)
                {
                    compilerInputFiles_.Add(inputFile);
                    return true;
                }
                return false;
            }
        }

        public static string GetFirstPath(string path)
        {
            string? first_path = path.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).First();
            return string.IsNullOrEmpty(first_path) ? path : first_path.Trim();
        }

        public static async Task BuildForSolutionAsync(VSFastBuildVSIXPackage package, List<EnvDTE.Project> projects, string rootDirectory, string bffname, OleMenuCommand command, string originalText)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            System.Diagnostics.Debug.Assert(null != package);
            System.Diagnostics.Debug.Assert(null != projects);
            System.Diagnostics.Debug.Assert(null != command);

            projects.RemoveAll(
                x =>
                {
                    foreach (string exclude in ExcludeProjects)
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
            if (projects.Count <= 0)
            {
                return;
            }

            string fbuildPath = System.IO.Path.Combine(rootDirectory, bffname);
            BuildContext buildContext = new BuildContext();
            buildContext.stringBuilder_ = new StringBuilder(1024);
            buildContext.optionBuilder_ = new StringBuilder(1024);

            VCProject vcProject = projects[0].Object as VCProject;
            VCConfiguration activeConfig = vcProject.ActiveConfiguration;
            buildContext.VCTargetsPath_ = activeConfig.Evaluate("$(VCTargetsPath)");
            buildContext.VCTargetsPathEffective_ = activeConfig.Evaluate("$(VCTargetsPathEffective)");
            buildContext.VC_IncludePath_ = activeConfig.Evaluate("$(VC_IncludePath)");
            buildContext.VC_LibraryPath_ = activeConfig.Evaluate("$(VC_LibraryPath_x64)");
            buildContext.VC_ExecutablePath_ = activeConfig.Evaluate("$(VC_ExecutablePath_x64)");
            buildContext.WindowsSDK_IncludePath_ = GetFirstPath(activeConfig.Evaluate("$(WindowsSDK_IncludePath)"));
            buildContext.WindowsSDK_LibraryPath_ = GetFirstPath(activeConfig.Evaluate("$(WindowsSDK_LibraryPath_x64)"));
            buildContext.WindowsSDK_ExecutablePath_ = GetFirstPath(activeConfig.Evaluate("$(WindowsSDK_ExecutablePath_x64)"));
            buildContext.WindowsSDKDir_ = GetFirstPath(activeConfig.Evaluate("$(WindowsSDKDir)"));
            buildContext.CppTaskAssembly_ = GetCPPTaskAssembly(buildContext.VCTargetsPath_);
            if (null == buildContext.CppTaskAssembly_)
            {
                return;
            }
            //buildContext.CLTask_ = Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.CL")) as ToolTask;
            //buildContext.LIBTask_ = Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.LIB")) as ToolTask;
            //buildContext.RCTask_ = Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.RC")) as ToolTask;
            //buildContext.LINKTask_ = Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.Link")) as ToolTask;

#if false
            buildContext.CLGenerateCommandLine_ = Delegate.CreateDelegate(typeof(Func<string>), buildContext.CLTask_, buildContext.CLTask_.GetType().GetRuntimeMethods().Where(method => method.Name == "GenerateCommandLine").First()) as Func<string, object, object>;
            buildContext.LIBGenerateCommandLine_ = Delegate.CreateDelegate(typeof(Func<string, object, object>), buildContext.LIBTask_, buildContext.LIBTask_.GetType().GetRuntimeMethods().Where(method => method.Name == "GenerateCommandLine").First()) as Func<string, object, object>;
            buildContext.RCGenerateCommandLine_ = Delegate.CreateDelegate(typeof(Func<string, object, object>), buildContext.RCTask_, buildContext.RCTask_.GetType().GetRuntimeMethods().Where(method => method.Name == "GenerateCommandLine").First()) as Func<string, object, object>;
            buildContext.LINKGenerateCommandLine_ = Delegate.CreateDelegate(typeof(Func<string, object, object>), buildContext.LINKTask_, buildContext.LINKTask_.GetType().GetRuntimeMethods().Where(method => method.Name == "GenerateCommandLine").First()) as Func<string, object, object>;
#else
            //buildContext.CLGenerateCommandLine_ = buildContext.CLTask_.GetType().GetRuntimeMethods().Where(method => method.Name == "GenerateCommandLine").First();
            //buildContext.LIBGenerateCommandLine_ = buildContext.LIBTask_.GetType().GetRuntimeMethods().Where(method => method.Name == "GenerateCommandLine").First();
            //buildContext.RCGenerateCommandLine_ = buildContext.RCTask_.GetType().GetRuntimeMethods().Where(method => method.Name == "GenerateCommandLine").First();
            //buildContext.LINKGenerateCommandLine_ = buildContext.LINKTask_.GetType().GetRuntimeMethods().Where(method => method.Name == "GenerateCommandLine").First();
#endif

            SolutionBuild2 solutionBuild = package.DTE.Solution.SolutionBuild as SolutionBuild2;
            SolutionConfiguration2 solutionConfiguration = solutionBuild.ActiveConfiguration as SolutionConfiguration2;
            buildContext.globalProperties_ = new Dictionary<string, string>()
            {
                { "Configuration", solutionConfiguration.Name },
                { "Platform", solutionConfiguration.PlatformName }
            };

            List<VSFastProject> vSFastProjects = new List<VSFastProject>(projects.Capacity);
            {
                foreach (EnvDTE.Project project in projects)
                {
                    BuildDependency buildDependency = solutionBuild.BuildDependencies.Item(project.UniqueName);
                    if (null == buildDependency)
                    {
                        continue;
                    }
                    VSFastProject vSFastProject = new() { project_ = project, dependencies_ = new List<EnvDTE.Project>() };
                    object[] requiredProjects = buildDependency.RequiredProjects as object[];
                    foreach (object item in requiredProjects)
                    {
                        EnvDTE.Project dependentProject = item as EnvDTE.Project;
                        if (null == dependentProject)
                        {
                            continue;
                        }
                        bool found = false;
                        foreach (EnvDTE.Project p in vSFastProject.dependencies_)
                        {
                            if (p.UniqueName == dependentProject.UniqueName)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            foreach (string exclude in ExcludeProjects)
                            {
                                string uniqueName = dependentProject.UniqueName;
                                if (uniqueName.EndsWith(exclude))
                                {
                                    found = true;
                                    break;
                                }
                            }
                        }
                        if (!found)
                        {
                            vSFastProject.dependencies_.Add(dependentProject);
                        }
                    }
                    vSFastProjects.Add(vSFastProject);
                }
                TopologicalSort(vSFastProjects);
            }
            StringBuilder stringBuilder = buildContext.stringBuilder_;

            stringBuilder.AppendLine("// Helper variables");
            stringBuilder.AppendLine(".FB_INPUT_1_PLACEHOLDER = '\"%1\"'");
            stringBuilder.AppendLine(".FB_INPUT_1_0_PLACEHOLDER = '\"%1[0]\"'");
            stringBuilder.AppendLine(".FB_INPUT_2_PLACEHOLDER = '\"%2\"'");
            stringBuilder.AppendLine(".FB_INPUT_3_PLACEHOLDER = '\"%3\"'");

            stringBuilder.AppendLine($".VCExePath = '{buildContext.VC_ExecutablePath_}'");
            stringBuilder.AppendLine($".WindowsSDKBasePath = '{buildContext.WindowsSDKDir_}'");

            stringBuilder.AppendLine("// Settings");
            stringBuilder.AppendLine("Settings");
            stringBuilder.AppendLine("{");
            stringBuilder.AppendLine("  .Utils =");
            stringBuilder.AppendLine("  [");
            stringBuilder.AppendLine("    .ConcurrencyGroupName = 'Utils'");
            stringBuilder.AppendLine("    .ConcurrencyLimit = 1");
            stringBuilder.AppendLine("  ]");
            stringBuilder.AppendLine("  .ConcurrencyGroups =");
            stringBuilder.AppendLine("  {");
            stringBuilder.AppendLine("    .Utils");
            stringBuilder.AppendLine("  }");
            stringBuilder.AppendLine("}");

            {
                List<Tuple<string, string>> envVars = new List<Tuple<string, string>>();
                foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process))
                {
                    string key = variable.Key as string;
                    if (key == "Temp" || key == "TMP")
                    {
                        envVars.Add(new Tuple<string, string>(key, System.IO.Path.Combine(rootDirectory, "temp")));
                    }
                    else if (string.Compare(key, "PATH", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        string pathValue = variable.Value as string;
                        pathValue += ";" + buildContext.VC_ExecutablePath_ + ";" + buildContext.WindowsSDK_ExecutablePath_;
                        pathValue = pathValue.Replace(";;", ";");
                        envVars.Add(new Tuple<string, string>(key, pathValue));
                    }
                    else
                    {
                        envVars.Add(new Tuple<string, string>(key, variable.Value as string));
                    }
                }
                envVars.Sort((x, y) => string.Compare(x.Item1, y.Item1, StringComparison.Ordinal));
                envVars.Add(new Tuple<string, string>("vsconsoleoutput", "1"));
                envVars.Add(new Tuple<string, string>("CXX", "1"));
                envVars.Add(new Tuple<string, string>("RC", "1"));
                envVars.Add(new Tuple<string, string>("CC", "1"));
                stringBuilder.AppendLine(".LocalEnv =");
                stringBuilder.AppendLine("{");
                for (int i = 0; i < envVars.Count; ++i)
                {
                    stringBuilder.AppendFormat("  '{0}={1}'", envVars[i].Item1, envVars[i].Item2);
                    if (i != (envVars.Count - 1))
                    {
                        stringBuilder.AppendLine(",");
                    }
                    else
                    {
                        stringBuilder.AppendLine();
                    }
                }
                stringBuilder.AppendLine("}");
            }
            // Compilers
            stringBuilder.AppendLine("// Compilers");

            // Compiler_CXX
            stringBuilder.AppendLine("Compiler('Compiler_CXX')");
            stringBuilder.AppendLine("{");
            stringBuilder.AppendLine($"  .Root = '{buildContext.VC_ExecutablePath_}'");
            stringBuilder.AppendLine($"  .Executable = '{buildContext.VC_ExecutablePath_}\\cl.exe'");
            stringBuilder.AppendLine("  .CompilerFamily = 'msvc'");
            stringBuilder.AppendLine("  .Environment = '.LocalEnv'");
            stringBuilder.AppendLine("  .ExtraFiles =");
            stringBuilder.AppendLine("  {");
            stringBuilder.AppendLine("    '$Root$/c1.dll',");
            stringBuilder.AppendLine("    '$Root$/c1xx.dll',");
            stringBuilder.AppendLine("    '$Root$/c2.dll',");

            if (System.IO.File.Exists(buildContext.VC_ExecutablePath_ + "\\1041\\clui.dll")) //Check English first...
            {
                stringBuilder.AppendLine("    '$Root$\\1041\\clui.dll',");
            }
            else
            {
                IEnumerable<string> numericDirectories = System.IO.Directory.GetDirectories(buildContext.VC_ExecutablePath_).Where(d => System.IO.Path.GetFileName(d).All(char.IsDigit));
                IEnumerable<string> cluiDirectories = numericDirectories.Where(d => System.IO.Directory.GetFiles(d, "clui.dll").Any());
                if (cluiDirectories.Any())
                {
                    stringBuilder.AppendLine($"    '$Root$\\{System.IO.Path.GetFileName(cluiDirectories.First())}\\clui.dll,'");
                }
            }

            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "msobj*.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "mspdb*.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "mspft*.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "msvcp*.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "tbbmalloc.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "vcmeta.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "vcruntime*.dll");

            stringBuilder.AppendLine("    '$Root$\\mspdbsrv.exe'");
            //stringBuilder.AppendLine("    '$Root$\\mspdbcore.dll',");

            //stringBuilder.AppendLine("    '$Root$/mspft{0}.dll'\n", PlatformToolsetVersion);
            //stringBuilder.AppendLine("    '$Root$/msobj{0}.dll'\n", PlatformToolsetVersion);
            //stringBuilder.AppendLine("    '$Root$/mspdb{0}.dll'\n", PlatformToolsetVersion);
            //stringBuilder.AppendLine("    '$VSBasePath$/VC/redist/{0}/Microsoft.VC{1}.CRT/msvcp{1}.dll'\n", Platform == "Win32" ? "x86" : "x64", PlatformToolsetVersion);
            //stringBuilder.AppendLine("    '$VSBasePath$/VC/redist/{0}/Microsoft.VC{1}.CRT/vccorlib{1}.dll'\n", Platform == "Win32" ? "x86" : "x64", PlatformToolsetVersion);

            stringBuilder.AppendLine("  }");
            stringBuilder.AppendLine("}");
            stringBuilder.AppendLine(".Compiler_CXX = 'Compiler_CXX'");
            stringBuilder.AppendLine(".Compiler_Dummy = 'Compiler_CXX'");

            // Compiler_RC
            {
                stringBuilder.AppendLine("Compiler('Compiler_RC')");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine($"  .Root = '{buildContext.WindowsSDK_ExecutablePath_}'");
                stringBuilder.AppendLine($"  .Executable = '{buildContext.WindowsSDK_ExecutablePath_}\\rc.exe'");
                stringBuilder.AppendLine("  .CompilerFamily = 'custom'");
                stringBuilder.AppendLine("  .Environment = '.LocalEnv'");
                stringBuilder.AppendLine("}");
                stringBuilder.AppendLine(".Compiler_RC = 'Compiler_RC'");
            }

            // Librarian
            {
                buildContext.LibrarianPath_ = System.IO.Path.Combine(buildContext.VC_ExecutablePath_, "lib.exe");
            }

            List<string> projectTargets = new List<string>(vSFastProjects.Count);
            foreach (VSFastProject project in vSFastProjects)
            {
                AddProject(buildContext, project, projectTargets);
            }
            // All
            if(0<projectTargets.Count){
                stringBuilder.AppendLine("Alias('all')");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine("  .Targets =");
                stringBuilder.AppendLine("  {");
                addStringList(stringBuilder, projectTargets, "    ");
                stringBuilder.AppendLine("  }");
                stringBuilder.AppendLine("  .Hidden = true");
                stringBuilder.AppendLine("}");
            }
            System.IO.File.WriteAllText(fbuildPath, stringBuilder.ToString());
        }

        private static bool IsBuildTarget(Microsoft.Build.Evaluation.ProjectItem item)
        {
            if (item.DirectMetadata.Any())
            {
                if (item.DirectMetadata.Where(x => x.Name == "ExcludedFromBuild" && x.EvaluatedValue == "true").Any())
                {
                    return false;
                }
            }
            return true;
        }

        private struct PrecompiledHeaderInfo
        {
            public PrecompiledHeaderInfo()
            {
            }
            public bool IsValid()
            {
                return !string.IsNullOrEmpty(PCHInputFile_) && !string.IsNullOrEmpty(PCHOutputFile_);
            }
            public string PCHOutputFile_ = string.Empty;
            public string PCHInputFile_ = string.Empty;
            public string PCHOptions_ = string.Empty;
        }

        private static bool IsCreatePrecompiledHeader(Microsoft.Build.Evaluation.ProjectItem item)
        {
            if (item.Metadata.Where(x => x.Name == "PrecompiledHeader" && x.EvaluatedValue == "Create").Any())
            {
                return true;
            }
            return false;
        }

        private static void GetPrecompiledHeader(Microsoft.Build.Evaluation.ProjectItem item, ref PrecompiledHeaderInfo precompiledHeaderInfo)
        {
            if (item.Metadata.Where(x => x.Name == "PrecompiledHeader" && x.EvaluatedValue == "Use").Any())
            {
                //precompiledHeaderInfo.PCHInputFile_ = item.GetMetadataValue("PrecompiledHeaderFile").Replace("//", "/").Replace(".hxx", ".cxx");
            }
            if (item.Metadata.Where(x => x.Name == "PrecompiledHeader" && x.EvaluatedValue == "Create").Any())
            {
                precompiledHeaderInfo.PCHInputFile_ = item.EvaluatedInclude;
                precompiledHeaderInfo.PCHOutputFile_ = item.GetMetadataValue("PrecompiledHeaderOutputFile").Replace("/", "\\").Replace("\\\\", "\\");
            }
            //if (item.Metadata.Where(x => x.Name == "PrecompiledHeader" && x.EvaluatedValue == "NotUsing").Any())
            //{
            //    return true;
            //}
        }

        private static string GenerateTaskCommandLine(ToolTask task, string[] propertiesToSkip, IEnumerable<ProjectMetadata> metaDataList)
        {
            foreach (ProjectMetadata metaData in metaDataList)
            {
                if (propertiesToSkip.Contains(metaData.Name))
                {
                    continue;
                }
                Log.OutputBuildLine($"{metaData.Name} = {metaData.EvaluatedValue}");
                IEnumerable<PropertyInfo> matchingProps = task.GetType().GetProperties().Where(prop => prop.Name == metaData.Name);
                if (matchingProps.Any() && !string.IsNullOrEmpty(metaData.EvaluatedValue))
                {
                    string evaluatedValue = metaData.EvaluatedValue.Trim();
                    if (metaData.Name == "AdditionalIncludeDirectories")
                    {
                        evaluatedValue = evaluatedValue.Replace("\\\\", "\\");
                    }

                    PropertyInfo propInfo = matchingProps.First();
                    if (propInfo.PropertyType.IsArray && propInfo.PropertyType.GetElementType() == typeof(string))
                    {
                        propInfo.SetValue(task, Convert.ChangeType(evaluatedValue.Split(';'), propInfo.PropertyType));
                    }
                    else
                    {
                        propInfo.SetValue(task, Convert.ChangeType(evaluatedValue, propInfo.PropertyType));
                    }
                }
            }
            MethodInfo methodInfo = task.GetType().GetRuntimeMethods().Where(method => method.Name == "GenerateCommandLine").First();
            return methodInfo.Invoke(task, new object[] { Type.Missing, Type.Missing }) as string;
        }

        public static void addStringList(StringBuilder stringBuilder, List<string> list, string prefix)
        {
            for (int i = 0; i < list.Count; ++i)
            {
                stringBuilder.Append($"{prefix}'{list[i]}'");
                if (i != list.Count - 1)
                {
                    stringBuilder.AppendLine(",");
                }
                else
                {
                    stringBuilder.AppendLine();
                }
            }
        }

        private enum BuildType
        {
            Application,
            StaticLib,
            DynamicLib
        }

        private static void AddProject(BuildContext buildContext, VSFastProject project, List<string> projectTargets)
        {
            Microsoft.Build.Evaluation.Project buildProject = new Microsoft.Build.Evaluation.Project(project.project_.FullName, buildContext.globalProperties_, null);
            string configType = buildProject.GetProperty("ConfigurationType").EvaluatedValue;
            BuildType buildType;
            switch (configType)
            {
                case "DynamicLibrary":
                    buildType = BuildType.DynamicLib;
                    break;
                case "StaticLibrary":
                    buildType = BuildType.StaticLib;
                    break;
                case "Application":
                default:
                    buildType = BuildType.Application;
                    break;
            }
            string intDir = buildProject.GetProperty("IntDir").EvaluatedValue.Replace("\\", "/");
            string outDir = buildProject.GetProperty("OutDir").EvaluatedValue.Replace("\\", "/");
            string targetName = project.project_.Name;
            projectTargets.Add(targetName);
            StringBuilder stringBuilder = buildContext.stringBuilder_;
            stringBuilder.AppendLine($"// {targetName}");

            ICollection<Microsoft.Build.Evaluation.ProjectItem> compileItems = buildProject.GetItems("ClCompile");

            // Dependencies
            if (0 < project.dependencies_.Count)
            {
                stringBuilder.AppendLine($"Alias('{targetName}-deps')");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine("  .Targets =");
                stringBuilder.AppendLine("  {");
                for (int i = 0; i < project.dependencies_.Count; ++i)
                {
                    stringBuilder.Append($"    '{project.dependencies_[i].Name}'");
                    if (i == (project.dependencies_.Count - 1))
                    {
                        stringBuilder.AppendLine();
                    }
                    else
                    {
                        stringBuilder.AppendLine(",");
                    }
                }
                stringBuilder.AppendLine("  }");
                stringBuilder.AppendLine("  .Hidden = true");
                stringBuilder.AppendLine("}");
            }
#if false
            List<Tuple<string, string>> properties = new List<Tuple<string, string>>();
            foreach (ProjectMetadata property in buildProject.AllEvaluatedItemDefinitionMetadata)
            {
                properties.Add(new Tuple<string, string>(property.Name, property.EvaluatedValue));
            }
            properties.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            foreach (Tuple<string, string> property in properties)
            {
                Log.OutputBuildLine($"    {property.Item1} = '{property.Item2}'");
            }
#endif
            // Precompile Header
            PrecompiledHeaderInfo precompiledHeaderInfo = new PrecompiledHeaderInfo();
            foreach (Microsoft.Build.Evaluation.ProjectItem item in compileItems)
            {
                if (!IsBuildTarget(item))
                {
                    continue;
                }
                #if false
            foreach(ProjectMetadata meta in item.Metadata)
            {
                    Log.OutputBuildLine($"    {meta.Name} = '{meta.EvaluatedValue}'");
            }
            #endif

                GetPrecompiledHeader(item, ref precompiledHeaderInfo);
            }
            if (precompiledHeaderInfo.IsValid())
            {
                foreach (Microsoft.Build.Evaluation.ProjectItem item in compileItems)
                {
                    if (!IsBuildTarget(item))
                    {
                        continue;
                    }
                    string evalInclude = item.EvaluatedInclude;
                    if (IsCreatePrecompiledHeader(item))
                    {
                        ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.CL"));
                        //string pchCompilerOptions = GenerateTaskCommandLine(task, new string[] { "ObjectFileName", "AssemblerListingLocation", "ProgramDataBaseFileName" }, item.Metadata) + " /FS";
                        string pchCompilerOptions = GenerateTaskCommandLine(task, new string[] { "ObjectFileName", "AssemblerListingLocation"}, item.Metadata) + " /FS";
                        pchCompilerOptions = pchCompilerOptions.Replace("  ", " ").Replace("\\", "/").Replace("//", "/");
                        precompiledHeaderInfo.PCHOptions_ = $"\"%1\" /Fo\"%3\" {pchCompilerOptions}";
                    }
                }
            }

            List<string> objTargets = new List<string>(2);
            { // Resource objects
                List<FBResourceItem> resourceItems = new List<FBResourceItem>(4);
                ICollection<Microsoft.Build.Evaluation.ProjectItem> resourceCompileItems = buildProject.GetItems("ResourceCompile");
                foreach (Microsoft.Build.Evaluation.ProjectItem item in resourceCompileItems)
                {
                    if (!IsBuildTarget(item))
                    {
                        continue;
                    }
                    ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.RC"));
                    string resourceCompilerOptions = GenerateTaskCommandLine(task, new string[] { "ResourceOutputFileName", "DesigntimePreprocessorDefinitions" }, item.Metadata);
                    resourceCompilerOptions = resourceCompilerOptions.Replace("\\", "/").Replace("//", "/");
                    string formattedCompilerOptions = string.Format("{0} /fo\"%2\" \"%1\"", resourceCompilerOptions).Replace("/TP", string.Empty).Replace("/TC", string.Empty);
                    string evaluatedInclude = item.EvaluatedInclude.Replace("\\", "/").Replace("//", "/");
                    IEnumerable<FBResourceItem> matchingNodes = resourceItems.Where(el => el.AddIfMatches(evaluatedInclude, formattedCompilerOptions));
                    if (!matchingNodes.Any())
                    {
                        resourceItems.Add(new FBResourceItem(evaluatedInclude, formattedCompilerOptions));
                    }
                }

                if (0 < resourceItems.Count)
                {
                    int count = 0;
                    foreach (FBResourceItem item in resourceItems)
                    {
                        string resourceTarget = $"{targetName}_rc_objs_{count}";
                        objTargets.Add(resourceTarget);
                        stringBuilder.AppendLine($"ObjectList('{resourceTarget}')");
                        stringBuilder.AppendLine("{");
                        if (0 < project.dependencies_.Count)
                        {
                            stringBuilder.AppendLine("  .PreBuildDependencies =");
                            stringBuilder.AppendLine("  {");
                            stringBuilder.AppendLine($"    '{targetName}-deps'");
                            stringBuilder.AppendLine("  }");
                        }
                        stringBuilder.AppendLine("  .Compiler = .Compiler_RC");
                        if (!string.IsNullOrEmpty(item.CompilerOptions))
                        {
                            stringBuilder.AppendLine($"  .CompilerOptions = ' {item.CompilerOptions}'");
                        }
                        stringBuilder.AppendLine($"  .CompilerOutputPath = '{intDir}'");
                        stringBuilder.AppendLine("  .CompilerOutputExtension = '.res'");
                        stringBuilder.AppendLine("  .CompilerOutputKeepBaseExtension = true");
                        stringBuilder.AppendLine("  .CompilerInputFiles =");
                        stringBuilder.AppendLine("  {");
                        for (int j = 0; j < item.ComiplerInputFiles.Count; ++j)
                        {
                            if (j == (item.ComiplerInputFiles.Count - 1))
                            {
                                stringBuilder.AppendLine($"    '{item.ComiplerInputFiles[j]}'");
                            }
                            else
                            {
                                stringBuilder.AppendLine($"    '{item.ComiplerInputFiles[j]}',");
                            }
                        }
                        stringBuilder.AppendLine("  }");

                        stringBuilder.AppendLine("  .Hidden = true");
                        stringBuilder.AppendLine("}");
                        ++count;
                    }
                }
            } // Resource objects

            // ObjectList
            List<FBCompileItem> fbCompileItem = new List<FBCompileItem>(16);
            { // Gather compile items
                string[] propertiesToSkip = new string[] { "ObjectFileName", "AssemblerListingLocation" };
                foreach (Microsoft.Build.Evaluation.ProjectItem item in compileItems)
                {
                    if (!IsBuildTarget(item))
                    {
                        continue;
                    }
                    string pchFile = string.Empty;
                    if (IsCreatePrecompiledHeader(item))
                    {
                        continue;
                    }

                    ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.CL"));
                    //task.GetType().GetProperty("Sources").SetValue(Task, new Microsoft.Build.Utilities.TaskItem[] { new Microsoft.Build.Utilities.TaskItem() }); //CPPTasks throws an exception otherwise...
                    //buildContext.CLTask_.GetType().GetProperty("Sources").SetValue(buildContext.CLTask_, new Microsoft.Build.Utilities.TaskItem[] { new Microsoft.Build.Utilities.TaskItem() });
                    string tempCompilerOptions = GenerateTaskCommandLine(task, propertiesToSkip, item.Metadata) + " /FS";
                    StringBuilder optionBuilder = buildContext.optionBuilder_;
                    optionBuilder.Clear();
                    optionBuilder.Append("\"%1\" /Fo\"%2\" ");
                    optionBuilder.Append(tempCompilerOptions);
                    optionBuilder = optionBuilder.Replace("\\", "/").Replace("//", "/").Replace("/TP", string.Empty).Replace("/TC", string.Empty);
                    if (item.EvaluatedInclude.EndsWith(".c"))
                    {
                        optionBuilder.Append(" /TC");
                    }
                    else
                    {
                        optionBuilder.Append(" /TP");
                    }
                    optionBuilder = optionBuilder.Replace("   ", " ").Replace("  ", " ");
                    string formattedCompilerOptions = optionBuilder.ToString();
                    string evaluatedInclude = item.EvaluatedInclude.Replace("\\", "/").Replace("//", "/");
                    IEnumerable<FBCompileItem> matchingNodes = fbCompileItem.Where(el => el.AddIfMatches(evaluatedInclude, ".Compiler_CXX", formattedCompilerOptions));
                    if (!matchingNodes.Any())
                    {
                        fbCompileItem.Add(new FBCompileItem(evaluatedInclude, ".Compiler_CXX", formattedCompilerOptions));
                    }
                }
            } // Gather compile items

            bool unityBuild = false;
            for (int i = 0; i < fbCompileItem.Count; ++i)
            {
                bool usedUnity = false;
                if (unityBuild && fbCompileItem[i].Compiler != "rc" && 1 < fbCompileItem[i].ComiplerInputFiles.Count)
                {
                    stringBuilder.AppendLine($"Unity('{targetName}-unity{i}')");
                    stringBuilder.AppendLine("{");
                    stringBuilder.AppendLine($"  .UnityInputFiles = {{{string.Join(",", fbCompileItem[i].ComiplerInputFiles.ConvertAll(el => string.Format("'{0}'", el)).ToArray())}}}");
                    stringBuilder.AppendLine($"  .UnityOutputPath = '{intDir}'");
                    stringBuilder.AppendLine($"  .UnityNumFiles = {1 + fbCompileItem[i].ComiplerInputFiles.Count / 10}");
                    stringBuilder.AppendLine("}");
                    usedUnity = true;
                }

                string objTargetName = $"{targetName}-objs{i}";
                objTargets.Add(objTargetName);
                stringBuilder.AppendLine($"ObjectList('{objTargetName}')");
                stringBuilder.AppendLine("{");
                if (0 < project.dependencies_.Count)
                {
                    stringBuilder.AppendLine("  .PreBuildDependencies =");
                    stringBuilder.AppendLine("  {");
                    stringBuilder.AppendLine($"    '{targetName}-deps'");
                    stringBuilder.AppendLine("  }");
                }

                stringBuilder.AppendLine($"  .Compiler = {fbCompileItem[i].Compiler}");

                if (precompiledHeaderInfo.IsValid())
                {
                    stringBuilder.AppendLine($"  .PCHInputFile = '{precompiledHeaderInfo.PCHInputFile_}'");
                    stringBuilder.AppendLine($"  .PCHOutputFile = '{precompiledHeaderInfo.PCHOutputFile_}'");
                    if (!string.IsNullOrEmpty(precompiledHeaderInfo.PCHOptions_))
                    {
                        stringBuilder.AppendLine($"  .PCHOptions = '{precompiledHeaderInfo.PCHOptions_}'");
                    }
                }
                stringBuilder.AppendLine($"  .CompilerOptions = ' {fbCompileItem[i].CompilerOptions}'");
                stringBuilder.AppendLine($"  .CompilerOutputPath = '{intDir}'");
                if (usedUnity)
                {
                    stringBuilder.AppendLine($"  .CompilerInputUnity = {{ '{targetName}-unity{i}' }}");
                }
                else
                {
                    string str = string.Join(",", fbCompileItem[i].ComiplerInputFiles.ConvertAll(x => string.Format("'{0}'", x)).ToArray());
                    stringBuilder.AppendLine($"  .CompilerInputFiles = {{ {str} }}");
                }
                if (!string.IsNullOrEmpty(fbCompileItem[i].CompilerOutputExtension))
                {
                    stringBuilder.AppendLine($"  .CompilerOutputExtension = '{fbCompileItem[i].CompilerOutputExtension}'");
                }
                stringBuilder.AppendLine("  .Hidden = true");
                stringBuilder.AppendLine("}");
            } //for (int i = 0

            // Final target
            string compilerOptions = 0 < fbCompileItem.Count ? fbCompileItem[0].CompilerOptions : string.Empty;
            switch (buildType)
            {
                case BuildType.Application:
                    {
                        stringBuilder.AppendLine($"Executable('{targetName}')");
                        stringBuilder.AppendLine("{");
                        if (0 < project.dependencies_.Count)
                        {
                            stringBuilder.AppendLine("  .PreBuildDependencies =");
                            stringBuilder.AppendLine("  {");
                            stringBuilder.AppendLine($"    '{targetName}-deps'");
                            stringBuilder.AppendLine("  }");
                        }
                        stringBuilder.AppendLine("  .Environment = .LocalEnv");
                        stringBuilder.AppendLine($"  .Linker = '{buildContext.LibrarianPath_}'");
                        ProjectItemDefinition linkDefinitions = buildProject.ItemDefinitions["Link"];
                        string outputFile = linkDefinitions.GetMetadataValue("OutputFile");
                        string outputDirectory = System.IO.Path.GetDirectoryName(outputFile);
                        if (!System.IO.Directory.Exists(outputDirectory))
                        {
                            System.IO.Directory.CreateDirectory(outputDirectory);
                        }
                        ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.Link"));
                        string linkerOptions = GenerateTaskCommandLine(task, new string[] { "OutputFile", "ProfileGuidedDatabase" }, linkDefinitions.Metadata);
                        linkerOptions = linkerOptions.Replace("'", "^'");
                        stringBuilder.AppendLine($"  .LinkerOptions = '\"%1\" /OUT:\"%2\" {linkerOptions}'");
                        stringBuilder.AppendLine($"  .LinkerOutput = '{outputFile}'");

                        stringBuilder.AppendLine("  .Libraries =");
                        stringBuilder.AppendLine("  {");
                        addStringList(stringBuilder, objTargets, "    ");
                        stringBuilder.AppendLine("  }");
                        stringBuilder.AppendLine("  .LinkerType = 'auto'");
                        stringBuilder.AppendLine("  .LinkerLinkObjects = false");
                        stringBuilder.AppendLine("}");
                    }
                    break;
                case BuildType.StaticLib:
                    {
                        stringBuilder.AppendLine($"Library('{targetName}')");
                        stringBuilder.AppendLine("{");
                        stringBuilder.AppendLine("  .Compiler = .Compiler_Dummy");
                        if (!string.IsNullOrEmpty(compilerOptions))
                        {
                            stringBuilder.AppendLine($"  .CompilerOptions = '\"%1\" /Fo\"%2\" /c {compilerOptions}'");
                        }
                        stringBuilder.AppendLine($"  .CompilerOutputPath = '{intDir}'");
                        stringBuilder.AppendLine("  .Environment = .LocalEnv");
                        stringBuilder.AppendLine($"  .Librarian = '{buildContext.LibrarianPath_}'");

                        ProjectItemDefinition libDefinitions = buildProject.ItemDefinitions["Lib"];
                        ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.LIB"));
                        string linkerOptions = GenerateTaskCommandLine(task, new string[] { "OutputFile" }, libDefinitions.Metadata);
                        string outputFile = libDefinitions.GetMetadataValue("OutputFile");
                        stringBuilder.AppendLine($"  .LibrarianOptions = '\"%1\" /OUT:\"%2\" {linkerOptions}'");
                        stringBuilder.AppendLine($"  .LibrarianOutput = '{outputFile}'");

                        stringBuilder.AppendLine("  .LibrarianAdditionalInputs =");
                        stringBuilder.AppendLine("  {");
                        addStringList(stringBuilder, objTargets, "    ");
                        stringBuilder.AppendLine("  }");
                        stringBuilder.AppendLine("  .LinkerType = 'auto'");
                        stringBuilder.AppendLine("}");
                    }
                    break;
                case BuildType.DynamicLib:
                    {
                        stringBuilder.AppendLine($"DLL('{targetName}')");
                        stringBuilder.AppendLine("{");
                        stringBuilder.AppendLine("  .Environment = .LocalEnv");
                        stringBuilder.AppendLine($"  .Linker = '{buildContext.LibrarianPath_}'");
                        ProjectItemDefinition linkDefinitions = buildProject.ItemDefinitions["Link"];
                        string outputFile = linkDefinitions.GetMetadataValue("OutputFile");
                        string outputDirectory = System.IO.Path.GetDirectoryName(outputFile);
                        if (!System.IO.Directory.Exists(outputDirectory))
                        {
                            System.IO.Directory.CreateDirectory(outputDirectory);
                        }
                        ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.Link"));
                        string linkerOptions = GenerateTaskCommandLine(task, new string[] { "OutputFile", "ProfileGuidedDatabase" }, linkDefinitions.Metadata);
                        linkerOptions = linkerOptions.Replace("'", "^'");
                        stringBuilder.AppendLine($"  .LinkerOptions = '\"%1\" /OUT:\"%2\" {linkerOptions}'");
                        stringBuilder.AppendLine($"  .LinkerOutput = '{outputFile}'");

                        stringBuilder.AppendLine("  .Libraries =");
                        stringBuilder.AppendLine("  {");
                        addStringList(stringBuilder, objTargets, "    ");
                        stringBuilder.AppendLine("  }");
                        stringBuilder.AppendLine("  .LinkerType = 'auto'");
                        stringBuilder.AppendLine("  .LinkerLinkObjects = false");
                        stringBuilder.AppendLine("}");
                    }
                    break;
            }
            if (buildProject.GetItems("PostBuildEvent").Any())
            {
                Microsoft.Build.Evaluation.ProjectItem buildEvent = buildProject.GetItems("PostBuildEvent").First();
                if (buildEvent.Metadata.Any())
                {
                    ProjectMetadata metaData = buildEvent.Metadata.First();
                    if (!string.IsNullOrEmpty(metaData.EvaluatedValue))
                    {
                        //string batchText = "call \"" + VCBasePath + "Auxiliary\\Build\\vcvarsall.bat\" " + (platform == "Win32" ? "x86" : "x64") + " " + WindowsSDKTarget + "\n";
                        string postBuildBatchFile = System.IO.Path.Combine(buildProject.DirectoryPath, System.IO.Path.GetFileNameWithoutExtension(buildProject.FullPath) + "_postbuild.bat");
                        System.IO.File.WriteAllText(postBuildBatchFile, metaData.EvaluatedValue);
                        stringBuilder.AppendLine($"Exec('{targetName}_postbuild')");
                        stringBuilder.AppendLine("{");
                        stringBuilder.AppendLine("  .ExecExecutable = 'C:/Windows/System32/cmd.exe'");
                        stringBuilder.AppendLine($"  .ExecArguments = '{postBuildBatchFile}'");
                        stringBuilder.AppendLine($"  .ExecInput = '{postBuildBatchFile}'");
                        stringBuilder.AppendLine($"  .ExecOutput = '{postBuildBatchFile}.txt'");
                        stringBuilder.AppendLine("  .ExecUseStdOutAsOutput = true");
                        stringBuilder.AppendLine("  .ExecAlways = true");
                        stringBuilder.AppendLine("}");
                    }
                }
            }
        }
    }
}
