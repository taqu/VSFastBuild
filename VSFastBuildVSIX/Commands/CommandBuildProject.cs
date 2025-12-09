using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Utilities;
using Microsoft.VisualStudio.PlatformUI.OleComponentSupport;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.VCProjectEngine;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

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
            if (package.IsBuildProcessRunning())
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

            targets.RemoveAll(
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

            if (targets.Count <= 0)
            {
                LeaveProcess(package, Command, commandText_);
                return;
            }
            targets.Sort((x0,x1)=>{
                    return string.Compare(x0.Name, x1.Name);
            });
            StringBuilder stringBuilder = new StringBuilder();
            foreach (EnvDTE.Project p in targets)
            {
                stringBuilder.Append(p.Name);
            }
            byte[] bytes = Encoding.UTF8.GetBytes(stringBuilder.ToString());
            string hash = ByteArrayToHexStringHalf(MurmurHash.MurmurHash3.ComputeHash(bytes));
            SolutionBuild2 solutionBuild = package.DTE.Solution.SolutionBuild as SolutionBuild2;
            SolutionConfiguration2 solutionConfiguration = solutionBuild.ActiveConfiguration as SolutionConfiguration2;
            string rootDirectory = System.IO.Path.GetDirectoryName(package.DTE.Solution.FullName);
            string bffname = string.Format("fbuild_{0}_{1}_{2}.bff", hash, solutionConfiguration.Name, solutionConfiguration.PlatformName);
            string bffpath = System.IO.Path.Combine(rootDirectory, bffname);
            await BuildForSolutionAsync(package, targets, bffpath);
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
        public static async Task StartMonitor(ToolkitPackage package, bool createIfNotExists=false)
        {
            ToolkitToolWindowPane pane = await package.FindToolWindowAsync(typeof(ToolWindowMonitor.Pane), 0, createIfNotExists, new CancellationToken()) as ToolkitToolWindowPane;
            if (null != pane)
            {
                ToolWindowMonitorControl toolWindowMonitorControl = pane.Content as ToolWindowMonitorControl;
                if (null != toolWindowMonitorControl)
                {
                    toolWindowMonitorControl.Start();
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
                    toolWindowMonitorControl.Stop();
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
                    toolWindowMonitorControl.Stop();
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

        public static readonly string[] ExcludeProjects = new string[]
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
            public string LinkerPath_ = string.Empty;
            public VSFastBuildCommon.VSEnvironment vsEnvironment_;
            public StringBuilder stringBuilder_;
            public StringBuilder optionBuilder_;
            public List<string> targets_;
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

        public static string ByteArrayToHexString(byte[] bytes)
        {
            return string.Concat(Array.ConvertAll(bytes, x => x.ToString("X2")));
        }

        public static string ByteArrayToHexStringHalf(byte[] bytes)
        {
            Span<byte> span = bytes.AsSpan().Slice(0, bytes.Length/2);
            StringBuilder builder = new StringBuilder();
            foreach(byte b in span)
            {
                builder.Append(b.ToString("X2"));
            }
            return builder.ToString();
        }

        public static string ChopLastFileSeparator(string path)
        {
            if (path.Length <= 0)
            {
                return path;
            }
            if (path[path.Length-1] == '/' || path[path.Length-1] == '\\')
            {
                return path.Substring(0, path.Length-1);
            }
            return path;
        }

        public static string GetFirstPath(string path)
        {
            string? first_path = path.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).First();
            return string.IsNullOrEmpty(first_path) ? path : first_path.Trim();
        }

        private static bool CheckRebuild(string fbuildPath, List<VSFastProject> vsFastProjects, out string hash)
        {
            hash = string.Empty;
            MurmurHash.MurmurHash3.State state = new MurmurHash.MurmurHash3.State();

            foreach (VSFastProject vsFastProject in vsFastProjects)
            {
                try
                {
                    using(System.IO.FileStream fileStream = System.IO.File.OpenRead(vsFastProject.project_.FullName))
                    {
                        MurmurHash.MurmurHash3.Update(state, fileStream);
                    }
                }
                catch
                {
                }
            }
            hash = "//" + ByteArrayToHexString(MurmurHash.MurmurHash3.ComputeHash(MurmurHash.MurmurHash3.Finalize(state)));
            if (!System.IO.File.Exists(fbuildPath))
            {
                return true;
            }
            try
            {
                string line = System.IO.File.ReadLines(fbuildPath).First<string>();
                return line != hash;
            }
            catch
            {
                return true;
            }
        }

        public static string GetVSMainVersion(string version)
        {
            int period = version.IndexOf(".");
            return period<0? version : version.Substring(0, period);
        }

        public static async Task<bool> BuildForSolutionAsync(VSFastBuildVSIXPackage package, List<EnvDTE.Project> projects, string bffpath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            System.Diagnostics.Debug.Assert(null != package);
            System.Diagnostics.Debug.Assert(null != projects);

            BuildContext buildContext = new BuildContext();
            buildContext.stringBuilder_ = new StringBuilder(1024);
            buildContext.optionBuilder_ = new StringBuilder(1024);
            buildContext.targets_ = new List<string>(16);

            VCProject vcProject = projects[0].Object as VCProject;
            VCConfiguration activeConfig = vcProject.ActiveConfiguration;

            //string VCIDEInstallDir = activeConfig.Evaluate("$(VCIDEInstallDir)");
            //string VCINSTALLDIR = activeConfig.Evaluate("$(VCINSTALLDIR)");
            //string VCToolsInstallDir = activeConfig.Evaluate("$(VCToolsInstallDir)");
            //string VCToolsVersion = activeConfig.Evaluate("$(VCToolsVersion)");
            string VisualStudioVersion = activeConfig.Evaluate("$(VisualStudioVersion)");
            //string VSINSTALLDIR = activeConfig.Evaluate("$(VSINSTALLDIR)");
            string WindowsSDKVersion = activeConfig.Evaluate("$(SDKVersion)");
            //string WindowsSDK_ExecutablePath_x64 = activeConfig.Evaluate("$(WindowsSDK_ExecutablePath_x64)");
            //string WindowsSDK_ExecutablePath_x86 = activeConfig.Evaluate("$(WindowsSDK_ExecutablePath_x86)");

            buildContext.vsEnvironment_ = VSFastBuildCommon.VSEnvironment.Create(GetVSMainVersion(VisualStudioVersion), WindowsSDKVersion);

            List<Tuple<string, string>> vcEnvironments = GetVCEnvironments(buildContext.vsEnvironment_.ToolsInstall);

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
                return false;
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
            string hash = string.Empty;
            if(!CheckRebuild(bffpath, vSFastProjects, out hash))
            {
                return true;
            }
            StringBuilder stringBuilder = buildContext.stringBuilder_;

            stringBuilder.AppendLine(hash);

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
                stringBuilder.AppendLine(".LocalEnv =");
                stringBuilder.AppendLine("{");
                for (int i = 0; i < vcEnvironments.Count; ++i)
                {
                    stringBuilder.AppendFormat("  '{0}={1}'", vcEnvironments[i].Item1, vcEnvironments[i].Item2);
                    if (i != (vcEnvironments.Count - 1))
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
            stringBuilder.AppendLine($"  .Executable = '{buildContext.VC_ExecutablePath_}/cl.exe'");
            stringBuilder.AppendLine("  .CompilerFamily = 'msvc'");
            stringBuilder.AppendLine("  .Environment = .LocalEnv");
            stringBuilder.AppendLine("  .ExtraFiles =");
            stringBuilder.AppendLine("  {");
            stringBuilder.AppendLine("    '$Root$/c1.dll',");
            stringBuilder.AppendLine("    '$Root$/c1xx.dll',");
            stringBuilder.AppendLine("    '$Root$/c2.dll',");

            if (System.IO.File.Exists(buildContext.VC_ExecutablePath_ + "/1041/clui.dll")) //Check English first...
            {
                stringBuilder.AppendLine("    '$Root$/1041/clui.dll',");
            }
            else
            {
                IEnumerable<string> numericDirectories = System.IO.Directory.GetDirectories(buildContext.VC_ExecutablePath_).Where(d => System.IO.Path.GetFileName(d).All(char.IsDigit));
                IEnumerable<string> cluiDirectories = numericDirectories.Where(d => System.IO.Directory.GetFiles(d, "clui.dll").Any());
                if (cluiDirectories.Any())
                {
                    stringBuilder.AppendLine($"    '$Root$/{System.IO.Path.GetFileName(cluiDirectories.First())}/clui.dll,'");
                }
            }

            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "msobj*.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "mspdb*.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "mspft*.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "msvcp*.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "tbbmalloc.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "vcmeta.dll");
            AddExtraDlls(stringBuilder, buildContext.VC_ExecutablePath_, "vcruntime*.dll");
            stringBuilder.AppendLine("    '$Root$/1033/clui.dll',");
            stringBuilder.AppendLine("    '$Root$/1033/mspft140ui.dll'");

            //stringBuilder.AppendLine("    '$Root$/mspdbsrv.exe'");
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
                stringBuilder.AppendLine($"  .Executable = '{buildContext.WindowsSDK_ExecutablePath_}/rc.exe'");
                stringBuilder.AppendLine("  .CompilerFamily = 'custom'");
                stringBuilder.AppendLine("  .Environment = .LocalEnv");
                stringBuilder.AppendLine("}");
                stringBuilder.AppendLine(".Compiler_RC = 'Compiler_RC'");
            }

            // Librarian
            {
                buildContext.LibrarianPath_ = $"{buildContext.VC_ExecutablePath_}/lib.exe";
            }

            // Linker
            {
                buildContext.LinkerPath_ = $"{buildContext.VC_ExecutablePath_}/link.exe";
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
            // Clean
            if(0<buildContext.targets_.Count){
                string fbuildname = System.IO.Path.GetFileNameWithoutExtension(bffpath);
                string fbuilddir = System.IO.Path.GetDirectoryName(bffpath);
                string cleanname = $"{fbuildname}_clean.bat";
                stringBuilder.AppendLine("Exec('clean')");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine("  .ExecExecutable = 'C:/Windows/System32/cmd.exe'");
                stringBuilder.AppendLine("  .ExecArguments = '/C \"%1\"'");
                stringBuilder.AppendLine($"  .ExecWorkingDir = '{fbuilddir}'");
                stringBuilder.AppendLine("  .ExecInput =");
                stringBuilder.AppendLine("  {");
                stringBuilder.AppendLine($"    '{cleanname}'");
                stringBuilder.AppendLine("  }");
                stringBuilder.AppendLine("  .ExecUseStdOutAsOutput = true");
                stringBuilder.AppendLine("  .ExecAlwaysShowOutput = true");
                stringBuilder.AppendLine($"  .ExecOutput = '{fbuildname}_clean_out'");
                stringBuilder.AppendLine("  .ExecAlways = true");
                stringBuilder.AppendLine("}");

                StringBuilder cleanBuilder = buildContext.optionBuilder_.Clear();
                foreach (string target in buildContext.targets_)
                {
                    string cleanTarget = target.Replace('/', '\\');
                    cleanBuilder.AppendLine($"DEL /F /Q {cleanTarget}");
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(fbuilddir, cleanname), cleanBuilder.ToString());
            }
            System.IO.File.WriteAllText(bffpath, stringBuilder.ToString());
            return true;
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
                //Log.OutputDebugLine($"{metaData.Name} = {metaData.EvaluatedValue}");
                if (propertiesToSkip.Contains(metaData.Name))
                {
                    continue;
                }
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

        private static bool GetPropertyBool(ProjectProperty property)
        {
            if(null == property)
            {
                return false;
            }
            string value = property.EvaluatedValue;
            bool result = false;
            return bool.TryParse(value, out result)? result : false;
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

            #if false
            foreach (var property in buildProject.AllEvaluatedProperties)
            {
                Log.OutputDebugLine($"{property.Name} = {property.EvaluatedValue}");
            }
            string wholeProgramOptimization = buildProject.GetProperty("WholeProgramOptimization").EvaluatedValue;
            string wholeProgramOptimizationAvailabilityTrue = buildProject.GetProperty("WholeProgramOptimizationAvailabilityTrue").EvaluatedValue;
            string wholeProgramOptimizationAvailabilityInstrument = buildProject.GetProperty("WholeProgramOptimizationAvailabilityInstrument").EvaluatedValue;
            string wholeProgramOptimizationAvailabilityOptimize = buildProject.GetProperty("WholeProgramOptimizationAvailabilityOptimize").EvaluatedValue;
            string wholeProgramOptimizationAvailabilityUpdate = buildProject.GetProperty("WholeProgramOptimizationAvailabilityUpdate").EvaluatedValue;
            #endif
            bool linkIncremental = GetPropertyBool(buildProject.GetProperty("LinkIncremental"));

            string targetName = project.project_.Name;
            string rootDir =System.IO.Path.GetDirectoryName(buildProject.FullPath);
            string intDir = buildProject.GetProperty("IntDirFullPath").EvaluatedValue.Replace("\\", "/");
            string compilerPDB = ChopLastFileSeparator(buildProject.GetProperty("IntDirFullPath").EvaluatedValue);
            string linkerPDB = System.IO.Path.Combine(buildProject.GetProperty("OutDirFullPath").EvaluatedValue, $"{targetName}.pdb");

            projectTargets.Add(targetName);
            StringBuilder stringBuilder = buildContext.stringBuilder_;
            stringBuilder.AppendLine($"// {targetName}");
            stringBuilder.AppendLine("{");
            stringBuilder.AppendLine($"  .CompilerPDB = '{compilerPDB}'");
            stringBuilder.AppendLine($"  .LinkerPDB = '{linkerPDB}'");

            ICollection<Microsoft.Build.Evaluation.ProjectItem> compileItems = buildProject.GetItems("ClCompile");

            // Dependencies
            if (0 < project.dependencies_.Count)
            {
                stringBuilder.AppendLine($"Alias('{targetName}_deps')");
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
            // Precompile Header
            PrecompiledHeaderInfo precompiledHeaderInfo = new PrecompiledHeaderInfo();
            foreach (Microsoft.Build.Evaluation.ProjectItem item in compileItems)
            {
                if (!IsBuildTarget(item))
                {
                    continue;
                }
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
                        string pchCompilerOptions = GenerateTaskCommandLine(task, new string[] { "ObjectFileName", "AssemblerListingLocation", "ProgramDataBaseFileName", "XMLDocumentationFileName", "DiagnosticsFormat"}, item.Metadata) + " /FS";
                        pchCompilerOptions = pchCompilerOptions.Replace("  ", " ").Replace("\\", "/").Replace("//", "/").Replace("/D ", "/D");
                        precompiledHeaderInfo.PCHOptions_ = $"\"%1\" /Fo\"%3\" {pchCompilerOptions} /Fd$CompilerPDB$";
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
                    string resourceCompilerOptions = GenerateTaskCommandLine(task, new string[] { "ResourceOutputFileName", "DesigntimePreprocessorDefinitions", "ProgramDataBaseFileName", "XMLDocumentationFileName", "DiagnosticsFormat" }, item.Metadata);
                    resourceCompilerOptions = resourceCompilerOptions.Replace("\\", "/").Replace("//", "/").Replace("/TP", string.Empty).Replace("/TC", string.Empty).Replace("/D ", "/D");
                    string formattedCompilerOptions = $"{resourceCompilerOptions} /fo\"%2\" \"%1\"";
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
                            stringBuilder.AppendLine($"    '{targetName}_deps'");
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
                            string targetPath = System.IO.Path.Combine(intDir, System.IO.Path.GetFileName(item.ComiplerInputFiles[j]));
                            buildContext.targets_.Add($"{targetPath}.res");
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
                string[] propertiesToSkip = new string[] { "ObjectFileName", "AssemblerListingLocation", "ProgramDataBaseFileName", "XMLDocumentationFileName", "DiagnosticsFormat" };
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
                    string tempCompilerOptions = GenerateTaskCommandLine(task, propertiesToSkip, item.Metadata) + " /FS";
                    StringBuilder optionBuilder = buildContext.optionBuilder_.Clear();
                    optionBuilder.Append("\"%1\" /Fo\"%2\" ");
                    optionBuilder.Append(tempCompilerOptions);
                    optionBuilder.Append(" /Fd$CompilerPDB$");
                    optionBuilder = optionBuilder.Replace("\\", "/").Replace("//", "/").Replace("/D ", "/D").Replace("/TP", string.Empty).Replace("/TC", string.Empty);
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
                    stringBuilder.AppendLine($"Unity('{targetName}_unity{i}')");
                    stringBuilder.AppendLine("{");
                    stringBuilder.AppendLine($"  .UnityInputFiles = {{{string.Join(",", fbCompileItem[i].ComiplerInputFiles.ConvertAll(el => string.Format("'{0}'", el)).ToArray())}}}");
                    stringBuilder.AppendLine($"  .UnityOutputPath = '{intDir}'");
                    stringBuilder.AppendLine($"  .UnityNumFiles = {1 + fbCompileItem[i].ComiplerInputFiles.Count / 10}");
                    stringBuilder.AppendLine("}");
                    usedUnity = true;
                }

                string objTargetName = $"{targetName}_objs{i}";
                objTargets.Add(objTargetName);
                stringBuilder.AppendLine($"ObjectList('{objTargetName}')");
                stringBuilder.AppendLine("{");
                if (0 < project.dependencies_.Count)
                {
                    stringBuilder.AppendLine("  .PreBuildDependencies =");
                    stringBuilder.AppendLine("  {");
                    stringBuilder.AppendLine($"    '{targetName}_deps'");
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
                stringBuilder.AppendLine("  .CompilerOutputExtension = '.obj'");
                stringBuilder.AppendLine("  .CompilerOutputKeepBaseExtension = true");
                if (usedUnity)
                {
                    stringBuilder.AppendLine($"  .CompilerInputUnity = {{ '{targetName}_unity{i}' }}");
                }
                else
                {
                    string str = string.Join(",", fbCompileItem[i].ComiplerInputFiles.ConvertAll(x => string.Format("'{0}'", x)).ToArray());
                    stringBuilder.AppendLine($"  .CompilerInputFiles = {{ {str} }}");
                    foreach (string file in fbCompileItem[i].ComiplerInputFiles)
                    {
                        string targetPath = System.IO.Path.Combine(intDir, System.IO.Path.GetFileName(file));
                        buildContext.targets_.Add($"{targetPath}.obj");
                    }
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
                            stringBuilder.AppendLine($"    '{targetName}_deps'");
                            stringBuilder.AppendLine("  }");
                        }
                        stringBuilder.AppendLine("  .Environment = .LocalEnv");
                        stringBuilder.AppendLine($"  .Linker = '{buildContext.LinkerPath_}'");
                        ProjectItemDefinition linkDefinitions = buildProject.ItemDefinitions["Link"];
                        string outputFile = linkDefinitions.GetMetadataValue("OutputFile");
                        string outputDirectory = System.IO.Path.GetDirectoryName(outputFile);
                        if (!System.IO.Directory.Exists(outputDirectory))
                        {
                            System.IO.Directory.CreateDirectory(outputDirectory);
                        }
                        //foreach(var metadata in linkDefinitions.Metadata)
                        //{
                        //    Log.OutputDebugLine(metadata.Name + ": " + metadata.EvaluatedValue);
                        //}
                        string profileGuidedDatabase = linkDefinitions.GetMetadataValue("ProfileGuidedDatabase");
                        string ilkDBFile = System.IO.Path.Combine(rootDir, linkDefinitions.GetMetadataValue("IncrementalLinkDatabaseFile"));
                        string ltcgObjFile = System.IO.Path.Combine(rootDir, linkDefinitions.GetMetadataValue("LinkTimeCodeGenerationObjectFile"));
                        string ltcgOptim = linkDefinitions.GetMetadataValue("LinkTimeCodeGeneration");
                        ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.Link"));
                        string linkerOptions = GenerateTaskCommandLine(task, new string[] { "OutputFile", "ProfileGuidedDatabase", "ProgramDataBaseFileName", "XMLDocumentationFileName", "DiagnosticsFormat", "LinkTimeCodeGenerationObjectFile", "IncrementalLinkDatabaseFile" }, linkDefinitions.Metadata);
                        linkerOptions = linkerOptions.Replace("'", "^'");
                        StringBuilder optionBuilder = buildContext.optionBuilder_.Clear();
                        optionBuilder.Append("\"%1\" /OUT:\"%2\" /pdb:$LinkerPDB$");
                        if (linkIncremental)
                        {
                            optionBuilder.Append($" /INCREMENTAL /ILK:\"{ilkDBFile}\"");
                        }
                        if (!string.IsNullOrEmpty(ltcgOptim))
                        {
                            switch (ltcgOptim)
                            {
                                case "UseFastLinkTimeCodeGeneration":
                                    optionBuilder.Append($" /LTCG:incremental /LTCGOUT:\"{ltcgObjFile}\"");
                                    break;
                                case "PGInstrument":
                                    optionBuilder.Append($" /LTCG:PGInstrument /LTCGOUT:\"{ltcgObjFile}\"");
                                    break;
                                case "PGOptimize":
                                    optionBuilder.Append($" /LTCG:PGOptimize /LTCGOUT:\"{ltcgObjFile}\"");
                                    break;
                                case "PGUpdate":
                                    optionBuilder.Append($" /LTCG:PGUpdate /LTCGOUT:\"{ltcgObjFile}\"");
                                    break;
                            }
                        }
                        optionBuilder.Append($" {linkerOptions}");

                        stringBuilder.AppendLine($"  .LinkerOptions = '{optionBuilder.ToString()}'");

                        stringBuilder.AppendLine($"  .LinkerOptions = '\"%1\" /OUT:\"%2\" /pdb:$LinkerPDB$ {linkerOptions}'");
                        stringBuilder.AppendLine($"  .LinkerOutput = '{outputFile}'");

                        stringBuilder.AppendLine("  .Libraries =");
                        stringBuilder.AppendLine("  {");
                        addStringList(stringBuilder, objTargets, "    ");
                        stringBuilder.AppendLine("  }");
                        stringBuilder.AppendLine("  .LinkerType = 'auto'");
                        stringBuilder.AppendLine("  .LinkerLinkObjects = false");
                        stringBuilder.AppendLine("}");
                        buildContext.targets_.Add(outputFile);
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
                        string ltcgOptim = libDefinitions.GetMetadataValue("LinkTimeCodeGeneration");
                        ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.LIB"));
                        string linkerOptions = GenerateTaskCommandLine(task, new string[] { "OutputFile", "ProgramDataBaseFileName", "XMLDocumentationFileName", "DiagnosticsFormat" }, libDefinitions.Metadata);
                        StringBuilder optionBuilder = buildContext.optionBuilder_.Clear();
                        string outputFile = libDefinitions.GetMetadataValue("OutputFile");
                        optionBuilder.Append("\"%1\" /OUT:\"%2\"");
                        if (!string.IsNullOrEmpty(ltcgOptim) && ltcgOptim == "true")
                        {
                            optionBuilder.Append(" /LTCG");
                        }
                        optionBuilder.Append($" {linkerOptions}");
                        stringBuilder.AppendLine($"  .LibrarianOptions = '{optionBuilder.ToString()}'");
                        stringBuilder.AppendLine($"  .LibrarianOutput = '{outputFile}'");

                        stringBuilder.AppendLine("  .LibrarianAdditionalInputs =");
                        stringBuilder.AppendLine("  {");
                        addStringList(stringBuilder, objTargets, "    ");
                        stringBuilder.AppendLine("  }");
                        stringBuilder.AppendLine("  .LinkerType = 'auto'");
                        stringBuilder.AppendLine("}");
                        buildContext.targets_.Add(outputFile);
                    }
                    break;
                case BuildType.DynamicLib:
                    {
                        stringBuilder.AppendLine($"DLL('{targetName}')");
                        stringBuilder.AppendLine("{");
                        stringBuilder.AppendLine("  .Environment = .LocalEnv");
                        stringBuilder.AppendLine($"  .Linker = '{buildContext.LinkerPath_}'");
                        ProjectItemDefinition linkDefinitions = buildProject.ItemDefinitions["Link"];
                        string ilkDBFile = System.IO.Path.Combine(rootDir, linkDefinitions.GetMetadataValue("IncrementalLinkDatabaseFile"));
                        string ltcgObjFile = System.IO.Path.Combine(rootDir, linkDefinitions.GetMetadataValue("LinkTimeCodeGenerationObjectFile"));
                        string ltcgOptim = linkDefinitions.GetMetadataValue("LinkTimeCodeGeneration");
                        string outputFile = linkDefinitions.GetMetadataValue("OutputFile");
                        string outputDirectory = System.IO.Path.GetDirectoryName(outputFile);
                        if (!System.IO.Directory.Exists(outputDirectory))
                        {
                            System.IO.Directory.CreateDirectory(outputDirectory);
                        }
                        ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.Link"));
                        string linkerOptions = GenerateTaskCommandLine(task, new string[] { "OutputFile", "ProfileGuidedDatabase", "ProgramDataBaseFileName", "XMLDocumentationFileName", "DiagnosticsFormat", "LinkTimeCodeGenerationObjectFile", "IncrementalLinkDatabaseFile" }, linkDefinitions.Metadata);
                        linkerOptions = linkerOptions.Replace("'", "^'");
                        StringBuilder optionBuilder = buildContext.optionBuilder_.Clear();
                        optionBuilder.Append("\"%1\" /OUT:\"%2\" /pdb:$LinkerPDB$");
                        bool ltcg = false;
                        if (!string.IsNullOrEmpty(ltcgOptim))
                        {
                            switch (ltcgOptim)
                            {
                                case "UseFastLinkTimeCodeGeneration":
                                    ltcg = true;
                                    optionBuilder.Append($" /LTCG:incremental /LTCGOUT:\"{ltcgObjFile}\"");
                                    break;
                                case "PGInstrument":
                                    ltcg = true;
                                    optionBuilder.Append($" /LTCG:PGInstrument /LTCGOUT:\"{ltcgObjFile}\"");
                                    break;
                                case "PGOptimization":
                                    ltcg = true;
                                    optionBuilder.Append($" /LTCG:PGOptimize /LTCGOUT:\"{ltcgObjFile}\"");
                                    break;
                                case "PGUpdate":
                                    ltcg = true;
                                    optionBuilder.Append($" /LTCG:PGUpdate /LTCGOUT:\"{ltcgObjFile}\"");
                                    break;
                            }
                        }
                        if (!ltcg && linkIncremental)
                        {
                            optionBuilder.Append($" /INCREMENTAL /ILK:\"{ilkDBFile}\"");
                        }
                        optionBuilder.Append($" {linkerOptions}");

                        stringBuilder.AppendLine($"  .LinkerOptions = '{optionBuilder.ToString()}'");
                        stringBuilder.AppendLine($"  .LinkerOutput = '{outputFile}'");

                        stringBuilder.AppendLine("  .Libraries =");
                        stringBuilder.AppendLine("  {");
                        addStringList(stringBuilder, objTargets, "    ");
                        stringBuilder.AppendLine("  }");
                        stringBuilder.AppendLine("  .LinkerType = 'auto'");
                        stringBuilder.AppendLine("  .LinkerLinkObjects = false");
                        stringBuilder.AppendLine("}");
                        buildContext.targets_.Add(outputFile);
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
            stringBuilder.AppendLine("}");
        }

        public static System.Diagnostics.Process ExecuteBffFile(string bffpath, string projectPath, string fbuildPath, string fbuldArgs)
        {
            string projectDir = System.IO.Path.GetDirectoryName(projectPath);
            try
            {
                System.Diagnostics.Process FBProcess = new System.Diagnostics.Process();
                FBProcess.StartInfo.FileName = fbuildPath;
                FBProcess.StartInfo.Arguments = "-config \"" + bffpath + "\" " + fbuldArgs;
                FBProcess.StartInfo.RedirectStandardOutput = true;
                FBProcess.StartInfo.RedirectStandardError = true;
                FBProcess.StartInfo.CreateNoWindow = true;
                FBProcess.StartInfo.UseShellExecute = false;
                FBProcess.StartInfo.WorkingDirectory = projectDir;
                FBProcess.StartInfo.StandardOutputEncoding = Console.OutputEncoding;
                return FBProcess;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public static List<Tuple<string, string>> GetVCEnvironments(string toolsInstall)
        {
            string vcvarsall = System.IO.Path.Combine(toolsInstall, "VC", "Auxiliary", "Build", "vcvarsall.bat");
            string cmd = "call \"" + vcvarsall + "\" x64 && set";

            ProcessStartInfo processInfo;
            processInfo = new ProcessStartInfo("cmd.exe", "/c " + cmd);
            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardError = false;
            processInfo.RedirectStandardOutput = true;

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(processInfo))
            {
                StringBuilder stringOutput = new StringBuilder();
                process.OutputDataReceived += (sender, args) => { stringOutput.AppendLine(args.Data); };

                process.BeginOutputReadLine();
                process.WaitForExit();

                int exitCode = process.ExitCode;

                List<Tuple<string, string>> environments = new List<Tuple<string, string>>(16);
                if (exitCode != 0)
                {
                    return environments;
                }
                string output = stringOutput.ToString();
                using(StringReader reader = new StringReader(output))
                {
                    while(true){
                        string line = reader.ReadLine();
                        if (null==line)
                        {
                            break;
                        }
                        if (line.Contains("[vcvarsall.bat]"))
                        {
                            break;
                        }
                    }
                    while(true){
                        string line = reader.ReadLine();
                        if (null==line)
                        {
                            break;
                        }
                        string[] splits = line.Split('=');
                        if(null == splits || splits.Length < 2)
                        {
                            continue;
                        }
                        if (string.Compare(splits[0], "PROMPT", true) == 0)
                        {
                            continue;
                        }
                        if (splits[0].StartsWith("EFC_"))
                        {
                            continue;
                        }
                        if (splits[0].StartsWith("_PTVS_PID"))
                        {
                            continue;
                        }
                        if (splits[0].StartsWith("ThreadedWaitDialogDpiContext"))
                        {
                            continue;
                        }
                        if (splits[0].StartsWith("_NO_DEBUG_HEAP"))
                        {
                            continue;
                        }
                        splits[1] = splits[1].Replace("$", "^$");
                        environments.Add(new Tuple<string, string>(splits[0], splits[1]));
                    }
                }
                return environments;
            }
        }
    }
}
