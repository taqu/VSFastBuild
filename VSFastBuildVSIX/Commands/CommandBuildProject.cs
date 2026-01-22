using Blake2Fast;
using EnvDTE;
using EnvDTE80;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework.XamlTypes;
using Microsoft.Build.Utilities;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.VCProjectEngine;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Policy;
using System.Security.RightsManagement;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VSFastBuildCommon;
using VSFastBuildVSIX.Options;
using static VSFastBuildVSIX.CommandBuildProject;

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
                await StopMonitorAsync(package);
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
                if (item is EnvDTE.Project)
                {
                    if (ProjectTypes.WindowsCPlusPlus != item.Project.Kind)
                    {
                        continue;
                    }

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
            await BuildProjectsAsync(package, targets);
            LeaveProcess(package, Command, commandText_);
        }

        public static async Task BuildProjectsAsync(VSFastBuildVSIXPackage package, List<EnvDTE.Project> targets)
        {
            await Log.AddOutputPaneAsync(Log.PaneBuild);
            OptionsPage optionPage = VSFastBuildVSIXPackage.Options;
            string fbuildPath = optionPage.Path;
            string fbuldArgs = optionPage.Arguments;
            if (!fbuldArgs.Contains("-j"))
            {
                int numProcessors = VSFastBuildCommon.SystemEnvironment.GetPhysicalProcessorCount();
                fbuldArgs += $" -j{numProcessors}";
            }

            bool openMonitor = optionPage.OpenMonitor;
            ToolWindowMonitorControl.TruncateLogFile();
            await CommandBuildProject.ResetMonitorAsync(package);
            if (openMonitor)
            {
                await CommandBuildProject.StartMonitorAsync(package, true);
            }

            try
            {
                List<Result> results = new List<Result>();
                List<EnvDTE.Project> tempTargets = new List<EnvDTE.Project>(1);
                List<VSFastProject> vSFastProjects = GetDependencies(package, targets);
                await Log.OutputBuildAsync($"--- VSFastBuild begin building ---");
                foreach (VSFastProject project in vSFastProjects)
                {
                    Result result = await CommandBuildProject.BuildForProjectAsync(package, project);
                    results.Add(result);
                }

                if (!VSFastBuildVSIXPackage.Options.GenOnly)
                {
                    foreach (Result result in results)
                    {
                        await Log.OutputBuildAsync($"--- VSFastBuild begin running {result.bffName_}---");
                        await RunProcessAsync(result, package, fbuildPath, fbuldArgs);
                        await Log.OutputBuildAsync($"--- VSFastBuild end running {result.bffName_}---");
                    }
                }
                await Log.OutputBuildAsync($"--- VSFastBuild end ---");
            }
            catch (Exception ex)
            {
                await Log.OutputDebugAsync(ex.Message);
            }
            if (openMonitor)
            {
                await CommandBuildProject.StopMonitorAsync(package);
            }
        }

        public struct ResultProject
        {
            public string name_;
            public string projectDir_;
            public string intDir_;
            public string configuration_;
            public string platform_;
            public string hash_;
        }

        public struct Result
        {
            public bool success_;
            public string bffPath_;
            public string bffName_;
            public ResultProject project_;
            public string lastbuildstate_;
        }

        public static async Task RunProcessAsync(Result result, VSFastBuildVSIXPackage package, string fbuildPath, string fbuildArgs)
        {
            if (!result.success_)
            {
                return;
            }

            {
                try
                {
                    //string arguments = $"/t /c \"{fbuildPath}\" -config \"{bffpath}\" {result.projects_[i].name_} {fbuldArgs}";
                    string tlogDir = System.IO.Path.Combine(result.project_.intDir_, result.project_.name_ + ".tlog");
                    string arguments = $"-config \"{result.bffPath_}\" {result.project_.name_} {fbuildArgs}";
                    //using (TLogTracker tracker = new TLogTracker(tlogDir, result.tempDir_))
                    {
                        //System.Diagnostics.Process process = CommandBuildProject.CreateProcess(arguments, i, result, tracker.Path);
                        System.Diagnostics.Process process = CommandBuildProject.CreateProcess(fbuildPath, arguments, result.project_.intDir_);
                        if (process.Start())
                        {
                            //tracker.Start();
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();
                            {
                                await process.WaitForExitAsync(package.CancellationToken);
                                string lastbuildstateFile = System.IO.Path.Combine(tlogDir, $"{result.project_.name_}.lastbuildstate");
                                try
                                {
                                    string lastbuildstate = $"{result.lastbuildstate_}\r\n{result.project_.configuration_}|{result.project_.platform_}|{result.project_.projectDir_}|\r\n";
                                    UTF8Encoding encoding = new UTF8Encoding(false);
                                    System.IO.File.WriteAllText(lastbuildstateFile, lastbuildstate, encoding);
                                }
                                catch { }
                            }
                            process.CancelErrorRead();
                            process.CancelOutputRead();
                            //tracker.Save();
                        }
                    }
                }
                catch (Exception ex)
                {
                    await Log.OutputDebugAsync(ex.Message);
                }
            }
        }

        public static async Task RunProcessAsync(VSFastBuildVSIXPackage package, string bffpath)
        {
            OptionsPage optionPage = VSFastBuildVSIXPackage.Options;
            string fbuildPath = optionPage.Path;
            string fbuldArgs = optionPage.Arguments;
            bool openMonitor = optionPage.OpenMonitor;
            string arguments = $"\"{fbuildPath}\" -config \"{bffpath}\" {fbuldArgs}";
            if (!fbuldArgs.Contains("-j"))
            {
                int numProcessors = VSFastBuildCommon.SystemEnvironment.GetPhysicalProcessorCount();
                arguments += $" -j{numProcessors}";
            }
            string workingDir = System.IO.Path.GetDirectoryName(bffpath);

            ToolWindowMonitorControl.TruncateLogFile();
            await CommandBuildProject.ResetMonitorAsync(package);
            if (openMonitor)
            {
                await CommandBuildProject.StartMonitorAsync(package, true);
            }

            try
            {
                System.Diagnostics.Process process = CommandBuildProject.CreateProcess(bffpath, arguments, workingDir);
                if (process.Start())
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync(package.CancellationToken);
                    process.CancelErrorRead();
                    process.CancelOutputRead();
                }
            }
            catch (Exception ex)
            {
                await Log.OutputDebugAsync(ex.Message);
            }
            await CommandBuildProject.StopMonitorAsync(package);
        }
#if false
        public static System.Diagnostics.Process CreateProcess(string arguments, int index, Result result, string tlogDir)
        {
            try
            {
                if (!System.IO.Directory.Exists(tlogDir))
                {
                    System.IO.Directory.CreateDirectory(tlogDir);
                }
                System.Diagnostics.Process FBProcess = new System.Diagnostics.Process();
                FBProcess.StartInfo.FileName = result.tracker_;
                FBProcess.StartInfo.Arguments = $"/i \"{tlogDir}\" {arguments}";
                FBProcess.StartInfo.RedirectStandardOutput = true;
                FBProcess.StartInfo.RedirectStandardError = true;
                FBProcess.StartInfo.CreateNoWindow = true;
                FBProcess.StartInfo.UseShellExecute = false;
                FBProcess.StartInfo.WorkingDirectory = result.projects_[index].projectDir_;
                FBProcess.OutputDataReceived += new DataReceivedEventHandler(FBProcess_OnOutputDataReceived);
                FBProcess.ErrorDataReceived += new DataReceivedEventHandler(FBProcess_OnErrorDataReceived);
                return FBProcess;
            }
            catch (Exception e)
            {
                return null;
            }
        }
#endif

        public static System.Diagnostics.Process CreateProcess(string fbuildPath, string arguments, string workingDir)
        {
            try
            {
                System.Diagnostics.Process FBProcess = new System.Diagnostics.Process();
                FBProcess.StartInfo.FileName = fbuildPath;
                FBProcess.StartInfo.Arguments = arguments;
                FBProcess.StartInfo.RedirectStandardOutput = true;
                FBProcess.StartInfo.RedirectStandardError = true;
                FBProcess.StartInfo.CreateNoWindow = true;
                FBProcess.StartInfo.UseShellExecute = false;
                FBProcess.StartInfo.WorkingDirectory = workingDir;
                FBProcess.OutputDataReceived += new DataReceivedEventHandler(FBProcess_OnOutputDataReceived);
                FBProcess.ErrorDataReceived += new DataReceivedEventHandler(FBProcess_OnErrorDataReceived);
                return FBProcess;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        private static void FBProcess_OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Log.OutputBuildLine(e.Data);
            }
        }

        private static void FBProcess_OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Log.OutputBuildLine(e.Data);
            }
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

        public static async Task StartMonitorAsync(ToolkitPackage package, bool show = false)
        {
            ToolkitToolWindowPane pane;
            if (show)
            {
                pane = await package.ShowToolWindowAsync(typeof(ToolWindowMonitor.Pane), 0, true, new CancellationToken()) as ToolkitToolWindowPane;
            }
            else
            {
                pane = await package.FindToolWindowAsync(typeof(ToolWindowMonitor.Pane), 0, false, new CancellationToken()) as ToolkitToolWindowPane;
            }
            if (null != pane)
            {
                ToolWindowMonitorControl toolWindowMonitorControl = pane.Content as ToolWindowMonitorControl;
                if (null != toolWindowMonitorControl)
                {
                    toolWindowMonitorControl.Start();
                }
            }
        }

        public static async Task StopMonitorAsync(ToolkitPackage package)
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

        public static async Task ResetMonitorAsync(ToolkitPackage package)
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

        public enum ItemType
        {
            CXX,
            Resource,
            MASM,
            HLSL,
            CUDA,
            Custom,
            Num,
        }

        public struct CustomBuildItem
        {
            public List<string> inputs_;
            public string output_;
            public string command_;
            public string arguments_;
            public bool linkOject_;
            public bool outputAsContent_;
        }

        public class FBCompileItem
        {
            public const string Empty = "";

            public ItemType Type => type_;
            public string Options => options_;
            public CustomBuildItem CustomBuildItem => customBuild_;
            public string OutputExtension => outputExtension_;
            public List<string> InputFiles => inputFiles_;

            private ItemType type_;
            private string options_;
            private CustomBuildItem customBuild_;
            private string outputExtension_;
            private List<string> inputFiles_;

            public FBCompileItem(CustomBuildItem customBuild)
            {
                type_ = ItemType.Custom;
                inputFiles_ = null;
                options_ = string.Empty;
                customBuild_ = customBuild;
                outputExtension_ = string.Empty;
            }

            public FBCompileItem(ItemType type, string inputFile, string options, string outputExtension = Empty)
            {
                type_ = type;
                inputFiles_ = new List<string>(16) { inputFile };
                options_ = options;
                customBuild_ = new CustomBuildItem();
                outputExtension_ = outputExtension;
            }

            public bool AddIfMatches(ItemType type, string inputFile, string options, string outputExtension = Empty)
            {
                if (type_ == ItemType.Custom)
                {
                    return false;
                }
                if (type_ == type && options_ == options && outputExtension_ == outputExtension)
                {
                    inputFiles_.Add(inputFile);
                    return true;
                }
                return false;
            }
        }

        public struct PrecompiledHeaderInfo
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

        public class VSFastProject
        {
            public List<FBCompileItem> CompileItems => compileItems_;
            public BitArray ExistsFlags => existsFlags_;

            public EnvDTE.Project project_;
            public Microsoft.Build.Evaluation.Project buildProject_;
            public List<string> dependNames_;
            public List<VSFastProject> dependencies_;

            public string configType_;
            public string targetName_;
            public string uniqueName_;
            public string rootDir_;
            public string intDir_;
            public string compilerPDB_;
            public string linkerPDB_;
            public string postDepend_;
            public PrecompiledHeaderInfo precompiledHeaderInfo_;
            private BitArray existsFlags_ = new BitArray((int)ItemType.Num);
            private List<FBCompileItem> compileItems_ = new List<FBCompileItem>(16);
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
                foreach (string dependency in project.project_.dependNames_)
                {
                    SortProject proj = projects.Find((x) => x.project_.uniqueName_ == dependency);
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
                    Type typeFXC = CPPTasksAssembly.GetType("Microsoft.Build.FXCTask.FXC");
                    Type typeCustom = CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.CustomBuild");
                    if (null != typeCL && null != typeRC && null != typeLink && null != typeLIB && null != typeFXC && null != typeCustom)
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
            public string CUDAPath_ = string.Empty;
            public VSFastBuildCommon.VSEnvironment vsEnvironment_;
            public Dictionary<string, string> environments_;
            public List<string> pathes_;
            public StringBuilder stringBuilder_;
            public StringBuilder optionBuilder_;
            public string configuration_;
            public string platform_;
            public List<string> targets_;
            public IDictionary<string, string> globalProperties_;
            public string envName_ = string.Empty;
            public string compilerName_ = string.Empty;
            public string compilerDummyName_ = string.Empty;
            public string rcName_ = string.Empty;
            public string masmName_ = string.Empty;
            public string cudaName_ = string.Empty;
            public string dxcName_ = string.Empty;
        }


        public static string ByteArrayToHexString(byte[] bytes)
        {
            return string.Concat(Array.ConvertAll(bytes, x => x.ToString("X2")));
        }

        public static string ByteArrayToHexStringHalf(byte[] bytes)
        {
            Span<byte> span = bytes.AsSpan().Slice(0, bytes.Length / 2);
            StringBuilder builder = new StringBuilder();
            foreach (byte b in span)
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
            if (path[path.Length - 1] == '/' || path[path.Length - 1] == '\\')
            {
                return path.Substring(0, path.Length - 1);
            }
            return path;
        }

        public static string GetFirstPath(string path)
        {
            string? first_path = path.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).First();
            return string.IsNullOrEmpty(first_path) ? path : first_path.Trim();
        }

        private static bool CheckRebuild(string fbuildPath, VSFastProject vsFastProject, out string hash)
        {
            hash = string.Empty;
            Blake2Fast.Implementation.Blake2bHashState hasher = Blake2b.CreateIncrementalHasher();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                using (System.IO.FileStream fileStream = System.IO.File.OpenRead(vsFastProject.project_.FullName))
                {
                    int bytesRead;
                    while (0 < (bytesRead = fileStream.Read(buffer, 0, buffer.Length)))
                    {
                        hasher.Update(buffer.AsSpan(0, bytesRead));
                    }
                }
            }
            catch
            {
                return true;
            }
            ArrayPool<byte>.Shared.Return(buffer);
            hash = ByteArrayToHexString(hasher.Finish());
            if (!System.IO.File.Exists(fbuildPath))
            {
                return true;
            }
            try
            {
                string line = System.IO.File.ReadLines(fbuildPath).First<string>();
                line = line.TrimPrefix("//");
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
            return period < 0 ? version : version.Substring(0, period);
        }

        private static void AddPathList(List<string> paths, string arg)
        {
            string[] pathList = arg.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string path in pathList)
            {
                if (paths.Contains(path))
                {
                    continue;
                }
                paths.Add(path);
            }
        }

        private static void AddPathList(List<string> paths, string rootDir, string arg)
        {
            string[] pathList = arg.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string path in pathList)
            {
                string fullpath = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootDir, path));
                if (paths.Contains(fullpath))
                {
                    continue;
                }
                paths.Add(fullpath);
            }
        }

        public static List<VSFastProject> GetDependencies(VSFastBuildVSIXPackage package, List<EnvDTE.Project> projects)
        {
            SolutionBuild2 solutionBuild = package.DTE.Solution.SolutionBuild as SolutionBuild2;
            SolutionConfiguration2 solutionConfiguration = solutionBuild.ActiveConfiguration as SolutionConfiguration2;
            Dictionary<string, string> globalProperties = new Dictionary<string, string>()
            {
                { "Configuration", solutionConfiguration.Name },
                { "Platform", solutionConfiguration.PlatformName }
            };
            List<VSFastProject> vSFastProjects = new List<VSFastProject>(projects.Capacity);
            ProjectCollection projectCollection = new ProjectCollection();
            for (int i = 0; i < projects.Count; ++i)
            {
                BuildDependency buildDependency = solutionBuild.BuildDependencies.Item(projects[i].UniqueName);
                if (null == buildDependency)
                {
                    continue;
                }
                VSFastProject vSFastProject = new() { targetName_ = projects[i].Name, project_ = projects[i], dependNames_ = new List<string>(), dependencies_ = new List<VSFastProject>(), uniqueName_ = projects[i].UniqueName, postDepend_ = projects[i].Name };

                object[] requiredProjects = buildDependency.RequiredProjects as object[];
                foreach (object item in requiredProjects)
                {
                    EnvDTE.Project dependentProject = item as EnvDTE.Project;
                    if (null == dependentProject)
                    {
                        continue;
                    }
                    bool found = false;
                    string uniqueName = dependentProject.UniqueName;
                    foreach (string name in vSFastProject.dependNames_)
                    {
                        if (name == uniqueName)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        foreach (string exclude in ExcludeProjects)
                        {
                            if (uniqueName.EndsWith(exclude))
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    if (!found)
                    {
                        vSFastProject.dependNames_.Add(uniqueName);
                        if (!projects.Any(x => x.UniqueName == uniqueName))
                        {
                            projects.Add(dependentProject);
                        }
                    }
                }
                vSFastProject.buildProject_ = new Microsoft.Build.Evaluation.Project(vSFastProject.project_.FullName, globalProperties, null, projectCollection);
                vSFastProjects.Add(vSFastProject);
            }
            vSFastProjects.Sort((x0, x1) =>
            {
                return string.Compare(x0.targetName_, x1.targetName_);
            });
            TopologicalSort(vSFastProjects);
            projectCollection.UnloadAllProjects();
            return vSFastProjects;
        }

        public static string GetProjectBFFRelativePath(VSFastBuildVSIXPackage package, Result result)
        {
            string bffPath = result.bffPath_;
            string solutionDir = System.IO.Path.GetDirectoryName(package.DTE.Solution.FullName).TrimEnd('\\', '/');

            Uri solutionUri = new Uri(solutionDir);
            Uri relativeUri = solutionUri.MakeRelativeUri(new Uri(bffPath));
            return relativeUri.OriginalString;
        }

        public static async Task<Result> CreateSolutionBFF(VSFastBuildVSIXPackage package, List<Result> results, StringBuilder stringBuilder)
        {
            SolutionBuild2 solutionBuild = package.DTE.Solution.SolutionBuild as SolutionBuild2;
            SolutionConfiguration2 solutionConfiguration = solutionBuild.ActiveConfiguration as SolutionConfiguration2;
            string rootDirectory = System.IO.Path.GetDirectoryName(package.DTE.Solution.FullName);

            Result result = new Result();
            result.bffName_ = string.Format("fbuild_{0}_{1}.bff", solutionConfiguration.Name, solutionConfiguration.PlatformName);
            result.bffPath_ = System.IO.Path.Combine(rootDirectory, result.bffName_);
            result.lastbuildstate_ = string.Empty;

            Blake2Fast.Implementation.Blake2bHashState hasher = Blake2b.CreateIncrementalHasher();
            for (int i = 0; i < results.Count; ++i)
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(results[i].project_.hash_);
                hasher.Update(bytes);
            }
            result.project_.hash_ = ByteArrayToHexString(hasher.Finish());
            if (System.IO.File.Exists(result.bffPath_))
            {
                try
                {
                    string line = System.IO.File.ReadLines(result.bffPath_).First<string>();
                    line = line.TrimPrefix("//");
                    if (line == result.project_.hash_)
                    {
                        return result;
                    }
                }
                catch
                {
                }
            }
            stringBuilder.Clear();
            stringBuilder.AppendLine($"//{result.project_.hash_}");
            foreach (Result project in results)
            {
                stringBuilder.AppendLine($"#include \"{GetProjectBFFRelativePath(package, project)}\"");
            }
            stringBuilder.AppendLine("{");
            stringBuilder.AppendLine("  Alias('all')");
            stringBuilder.AppendLine("  {");
            stringBuilder.AppendLine("    .Targets =");
            stringBuilder.AppendLine("    {");
            foreach (Result project in results)
            {
                stringBuilder.AppendLine($"      '{project.project_.name_}',");
            }
            stringBuilder.AppendLine("    }");
            stringBuilder.AppendLine("  }");
            stringBuilder.AppendLine("}");

            try
            {
                System.IO.File.WriteAllText(result.bffPath_, stringBuilder.ToString());
            }
            catch
            {
            }
            return result;
        }

        public static string GetCommonFilePath(VSFastBuildVSIXPackage package)
        {
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(package.DTE.Solution.FullName).TrimEnd('\\', '/'), "fbuild_common.bff");
        }

        public static void CreateCommonBFF(VSFastBuildVSIXPackage package, StringBuilder stringBuilder)
        {
            string commonFile = GetCommonFilePath(package);
            if (System.IO.File.Exists(commonFile))
            {
                return;
            }
            stringBuilder.Clear();
        }

        public static string GetCommonRelativePath(VSFastBuildVSIXPackage package, VSFastProject vsFastProject)
        {
            string projectDir = System.IO.Path.GetDirectoryName(vsFastProject.project_.FullName).TrimEnd('\\', '/');
            string commonFile = GetCommonFilePath(package);

            Uri projectUri = new Uri(projectDir);
            Uri commonUri = projectUri.MakeRelativeUri(new Uri(commonFile));
            return commonUri.OriginalString;
        }

        public static async Task<Result> BuildForProjectAsync(VSFastBuildVSIXPackage package, VSFastProject vsFastProject)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            System.Diagnostics.Debug.Assert(null != package);
            System.Diagnostics.Debug.Assert(null != vsFastProject);

            BuildContext buildContext = new BuildContext();
            buildContext.stringBuilder_ = new StringBuilder(1024);
            buildContext.optionBuilder_ = new StringBuilder(1024);
            buildContext.targets_ = new List<string>(16);

            VCProject vcProject = vsFastProject.project_.Object as VCProject;
            VCConfiguration activeConfig = vcProject.ActiveConfiguration;

            //string VCIDEInstallDir = activeConfig.Evaluate("$(VCIDEInstallDir)");
            //string VCINSTALLDIR = activeConfig.Evaluate("$(VCINSTALLDIR)");
            //string VCToolsInstallDir = activeConfig.Evaluate("$(VCToolsInstallDir)");
            string VCToolsVersion = activeConfig.Evaluate("$(VCToolsVersion)");
            string VisualStudioVersion = activeConfig.Evaluate("$(VisualStudioVersion)");
            string PlatformToolSet = activeConfig.Evaluate("$(PlatformToolSet)");
            string VCToolArchitecture = activeConfig.Evaluate("$(VCToolArchitecture)");
            //string VSINSTALLDIR = activeConfig.Evaluate("$(VSINSTALLDIR)");
            string WindowsSDKVersion = activeConfig.Evaluate("$(SDKVersion)");
            //string WindowsSDK_ExecutablePath_x64 = activeConfig.Evaluate("$(WindowsSDK_ExecutablePath_x64)");
            //string WindowsSDK_ExecutablePath_x86 = activeConfig.Evaluate("$(WindowsSDK_ExecutablePath_x86)");

            buildContext.vsEnvironment_ = VSFastBuildCommon.VSEnvironment.Create(GetVSMainVersion(VisualStudioVersion), WindowsSDKVersion);

            buildContext.environments_ = GetVCEnvironments(buildContext.vsEnvironment_.ToolsInstall);

            buildContext.VCTargetsPath_ = activeConfig.Evaluate("$(VCTargetsPath)");
            buildContext.VCTargetsPathEffective_ = activeConfig.Evaluate("$(VCTargetsPathEffective)");
            buildContext.VC_IncludePath_ = activeConfig.Evaluate("$(VC_IncludePath)");
            buildContext.VC_LibraryPath_ = activeConfig.Evaluate("$(VC_LibraryPath_x64)");
            buildContext.VC_ExecutablePath_ = activeConfig.Evaluate("$(VC_ExecutablePath_x64)");
            buildContext.WindowsSDK_IncludePath_ = GetFirstPath(activeConfig.Evaluate("$(WindowsSDK_IncludePath)"));
            buildContext.WindowsSDK_LibraryPath_ = GetFirstPath(activeConfig.Evaluate("$(WindowsSDK_LibraryPath_x64)"));
            buildContext.WindowsSDK_ExecutablePath_ = GetFirstPath(activeConfig.Evaluate("$(WindowsSDK_ExecutablePath_x64)"));
            buildContext.WindowsSDKDir_ = GetFirstPath(activeConfig.Evaluate("$(WindowsSDKDir)"));

            buildContext.configuration_ = vsFastProject.buildProject_.GetProperty("Configuration").EvaluatedValue;
            buildContext.platform_ = vsFastProject.buildProject_.GetProperty("Platform").EvaluatedValue;

            Result result;
            result = new Result()
            {
                success_ = false,
                bffPath_ = string.Empty,
                bffName_ = string.Empty,
                lastbuildstate_ = string.Empty,
            };
            Microsoft.Build.Evaluation.Project buildProject = vsFastProject.buildProject_;
            result.lastbuildstate_ = $"PlatformToolSet={PlatformToolSet}:VCToolArchitecture={VCToolArchitecture}:VCToolsVersion={VCToolsVersion}:TargetPlatformVersion={vsFastProject.buildProject_.GetProperty("TargetPlatformVersion").EvaluatedValue}:";

            vsFastProject.configType_ = buildProject.GetProperty("ConfigurationType").EvaluatedValue;
            vsFastProject.rootDir_ = System.IO.Path.GetDirectoryName(buildProject.FullPath).TrimEnd('\\', '/');
            vsFastProject.intDir_ = buildProject.GetProperty("IntDirFullPath").EvaluatedValue.Replace("\\", "/");
            vsFastProject.compilerPDB_ = ChopLastFileSeparator(buildProject.GetProperty("IntDirFullPath").EvaluatedValue);
            vsFastProject.linkerPDB_ = System.IO.Path.Combine(buildProject.GetProperty("OutDirFullPath").EvaluatedValue, $"{vsFastProject.targetName_}.pdb");
            result.project_ = new ResultProject()
            {
                name_ = vsFastProject.targetName_,
                projectDir_ = vsFastProject.rootDir_,
                intDir_ = vsFastProject.intDir_,
                configuration_ = buildProject.GetProperty("Configuration").EvaluatedValue,
                platform_ = buildProject.GetProperty("Platform").EvaluatedValue,
            };

            StringBuilder bffBuilder = buildContext.stringBuilder_.Clear();
            string rootDirectory = System.IO.Path.GetDirectoryName(vsFastProject.project_.FullName);
            result.bffName_ = string.Format("fbuild_{0}_{1}_{2}.bff", vsFastProject.targetName_, buildContext.configuration_, buildContext.platform_);
            result.bffPath_ = System.IO.Path.Combine(rootDirectory, result.bffName_);
            if (!CheckRebuild(result.bffPath_, vsFastProject, out result.project_.hash_))
            {
                result.success_ = true;
                return result;
            }

            buildContext.CppTaskAssembly_ = GetCPPTaskAssembly(buildContext.VCTargetsPath_);
            if (null == buildContext.CppTaskAssembly_)
            {
                result.success_ = false;
                return result;
            }

            if (buildContext.environments_.ContainsKey("CUDA_PATH"))
            {
                buildContext.CUDAPath_ = buildContext.environments_["CUDA_PATH"];
            }

            {
                Dictionary<string, List<string>> paths = new Dictionary<string, List<string>>() {
                    { "PATH", new List<string>() },
                    { "LIB", new List<string>() },
                    { "LIBPATH", new List<string>() },
                    { "INCLUDE", new List<string>() },
                };
                foreach (KeyValuePair<string, string> environment in buildContext.environments_)
                {
                    if (string.Compare(environment.Key, "PATH", true) == 0)
                    {
                        AddPathList(paths["PATH"], environment.Value);

                    }
                    else if (string.Compare(environment.Key, "LIB", true) == 0)
                    {
                        AddPathList(paths["LIB"], environment.Value);

                    }
                    else if (string.Compare(environment.Key, "LIBPATH", true) == 0)
                    {
                        AddPathList(paths["LIBPATH"], environment.Value);

                    }
                    else if (string.Compare(environment.Key, "INCLUDE", true) == 0)
                    {
                        AddPathList(paths["INCLUDE"], environment.Value);

                    }
                    else if (string.Compare(environment.Key, "CUDA_PATH", true) == 0)
                    {
                        buildContext.CUDAPath_ = environment.Value.Replace("\\", "/");
                    }
                }
                {
                    string rootDir = System.IO.Path.GetDirectoryName(vsFastProject.buildProject_.FullPath);
                    AddPathList(paths["PATH"], rootDir, vsFastProject.buildProject_.GetProperty("Path").EvaluatedValue);
                    AddPathList(paths["LIB"], rootDir, vsFastProject.buildProject_.GetProperty("LibraryPath").EvaluatedValue);
                    AddPathList(paths["LIBPATH"], rootDir, vsFastProject.buildProject_.GetProperty("ReferencePath").EvaluatedValue);
                    AddPathList(paths["INCLUDE"], rootDir, vsFastProject.buildProject_.GetProperty("IncludePath").EvaluatedValue);
                }
                Dictionary<string, string> dictionary = new Dictionary<string, string>(buildContext.environments_.Count);
                string pathKey = string.Empty;
                foreach (KeyValuePair<string, string> environment in buildContext.environments_)
                {
                    if (string.Compare(environment.Key, "PATH", true) == 0)
                    {
                        pathKey = environment.Key;
                        dictionary.Add(environment.Key, string.Join(";", paths["PATH"]));

                    }
                    else if (string.Compare(environment.Key, "LIB", true) == 0)
                    {
                        dictionary.Add(environment.Key, string.Join(";", paths["LIB"]));

                    }
                    else if (string.Compare(environment.Key, "LIBPATH", true) == 0)
                    {
                        dictionary.Add(environment.Key, string.Join(";", paths["LIBPATH"]));

                    }
                    else if (string.Compare(environment.Key, "INCLUDE", true) == 0)
                    {
                        dictionary.Add(environment.Key, string.Join(";", paths["INCLUDE"]));
                    }
                    else
                    {
                        dictionary.Add(environment.Key, environment.Value);
                    }
                }
                buildContext.environments_ = dictionary;
                if (!string.IsNullOrEmpty(pathKey) && buildContext.environments_.ContainsKey(pathKey))
                {
                    buildContext.pathes_ = buildContext.environments_[pathKey].Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
                else
                {
                    buildContext.pathes_ = new List<string>();
                }
            }
            // Gather items
            BitArray existsFlags = new BitArray((int)ItemType.Num);
            {
                GatherItems(buildContext, vsFastProject);
                existsFlags.Or(vsFastProject.ExistsFlags);
            }

            StringBuilder stringBuilder = buildContext.stringBuilder_;

            stringBuilder.AppendLine($"//{result.project_.hash_}");

#if false
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
#endif

            string targetName = vsFastProject.targetName_.ToUpperInvariant().Replace('-', '_');
            buildContext.envName_ = $"LocalEnv_{targetName}";
            buildContext.compilerName_ = $"Compiler_CXX_{targetName}";
            buildContext.compilerDummyName_ = $"Compiler_Dummy_{targetName}";
            buildContext.rcName_ = $"Compiler_RC{targetName}";
            buildContext.masmName_ = $"Compiler_ASM_MASM{targetName}";
            buildContext.cudaName_ = $"Compiler_CUDA{targetName}";
            buildContext.dxcName_ = $"Compiler_DXC{targetName}";
            {
                List<string> keyvalue = new List<string>(buildContext.environments_.Count);
                foreach (KeyValuePair<string, string> environment in buildContext.environments_)
                {
                    keyvalue.Add($"  '{environment.Key}={environment.Value}'");
                }
                string envs = string.Join("," + Environment.NewLine, keyvalue);
                stringBuilder.AppendLine($".{buildContext.envName_} =");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine(envs);
                stringBuilder.AppendLine("}");
            }

            // Compilers
            stringBuilder.AppendLine("// Compilers");

            // Compiler_CXX
            if (existsFlags[(int)ItemType.CXX])
            {
                stringBuilder.AppendLine($"Compiler('{buildContext.compilerName_}')");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine($"  .Root = '{buildContext.VC_ExecutablePath_}'");
                stringBuilder.AppendLine($"  .Executable = '{buildContext.VC_ExecutablePath_}/CL.exe'");
                stringBuilder.AppendLine("  .CompilerFamily = 'msvc'");
                stringBuilder.AppendLine($"  .Environment = .{buildContext.envName_}");
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
                //stringBuilder.AppendLine("    '$Root$/1033/clui.dll',");
                //stringBuilder.AppendLine("    '$Root$/1033/mspft140ui.dll'");

                stringBuilder.AppendLine("  }");
                stringBuilder.AppendLine("}");
                stringBuilder.AppendLine($".{buildContext.compilerName_} = '{buildContext.compilerName_}'");
                stringBuilder.AppendLine($".{buildContext.compilerDummyName_} = '{buildContext.compilerName_}'");
            }

            // Compiler_RC
            if (existsFlags[(int)ItemType.Resource])
            {
                stringBuilder.AppendLine($"Compiler('{buildContext.rcName_}')");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine($"  .Root = '{buildContext.WindowsSDK_ExecutablePath_}'");
                stringBuilder.AppendLine($"  .Executable = '{buildContext.WindowsSDK_ExecutablePath_}\\RC.exe'");
                stringBuilder.AppendLine("  .CompilerFamily = 'custom'");
                stringBuilder.AppendLine($"  .Environment = .{buildContext.envName_}");
                stringBuilder.AppendLine("}");
                stringBuilder.AppendLine($".{buildContext.rcName_} = '{buildContext.rcName_}'");
            }

            // Compiler_ASM_MASM
            if (existsFlags[(int)ItemType.MASM])
            {
                stringBuilder.AppendLine($"Compiler('{buildContext.masmName_}')");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine($"  .Root = '{buildContext.VC_ExecutablePath_}'");
                stringBuilder.AppendLine($"  .Executable = '{buildContext.VC_ExecutablePath_}\\ml64.exe'");
                stringBuilder.AppendLine("  .CompilerFamily = 'custom'");
                stringBuilder.AppendLine($"  .Environment = .{buildContext.envName_}");
                stringBuilder.AppendLine("}");
                stringBuilder.AppendLine($".{buildContext.masmName_} = '{buildContext.masmName_}'");
            }

            // Compiler_CUDA
            if (existsFlags[(int)ItemType.CUDA])
            {
                stringBuilder.AppendLine($"Compiler('{buildContext.cudaName_}')");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine($"  .Root = '{buildContext.CUDAPath_}/bin'");
                stringBuilder.AppendLine($"  .Executable = '{buildContext.CUDAPath_}/bin/nvcc.exe'");
                stringBuilder.AppendLine("  .CompilerFamily = 'cuda-nvcc'");
                stringBuilder.AppendLine($"  .Environment = .{buildContext.envName_}");
                stringBuilder.AppendLine("}");
                stringBuilder.AppendLine($".{buildContext.cudaName_} = '{buildContext.cudaName_}'");
            }

            // Compiler_DXC
            if (existsFlags[(int)ItemType.HLSL])
            {
                stringBuilder.AppendLine($"Compiler('{buildContext.dxcName_}')");
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine($"  .Root = '{buildContext.WindowsSDK_ExecutablePath_}'");
                stringBuilder.AppendLine($"  .Executable = '{buildContext.WindowsSDK_ExecutablePath_}/dxc.exe'");
                stringBuilder.AppendLine("  .CompilerFamily = 'custom'");
                stringBuilder.AppendLine($"  .Environment = .{buildContext.envName_}");
                stringBuilder.AppendLine("}");
                stringBuilder.AppendLine($".{buildContext.dxcName_} = '{buildContext.dxcName_}'");
            }

            // Librarian
            {
                buildContext.LibrarianPath_ = $"{buildContext.VC_ExecutablePath_}/Lib.exe";
            }

            // Linker
            {
                buildContext.LinkerPath_ = $"{buildContext.VC_ExecutablePath_}/Link.exe";
            }

            {
                AddProject(buildContext, vsFastProject);
            }

#if false
            // All
            if (0 < vSFastProjects.Count)
            {
                List<string> projectTargets = new List<string>(vSFastProjects.Count);
                foreach (VSFastProject project in vSFastProjects)
                {
                    projectTargets.Add(project.postDepend_);
                }
                stringBuilder.AppendLine("{");
                stringBuilder.AppendLine("  Alias('all')");
                stringBuilder.AppendLine("  {");
                stringBuilder.AppendLine("    .Targets =");
                stringBuilder.AppendLine("    {");
                addStringList(stringBuilder, projectTargets, "      ");
                stringBuilder.AppendLine("    }");
                stringBuilder.AppendLine("  }");
                stringBuilder.AppendLine("}");
            }
#endif
            // Clean
            if (0 < buildContext.targets_.Count)
            {
                string fbuildname = result.project_.name_;
                string fbuilddir = System.IO.Path.GetDirectoryName(result.bffPath_);
                string cleanname = $"fbuild_{vsFastProject.targetName_}_clean_{buildContext.configuration_}_{buildContext.platform_}";
                stringBuilder.AppendLine($"Exec('clean_{fbuildname}')");
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
            System.IO.File.WriteAllText(result.bffPath_, stringBuilder.ToString());
            result.success_ = true;
            return result;
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

        private static bool IsCreatePrecompiledHeader(Microsoft.Build.Evaluation.ProjectItem item)
        {
            if (item.Metadata.Where(x => x.Name == "PrecompiledHeader" && x.EvaluatedValue == "Create").Any())
            {
                return true;
            }
            return false;
        }

        private static void GetPrecompiledHeader(string rootDir, Microsoft.Build.Evaluation.ProjectItem item, ref PrecompiledHeaderInfo precompiledHeaderInfo)
        {
            //if (item.Metadata.Where(x => x.Name == "PrecompiledHeader" && x.EvaluatedValue == "Use").Any())
            //{
            //    precompiledHeaderInfo.PCHInputFile_ = item.GetMetadataValue("PrecompiledHeaderFile").Replace("//", "/").Replace(".hxx", ".cxx");
            //}
            if (item.Metadata.Where(x => x.Name == "PrecompiledHeader" && x.EvaluatedValue == "Create").Any())
            {
                precompiledHeaderInfo.PCHInputFile_ = item.EvaluatedInclude;
                if (!precompiledHeaderInfo.PCHInputFile_.Contains('/') && !precompiledHeaderInfo.PCHInputFile_.Contains('\\'))
                {
                    precompiledHeaderInfo.PCHInputFile_ = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootDir, precompiledHeaderInfo.PCHInputFile_));
                }
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
                Log.OutputDebugLine($"{metaData.Name} = {metaData.EvaluatedValue}");
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

        private static string CreateCUDACommandline(StringBuilder stringBuilder, ICollection<ProjectMetadata> metadataList)
        {
            string additionalOptions = string.Empty;
            string defines = string.Empty;
            string optimization = string.Empty;
            string runtime = string.Empty;
            string runtimeChecks = string.Empty;
            string nvccCompilation = string.Empty;
            string cudaRuntime = string.Empty;
            string targetMachinePlatform = string.Empty;
            bool fastMath = false;
            bool hostDebugInfo = false; //-g
            foreach (ProjectMetadata metaData in metadataList)
            {
                switch (metaData.Name)
                {
                    case "AdditionalOptions":
                        additionalOptions = metaData.EvaluatedValue;
                        break;
                    case "Defines":
                        defines = metaData.EvaluatedValue;
                        break;
                    case "Optimization":
                        optimization = metaData.EvaluatedValue;
                        break;
                    case "Runtime":
                        runtime = metaData.EvaluatedValue;
                        break;
                    case "RuntimeChecks":
                        runtimeChecks = metaData.EvaluatedValue;
                        break;
                    case "NvccCompilation":
                        nvccCompilation = metaData.EvaluatedValue.ToLower();
                        break;
                    case "CudaRuntime":
                        cudaRuntime = metaData.EvaluatedValue.ToLower();
                        break;
                    case "TargetMachinePlatform":
                        targetMachinePlatform = metaData.EvaluatedValue;
                        break;
                    case "FastMath":
                        fastMath = "true" == metaData.EvaluatedValue;
                        break;
                    case "HostDebugInfo":
                        hostDebugInfo = "true" == metaData.EvaluatedValue;
                        break;
                }
            }
            stringBuilder.Clear();
            stringBuilder.Append("-forward-unknown-to-host-compiler");
            if (!string.IsNullOrEmpty(defines))
            {
                string[] defineLinst = defines.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string define in defineLinst)
                {
                    stringBuilder.Append($" -D{define}");
                }
            }
            stringBuilder.Append($" -Xcompiler=\"-{optimization} -{runtime}");
            if (!string.IsNullOrEmpty(runtimeChecks))
            {
                stringBuilder.Append($" /{runtimeChecks}");
            }
            stringBuilder.Append("\"");
            if (!string.IsNullOrEmpty(nvccCompilation))
            {
                stringBuilder.Append($" --{nvccCompilation}");
            }
            if (!string.IsNullOrEmpty(cudaRuntime))
            {
                stringBuilder.Append($" -cudart {cudaRuntime}");
            }
            if (!string.IsNullOrEmpty(targetMachinePlatform))
            {
                stringBuilder.Append($" --machine {targetMachinePlatform}");
            }
            if (fastMath)
            {
                stringBuilder.Append(" -use_fast_math");
            }
            if (hostDebugInfo)
            {
                stringBuilder.Append(" -g");
            }
            if (!string.IsNullOrEmpty(additionalOptions))
            {
                stringBuilder.Append($" {additionalOptions}");
            }
            stringBuilder.Append(" -x cu -c \"%1\" -o \"%2\" -Xcompiler=-Fd\"$CompilerPDB$\",-FS");
            return stringBuilder.ToString();
        }

        private static string CreateMASMCommandline(StringBuilder stringBuilder, ICollection<ProjectMetadata> metadataList)
        {
            bool nologo = false;
            bool generateDebugInformation = false;
            string warningLevel = string.Empty;
            string defines = string.Empty;
            foreach (ProjectMetadata metaData in metadataList)
            {
                switch (metaData.Name)
                {
                    case "NoLogo":
                        nologo = "true" == metaData.EvaluatedValue;
                        break;
                    case "GenerateDebugInformation":
                        generateDebugInformation = "true" == metaData.EvaluatedValue;
                        break;
                    case "Defines":
                        defines = metaData.EvaluatedValue;
                        break;
                    case "WarningLevel":
                        warningLevel = metaData.EvaluatedValue;
                        break;
                }
            }
            stringBuilder.Clear();
            stringBuilder.Append("/c");
            if (nologo)
            {
                stringBuilder.Append(" /nologo");
            }
            if (generateDebugInformation)
            {
                stringBuilder.Append(" /Zi");
            }
            if (!string.IsNullOrEmpty(warningLevel))
            {
                stringBuilder.Append($" /W{warningLevel}");
            }
            if (!string.IsNullOrEmpty(defines))
            {
                string[] defineLinst = defines.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string define in defineLinst)
                {
                    stringBuilder.Append($" /D{define}");
                }
            }
            stringBuilder.Append(" /Fo\"%2\" \"%1\"");
            return stringBuilder.ToString();
        }

        private static CustomBuildItem CreateCustomBuildCommandline(ICollection<ProjectMetadata> metadataList)
        {
            CustomBuildItem customBuildItem = new CustomBuildItem();
            customBuildItem.inputs_ = new List<string>();
            customBuildItem.output_ = string.Empty;
            customBuildItem.command_ = string.Empty;
            customBuildItem.arguments_ = string.Empty;
            customBuildItem.linkOject_ = false;
            customBuildItem.outputAsContent_ = false;
            foreach (ProjectMetadata metaData in metadataList)
            {
                switch (metaData.Name)
                {
                    case "AdditionalInputs":
                        customBuildItem.inputs_ = metaData.EvaluatedValue.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        break;
                    case "Outputs":
                        if (!string.IsNullOrEmpty(metaData.EvaluatedValue))
                        {
                            customBuildItem.output_ = metaData.EvaluatedValue.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).First();
                        }
                        break;
                    case "Command":
                        customBuildItem.command_ = metaData.EvaluatedValue;
                        break;
                    case "LinkObjects":
                        customBuildItem.linkOject_ = "true" == metaData.EvaluatedValue;
                        break;
                    case "TreatOutputAsContent":
                        customBuildItem.outputAsContent_ = "true" == metaData.EvaluatedValue;
                        break;
                }
            }
            if (!string.IsNullOrEmpty(customBuildItem.command_))
            {
                string[] lines = customBuildItem.command_.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                customBuildItem.command_ = string.Empty;
                foreach (string line in lines)
                {
                    if (line.Contains("setlocal")
                        || line.Contains("endlocal")
                        || line.Contains(":cmdEnd")
                        || line.Contains("exit")
                        || line.Contains(":cmErrorLevel")
                        || line.Contains(":cmDone")
                        || line.Contains("%errorlevel%"))
                    {
                        continue;
                    }
                    string trimed = line.Trim(' ', '\t', '\v');
                    if (string.IsNullOrEmpty(trimed))
                    {
                        continue;
                    }
                    if ('"' == trimed[0])
                    {
                        int commandEnd = 0;
                        for (int i = 1; i < trimed.Length; ++i)
                        {
                            if ('"' == trimed[i])
                            {
                                commandEnd = i;
                                break;
                            }
                        }
                        if (1 < commandEnd)
                        {
                            customBuildItem.command_ = trimed.Substring(0, commandEnd + 1);
                        }
                        trimed = trimed.Substring(commandEnd + 1);
                    }
                    else
                    {
                        int commandEnd = 0;
                        for (int i = 1; i < trimed.Length; ++i)
                        {
                            if (char.IsWhiteSpace(trimed[i]))
                            {
                                commandEnd = i;
                                break;
                            }
                        }
                        if (0 < commandEnd)
                        {
                            customBuildItem.command_ = trimed.Substring(0, commandEnd);
                        }
                        trimed = trimed.Substring(commandEnd);
                    }
                    if (!string.IsNullOrEmpty(trimed))
                    {
                        customBuildItem.arguments_ = trimed;
                    }
                    if (!string.IsNullOrEmpty(customBuildItem.command_))
                    {
                        break;
                    }
                }
            }
            return customBuildItem;
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
            if (null == property)
            {
                return false;
            }
            string value = property.EvaluatedValue;
            bool result = false;
            return bool.TryParse(value, out result) ? result : false;
        }

        private static void GatherItems(BuildContext buildContext, VSFastProject project)
        {
            Microsoft.Build.Evaluation.Project buildProject = project.buildProject_;

            ICollection<Microsoft.Build.Evaluation.ProjectItem> compileItems = buildProject.GetItems("ClCompile");

            // Precompile Header
            project.precompiledHeaderInfo_ = new PrecompiledHeaderInfo();
            foreach (Microsoft.Build.Evaluation.ProjectItem item in compileItems)
            {
                if (!IsBuildTarget(item))
                {
                    continue;
                }
                GetPrecompiledHeader(project.rootDir_, item, ref project.precompiledHeaderInfo_);
                if (project.precompiledHeaderInfo_.IsValid())
                {
                    break;
                }
            }
            if (project.precompiledHeaderInfo_.IsValid())
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
                        //string pchCompilerOptions = GenerateTaskCommandLine(task, new string[] { "ObjectFileName", "AssemblerListingLocation", "ProgramDataBaseFileName", "XMLDocumentationFileName", "DiagnosticsFormat" }, item.Metadata) + " /FS";
                        string pchCompilerOptions = GenerateTaskCommandLine(task, new string[] { "ObjectFileName", "AssemblerListingLocation", "XMLDocumentationFileName", "DiagnosticsFormat" }, item.Metadata) + " /FS";
#if false
                        switch (buildContext.platform_)
                        {
                            case "x86":
                                pchCompilerOptions += " /D_M_IX86=600";
                                break;
                            case "x64":
                                pchCompilerOptions += " /D_M_X64=100";
                                break;
                            case "arm":
                                pchCompilerOptions += " /D_M_ARM=7";
                                break;
                            case "arm64":
                                pchCompilerOptions += " /D_M_ARM64=1";
                                break;
                        }
#endif
                        pchCompilerOptions = pchCompilerOptions.Replace("  ", " ").Replace("\\", "/").Replace("//", "/").Replace("/D ", "/D");
                        project.precompiledHeaderInfo_.PCHOptions_ = $"\"%1\" /Fo\"%3\" {pchCompilerOptions} /Fd\"$CompilerPDB$\"";
                    }
                }
            }

            { // Resource objects
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
                    string evaluatedInclude = System.IO.Path.GetFullPath(System.IO.Path.Combine(project.rootDir_, item.EvaluatedInclude)).Replace("\\", "/").Replace("//", "/");
                    IEnumerable<FBCompileItem> matchingNodes = project.CompileItems.Where(el => el.AddIfMatches(ItemType.Resource, evaluatedInclude, formattedCompilerOptions, "res"));
                    if (!matchingNodes.Any())
                    {
                        project.CompileItems.Add(new FBCompileItem(ItemType.Resource, evaluatedInclude, formattedCompilerOptions, "res"));
                        project.ExistsFlags.Set((int)ItemType.Resource, true);
                    }
                }
            }

            { // Compile items
                //string[] propertiesToSkip = new string[] { "ObjectFileName", "AssemblerListingLocation", "ProgramDataBaseFileName", "XMLDocumentationFileName", "DiagnosticsFormat" };
                string[] propertiesToSkip = new string[] { "ObjectFileName", "AssemblerListingLocation", "XMLDocumentationFileName", "DiagnosticsFormat" };
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
                    optionBuilder.Append(" /Fd\"$CompilerPDB$\"");
                    optionBuilder = optionBuilder.Replace("\\", "/").Replace("//", "/").Replace("/D ", "/D").Replace("/JMC", "/Zi").Replace("/TP", string.Empty).Replace("/TC", string.Empty);
                    if (item.EvaluatedInclude.EndsWith(".c"))
                    {
                        optionBuilder.Append(" /TC");
                    }
                    else
                    {
                        optionBuilder.Append(" /TP");
                    }
#if false
                    switch (buildContext.platform_)
                    {
                        case "x86":
                            optionBuilder.Append(" /D_M_IX86=600");
                            break;
                        case "x64":
                            optionBuilder.Append(" /D_M_X64=100");
                            break;
                        case "arm":
                            optionBuilder.Append(" /D_M_ARM=7");
                            break;
                        case "arm64":
                            optionBuilder.Append(" /_M_ARM64=1");
                            break;
                    }
#endif
                    optionBuilder = optionBuilder.Replace("   ", " ").Replace("  ", " ");
                    string formattedCompilerOptions = optionBuilder.ToString();
                    string evaluatedInclude = System.IO.Path.GetFullPath(System.IO.Path.Combine(project.rootDir_, item.EvaluatedInclude)).Replace("\\", "/").Replace("//", "/");
                    IEnumerable<FBCompileItem> matchingNodes = project.CompileItems.Where(el => el.AddIfMatches(ItemType.CXX, evaluatedInclude, formattedCompilerOptions, "obj"));
                    if (!matchingNodes.Any())
                    {
                        project.CompileItems.Add(new FBCompileItem(ItemType.CXX, evaluatedInclude, formattedCompilerOptions, "obj"));
                        project.ExistsFlags.Set((int)ItemType.CXX, true);
                    }
                }
            }

            // CUDA Compile
            {
                ICollection<Microsoft.Build.Evaluation.ProjectItem> cudaCompileItems = buildProject.GetItems("CudaCompile");
                foreach (Microsoft.Build.Evaluation.ProjectItem item in cudaCompileItems)
                {
                    string tempCompilerOptions = CreateCUDACommandline(buildContext.optionBuilder_, item.Metadata);
                    string evaluatedInclude = System.IO.Path.GetFullPath(System.IO.Path.Combine(project.rootDir_, item.EvaluatedInclude)).Replace("\\", "/").Replace("//", "/");
                    IEnumerable<FBCompileItem> matchingNodes = project.CompileItems.Where(el => el.AddIfMatches(ItemType.CUDA, evaluatedInclude, tempCompilerOptions, "obj"));
                    if (!matchingNodes.Any())
                    {
                        project.CompileItems.Add(new FBCompileItem(ItemType.CUDA, evaluatedInclude, tempCompilerOptions, "obj"));
                        project.ExistsFlags.Set((int)ItemType.CUDA, true);
                    }
                }
            }

            // MASM Compile
            {
                ICollection<Microsoft.Build.Evaluation.ProjectItem> MASMCompileItems = buildProject.GetItems("MASM");
                foreach (Microsoft.Build.Evaluation.ProjectItem item in MASMCompileItems)
                {
                    string tempCompilerOptions = CreateMASMCommandline(buildContext.optionBuilder_, item.Metadata);
                    string evaluatedInclude = System.IO.Path.GetFullPath(System.IO.Path.Combine(project.rootDir_, item.EvaluatedInclude)).Replace("\\", "/").Replace("//", "/");
                    IEnumerable<FBCompileItem> matchingNodes = project.CompileItems.Where(el => el.AddIfMatches(ItemType.MASM, evaluatedInclude, tempCompilerOptions, "obj"));
                    if (!matchingNodes.Any())
                    {
                        project.CompileItems.Add(new FBCompileItem(ItemType.MASM, evaluatedInclude, tempCompilerOptions, "obj"));
                        project.ExistsFlags.Set((int)ItemType.MASM, true);
                    }
                }
            }

            // FX Compile
            {
                string[] propertiesToSkip = new string[] { "ObjectFileOutput" };
                ICollection<Microsoft.Build.Evaluation.ProjectItem> FXCompileItems = buildProject.GetItems("FXCompile");
                foreach (Microsoft.Build.Evaluation.ProjectItem item in FXCompileItems)
                {
                    ToolTask task = Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.FXCTask.FXC")) as ToolTask;
                    string tempCompilerOptions = GenerateTaskCommandLine(task, propertiesToSkip, item.Metadata);
                    StringBuilder optionBuilder = buildContext.optionBuilder_.Clear();
                    optionBuilder.Append(tempCompilerOptions);
                    optionBuilder.Append(" \"%1\" /Fo\"%2\" ");
                    optionBuilder = optionBuilder.Replace("\\", "/").Replace("//", "/");
                    optionBuilder = optionBuilder.Replace("   ", " ").Replace("  ", " ");
                    string formattedCompilerOptions = optionBuilder.ToString();
                    string evaluatedInclude = System.IO.Path.GetFullPath(System.IO.Path.Combine(project.rootDir_, item.EvaluatedInclude)).Replace("\\", "/").Replace("//", "/");
                    IEnumerable<FBCompileItem> matchingNodes = project.CompileItems.Where(el => el.AddIfMatches(ItemType.HLSL, evaluatedInclude, formattedCompilerOptions, "cso"));
                    if (!matchingNodes.Any())
                    {
                        project.CompileItems.Add(new FBCompileItem(ItemType.HLSL, evaluatedInclude, formattedCompilerOptions, "cso"));
                        project.ExistsFlags.Set((int)ItemType.HLSL, true);
                    }
                }
            }

            // Custom Build items
            {
                string[] propertiesToSkip = new string[] { "ObjectFileOutput" };
                ICollection<Microsoft.Build.Evaluation.ProjectItem> customBuildItems = buildProject.GetItems("CustomBuild");
                foreach (Microsoft.Build.Evaluation.ProjectItem item in customBuildItems)
                {
                    if (item.GetMetadataValue("Message").Contains("CMakeLists.txt"))
                    {
                        continue;
                    }
                    CustomBuildItem customBuildItem = CreateCustomBuildCommandline(item.Metadata);
                    if (!string.IsNullOrEmpty(customBuildItem.command_))
                    {
                        customBuildItem.command_ = ExecWhere(buildContext, customBuildItem.command_);
                        project.CompileItems.Add(new FBCompileItem(customBuildItem));
                        project.ExistsFlags.Set((int)ItemType.Custom, true);
                    }
                }
            }
        }

        public static void TouchFile(string path)
        {
            if (System.IO.File.Exists(path))
            {
                return;
            }
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.Create(path).Dispose();
            }
            catch
            {
            }
        }

        private static void AddProject(BuildContext buildContext, VSFastProject project)
        {
            Microsoft.Build.Evaluation.Project buildProject = project.buildProject_;
            BuildType buildType;
            switch (project.configType_)
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

            bool linkIncremental = GetPropertyBool(buildProject.GetProperty("LinkIncremental"));

            StringBuilder stringBuilder = buildContext.stringBuilder_;
            stringBuilder.AppendLine($"// {project.targetName_}");

            stringBuilder.AppendLine("{");
            stringBuilder.AppendLine($"  .CompilerPDB = '{project.compilerPDB_}'");
            stringBuilder.AppendLine($"  .LinkerPDB = '{project.linkerPDB_}'");

            List<string> objTargets = new List<string>(16);
            List<string> dependencies = new List<string>();
            int prebuildCount = 0;
            {
                ICollection<Microsoft.Build.Evaluation.ProjectItem> buildEvents = buildProject.GetItems("PreBuildEvent");
                foreach (Microsoft.Build.Evaluation.ProjectItem projectItem in buildEvents)
                {
                    ProjectMetadata metadata = projectItem.GetMetadata("Command");
                    if (null != metadata)
                    {
                        ++prebuildCount;
                    }
                }
            }

            if (0 < prebuildCount || project.ExistsFlags[(int)ItemType.Custom])
            {
                string preBuildDependencies = string.Empty;
                if (0 < project.dependencies_.Count)
                {
                    StringBuilder dependenciesBuilder = buildContext.optionBuilder_.Clear();
                    dependenciesBuilder.AppendLine("  .PreBuildDependencies =");
                    dependenciesBuilder.AppendLine("  {");
                    for (int i = 0; i < project.dependencies_.Count; ++i)
                    {
                        dependenciesBuilder.Append($"      '{project.dependencies_[i].postDepend_}'");
                        if (i == (project.dependencies_.Count - 1))
                        {
                            dependenciesBuilder.AppendLine();
                        }
                        else
                        {
                            dependenciesBuilder.AppendLine(",");
                        }
                    }
                    dependenciesBuilder.AppendLine("  }");
                    preBuildDependencies = dependenciesBuilder.ToString();
                }
                {
                    ICollection<Microsoft.Build.Evaluation.ProjectItem> buildEvents = buildProject.GetItems("PreBuildEvent");
                    int count = 0;
                    foreach (Microsoft.Build.Evaluation.ProjectItem projectItem in buildEvents)
                    {
                        ProjectMetadata metadata = projectItem.GetMetadata("Command");
                        if (null == metadata)
                        {
                            continue;
                        }
                        string command = metadata.EvaluatedValue;
                        string dummy = System.IO.Path.Combine(project.intDir_, $"{project.targetName_}_prebuild_dummy_{count}");
                        TouchFile(dummy);
                        string name = $"{project.targetName_}_prebuild{count}";
                        string preBuildBatchFile = System.IO.Path.Combine(project.intDir_, $"{name}.bat");
                        System.IO.File.WriteAllText(preBuildBatchFile, command);
                        stringBuilder.AppendLine($"  Exec('{name}')");
                        stringBuilder.AppendLine("  {");
                        stringBuilder.AppendLine("    .ExecExecutable = 'C:/Windows/System32/cmd.exe'");
                        stringBuilder.AppendLine($"    .ExecArguments = '/C \"%1\"'");
                        stringBuilder.AppendLine($"    .ExecInput = {{'{preBuildBatchFile}'}}");
                        stringBuilder.AppendLine($"    .ExecOutput = '{dummy}'");
                        stringBuilder.AppendLine("    .ExecUseStdOutAsOutput = true");
                        stringBuilder.AppendLine("    .ExecAlways = true");
                        stringBuilder.AppendLine($"    .Environment = .{buildContext.envName_}");
                        stringBuilder.AppendLine("  }");
                        dependencies.Add(name);
                        ++count;
                    }
                }

                // Custom build
                if (project.ExistsFlags[(int)ItemType.Custom])
                {
                    int count = 0;
                    foreach (FBCompileItem item in project.CompileItems.Where((FBCompileItem item) => item.Type == ItemType.Custom))
                    {
                        string name = $"{project.targetName_}_custom{count}";
                        stringBuilder.AppendLine($"  Exec('{name}')");
                        stringBuilder.AppendLine("  {");
                        stringBuilder.Append(preBuildDependencies);
                        if (!string.IsNullOrEmpty(item.CustomBuildItem.arguments_))
                        {
                            stringBuilder.AppendLine($"    .ExecExecutable = '{item.CustomBuildItem.command_}'");
                            stringBuilder.AppendLine($"    .ExecArguments = '{item.CustomBuildItem.arguments_}'");
                        }
                        else
                        {
                            stringBuilder.AppendLine($"    .ExecExecutable = '{item.CustomBuildItem.command_}'");
                        }
                        if (0 < item.CustomBuildItem.inputs_.Count)
                        {
                            stringBuilder.AppendLine($"    .ExecInput =");
                            stringBuilder.AppendLine("    {");
                            addStringList(stringBuilder, item.CustomBuildItem.inputs_, "    ");
                            stringBuilder.AppendLine("    }");
                        }
                        if (!string.IsNullOrEmpty(item.CustomBuildItem.output_))
                        {
                            stringBuilder.AppendLine($"    .ExecWorkingDir = '{System.IO.Path.GetDirectoryName(item.CustomBuildItem.output_)}'");
                            stringBuilder.AppendLine($"    .ExecOutput = '{item.CustomBuildItem.output_}'");
                            buildContext.targets_.Add(item.CustomBuildItem.output_);
                            if (item.CustomBuildItem.linkOject_)
                            {
                                objTargets.Add(name);
                            }
                        }
                        if (item.CustomBuildItem.outputAsContent_)
                        {
                            stringBuilder.AppendLine("    .ExecUseStdOutAsOutput = true");
                        }
                        stringBuilder.AppendLine("    .ExecAlwaysShowOutput = true");
                        stringBuilder.AppendLine("    .ExecAlways = true");
                        stringBuilder.AppendLine($"    .Environment = .{buildContext.envName_}");
                        stringBuilder.AppendLine("  }");
                        dependencies.Add(name);
                        ++count;
                    }
                }
            }
            else
            {
                // Dependencies
                if (0 < project.dependencies_.Count)
                {
                    for (int i = 0; i < project.dependencies_.Count; ++i)
                    {
                        dependencies.Add(project.dependencies_[i].postDepend_);
                    }
                }
            }

            string dependenciesName = string.Empty;
            if (0 < dependencies.Count)
            {
                dependenciesName = $"{project.targetName_}_deps";
                stringBuilder.AppendLine($"  Alias('{dependenciesName}')");
                stringBuilder.AppendLine("  {");
                stringBuilder.AppendLine("    .Targets =");
                stringBuilder.AppendLine("    {");
                addStringList(stringBuilder, dependencies, "    ");
                stringBuilder.AppendLine("    }");
                stringBuilder.AppendLine("    .Hidden = true");
                stringBuilder.AppendLine("  }");
            }

            // Resource objects
            {
                int count = 0;
                foreach (FBCompileItem item in project.CompileItems.Where(item => item.Type == ItemType.Resource))
                {
                    string resourceTarget = $"{project.targetName_}_rc_objs_{count}";
                    objTargets.Add(resourceTarget);
                    stringBuilder.AppendLine($"  ObjectList('{resourceTarget}')");
                    stringBuilder.AppendLine("  {");
                    if (!string.IsNullOrEmpty(dependenciesName))
                    {
                        stringBuilder.AppendLine("    .PreBuildDependencies =");
                        stringBuilder.AppendLine("    {");
                        stringBuilder.AppendLine($"      '{dependenciesName}'");
                        stringBuilder.AppendLine("    }");
                    }
                    stringBuilder.AppendLine($"    .Compiler = .{buildContext.rcName_}");
                    if (!string.IsNullOrEmpty(item.Options))
                    {
                        stringBuilder.AppendLine($"    .CompilerOptions = ' {item.Options}'");
                    }
                    stringBuilder.AppendLine($"    .CompilerOutputPath = '{project.intDir_}'");
                    stringBuilder.AppendLine($"    .CompilerOutputExtension = '.{item.OutputExtension}'");
                    stringBuilder.AppendLine("    .CompilerOutputKeepBaseExtension = false");
                    stringBuilder.AppendLine("    .CompilerInputFiles =");
                    stringBuilder.AppendLine("    {");
                    for (int j = 0; j < item.InputFiles.Count; ++j)
                    {
                        if (j == (item.InputFiles.Count - 1))
                        {
                            stringBuilder.AppendLine($"      '{item.InputFiles[j]}'");
                        }
                        else
                        {
                            stringBuilder.AppendLine($"      '{item.InputFiles[j]}',");
                        }
                        string targetPath = System.IO.Path.Combine(project.intDir_, System.IO.Path.GetFileName(item.InputFiles[j]));
                        buildContext.targets_.Add($"{targetPath}.{item.OutputExtension}");
                    }
                    stringBuilder.AppendLine("    }");

                    stringBuilder.AppendLine("    .Hidden = true");
                    stringBuilder.AppendLine("  }");
                    ++count;
                }
            }

            // MASM objects
            {
                int count = 0;
                foreach (FBCompileItem item in project.CompileItems.Where(item => item.Type == ItemType.MASM))
                {
                    string masmTarget = $"{project.targetName_}_masm_objs_{count}";
                    objTargets.Add(masmTarget);
                    stringBuilder.AppendLine($"  ObjectList('{masmTarget}')");
                    stringBuilder.AppendLine("  {");
                    if (!string.IsNullOrEmpty(dependenciesName))
                    {
                        stringBuilder.AppendLine("    .PreBuildDependencies =");
                        stringBuilder.AppendLine("    {");
                        stringBuilder.AppendLine($"      '{dependenciesName}'");
                        stringBuilder.AppendLine("    }");
                    }
                    stringBuilder.AppendLine($"    .Compiler = .{buildContext.masmName_}");
                    if (!string.IsNullOrEmpty(item.Options))
                    {
                        stringBuilder.AppendLine($"    .CompilerOptions = ' {item.Options}'");
                    }
                    stringBuilder.AppendLine($"    .CompilerOutputPath = '{project.intDir_}'");
                    stringBuilder.AppendLine($"    .CompilerOutputExtension = '.{item.OutputExtension}'");
                    stringBuilder.AppendLine("    .CompilerOutputKeepBaseExtension = false");
                    stringBuilder.AppendLine("    .CompilerInputFiles =");
                    stringBuilder.AppendLine("    {");
                    for (int j = 0; j < item.InputFiles.Count; ++j)
                    {
                        if (j == (item.InputFiles.Count - 1))
                        {
                            stringBuilder.AppendLine($"      '{item.InputFiles[j]}'");
                        }
                        else
                        {
                            stringBuilder.AppendLine($"      '{item.InputFiles[j]}',");
                        }
                        string targetPath = System.IO.Path.Combine(project.intDir_, System.IO.Path.GetFileName(item.InputFiles[j]));
                        buildContext.targets_.Add($"{targetPath}.{item.OutputExtension}");
                    }
                    stringBuilder.AppendLine("    }");

                    stringBuilder.AppendLine("    .Hidden = true");
                    stringBuilder.AppendLine("  }");
                    ++count;
                }
            }

            // CUDA objects
            {
                int count = 0;
                foreach (FBCompileItem item in project.CompileItems.Where(item => item.Type == ItemType.CUDA))
                {
                    string cudaTarget = $"{project.targetName_}_cuda_objs_{count}";
                    objTargets.Add(cudaTarget);
                    stringBuilder.AppendLine($"  ObjectList('{cudaTarget}')");
                    stringBuilder.AppendLine("  {");
                    if (!string.IsNullOrEmpty(dependenciesName))
                    {
                        stringBuilder.AppendLine("    .PreBuildDependencies =");
                        stringBuilder.AppendLine("    {");
                        stringBuilder.AppendLine($"      '{dependenciesName}'");
                        stringBuilder.AppendLine("    }");
                    }
                    stringBuilder.AppendLine($"    .Compiler = .{buildContext.cudaName_}");
                    if (!string.IsNullOrEmpty(item.Options))
                    {
                        stringBuilder.AppendLine($"    .CompilerOptions = ' {item.Options}'");
                    }
                    stringBuilder.AppendLine($"    .CompilerOutputPath = '{project.intDir_}'");
                    stringBuilder.AppendLine($"    .CompilerOutputExtension = '.{item.OutputExtension}'");
                    stringBuilder.AppendLine("    .CompilerOutputKeepBaseExtension = false");
                    stringBuilder.AppendLine("    .CompilerInputFiles =");
                    stringBuilder.AppendLine("    {");
                    for (int j = 0; j < item.InputFiles.Count; ++j)
                    {
                        if (j == (item.InputFiles.Count - 1))
                        {
                            stringBuilder.AppendLine($"      '{item.InputFiles[j]}'");
                        }
                        else
                        {
                            stringBuilder.AppendLine($"      '{item.InputFiles[j]}',");
                        }
                        string targetPath = System.IO.Path.Combine(project.intDir_, System.IO.Path.GetFileName(item.InputFiles[j]));
                        buildContext.targets_.Add($"{targetPath}.{item.OutputExtension}");
                    }
                    stringBuilder.AppendLine("    }");

                    stringBuilder.AppendLine("    .Hidden = true");
                    stringBuilder.AppendLine("  }");
                    ++count;
                }
            }

            // HLSL objects
            string fxctargets_deps = string.Empty;
            {
                List<string> fxctargets = new List<string>();
                foreach (FBCompileItem item in project.CompileItems.Where(item => item.Type == ItemType.HLSL))
                {
                    string fxcTarget = $"{project.targetName_}_fxc_objs_{fxctargets.Count}";
                    fxctargets.Add(fxcTarget);
                    stringBuilder.AppendLine($"  ObjectList('{fxcTarget}')");
                    stringBuilder.AppendLine("  {");
                    if (!string.IsNullOrEmpty(dependenciesName))
                    {
                        stringBuilder.AppendLine("    .PreBuildDependencies =");
                        stringBuilder.AppendLine("    {");
                        stringBuilder.AppendLine($"      '{dependenciesName}'");
                        stringBuilder.AppendLine("    }");
                    }
                    stringBuilder.AppendLine($"    .Compiler = .{buildContext.dxcName_}");
                    if (!string.IsNullOrEmpty(item.Options))
                    {
                        stringBuilder.AppendLine($"    .CompilerOptions = ' {item.Options}'");
                    }
                    stringBuilder.AppendLine($"    .CompilerOutputPath = '{project.intDir_}'");
                    stringBuilder.AppendLine($"    .CompilerOutputExtension = '.{item.OutputExtension}'");
                    stringBuilder.AppendLine("    .CompilerOutputKeepBaseExtension = false");
                    stringBuilder.AppendLine("    .CompilerInputFiles =");
                    stringBuilder.AppendLine("    {");
                    for (int j = 0; j < item.InputFiles.Count; ++j)
                    {
                        if (j == (item.InputFiles.Count - 1))
                        {
                            stringBuilder.AppendLine($"      '{item.InputFiles[j]}'");
                        }
                        else
                        {
                            stringBuilder.AppendLine($"      '{item.InputFiles[j]}',");
                        }
                        string targetPath = System.IO.Path.Combine(project.intDir_, System.IO.Path.GetFileName(item.InputFiles[j]));
                        buildContext.targets_.Add($"{targetPath}.{item.OutputExtension}");
                    }
                    stringBuilder.AppendLine("    }");

                    stringBuilder.AppendLine("    .Hidden = true");
                    stringBuilder.AppendLine("  }");
                }
                if (0 < fxctargets.Count)
                {
                    fxctargets_deps = $"{project.targetName_}_fxctargets";
                    stringBuilder.AppendLine($"  Alias('{fxctargets_deps}')");
                    stringBuilder.AppendLine("  {");
                    stringBuilder.AppendLine("    .Targets =");
                    stringBuilder.AppendLine("    {");
                    addStringList(stringBuilder, fxctargets, "      ");
                    stringBuilder.AppendLine("    }");
                    stringBuilder.AppendLine("    .Hidden = true");
                    stringBuilder.AppendLine("  }");
                }
            }

            {// CXX items
                bool unityBuild = false;
                int count = 0;
                foreach (FBCompileItem item in project.CompileItems.Where(item => item.Type == ItemType.CXX))
                {
                    bool usedUnity = false;
                    if (unityBuild && 1 < item.InputFiles.Count)
                    {
                        stringBuilder.AppendLine($"  Unity('{project.targetName_}_unity{count}')");
                        stringBuilder.AppendLine("  {");
                        stringBuilder.AppendLine($"    .UnityInputFiles = {{{string.Join(",", item.InputFiles.ConvertAll(el => string.Format("'{0}'", el)).ToArray())}}}");
                        stringBuilder.AppendLine($"    .UnityOutputPath = '{project.intDir_}'");
                        stringBuilder.AppendLine($"    .UnityNumFiles = {1 + item.InputFiles.Count / 10}");
                        stringBuilder.AppendLine("  }");
                        usedUnity = true;
                    }

                    string objTargetName = $"{project.targetName_}_objs{count}";
                    objTargets.Add(objTargetName);
                    stringBuilder.AppendLine($"  ObjectList('{objTargetName}')");
                    stringBuilder.AppendLine("  {");
                    if (!string.IsNullOrEmpty(dependenciesName))
                    {
                        stringBuilder.AppendLine("    .PreBuildDependencies =");
                        stringBuilder.AppendLine("    {");
                        stringBuilder.AppendLine($"      '{dependenciesName}'");
                        stringBuilder.AppendLine("    }");
                    }

                    stringBuilder.AppendLine($"    .Compiler = .{buildContext.compilerName_}");

                    if (project.precompiledHeaderInfo_.IsValid())
                    {
                        stringBuilder.AppendLine($"    .PCHInputFile = '{project.precompiledHeaderInfo_.PCHInputFile_}'");
                        stringBuilder.AppendLine($"    .PCHOutputFile = '{project.precompiledHeaderInfo_.PCHOutputFile_}'");
                        if (!string.IsNullOrEmpty(project.precompiledHeaderInfo_.PCHOptions_))
                        {
                            stringBuilder.AppendLine($"    .PCHOptions = '{project.precompiledHeaderInfo_.PCHOptions_}'");
                        }
                    }
                    stringBuilder.AppendLine($"    .CompilerOptions = ' {item.Options}'");
                    stringBuilder.AppendLine($"    .CompilerOutputPath = '{project.intDir_}'");
                    stringBuilder.AppendLine($"    .CompilerOutputExtension = '.{item.OutputExtension}'");
                    stringBuilder.AppendLine("    .CompilerOutputKeepBaseExtension = false");
                    if (usedUnity)
                    {
                        stringBuilder.AppendLine($"    .CompilerInputUnity = {{ '{project.targetName_}_unity{count}' }}");
                    }
                    else
                    {
                        string str = string.Join(",", item.InputFiles.ConvertAll(x => string.Format("'{0}'", x)).ToArray());
                        stringBuilder.AppendLine($"    .CompilerInputFiles = {{ {str} }}");
                        foreach (string file in item.InputFiles)
                        {
                            string targetPath = System.IO.Path.Combine(project.intDir_, System.IO.Path.GetFileName(file));
                            buildContext.targets_.Add($"{targetPath}.{item.OutputExtension}");
                        }
                    }
                    stringBuilder.AppendLine($"    .CompilerOutputExtension = '.{item.OutputExtension}'");
                    stringBuilder.AppendLine("    .Hidden = true");
                    stringBuilder.AppendLine("  }");
                    ++count;
                } //for (int i = 0
            }

            string lastTargetName = project.targetName_;
            {
                ICollection<Microsoft.Build.Evaluation.ProjectItem> postBuildEvents = buildProject.GetItems("PostBuildEvent");
                int postBuildCount = 0;
                foreach (Microsoft.Build.Evaluation.ProjectItem buildEvent in postBuildEvents)
                {
                    ProjectMetadata metadata = buildEvent.GetMetadata("Command");
                    if (null == metadata)
                    {
                        continue;
                    }
                    ++postBuildCount;
                }
                if (0 < postBuildCount)
                {
                    lastTargetName = $"{project.targetName_}_main";
                }
            }
            // Final target
            switch (buildType)
            {
                case BuildType.Application:
                    {
                        stringBuilder.AppendLine($"  Executable('{lastTargetName}')");
                        stringBuilder.AppendLine("  {");
                        if (!string.IsNullOrEmpty(dependenciesName))
                        {
                            stringBuilder.AppendLine("    .PreBuildDependencies =");
                            stringBuilder.AppendLine("    {");
                            if (string.IsNullOrEmpty(fxctargets_deps))
                            {
                                stringBuilder.AppendLine($"      '{dependenciesName}'");
                            }
                            else
                            {
                                stringBuilder.AppendLine($"      '{dependenciesName}',");
                                stringBuilder.AppendLine($"      '{fxctargets_deps}'");
                            }
                            stringBuilder.AppendLine("    }");
                        }
                        stringBuilder.AppendLine($"    .Environment = .{buildContext.envName_}");
                        stringBuilder.AppendLine($"    .Linker = '{buildContext.LinkerPath_}'");
                        ProjectItemDefinition linkDefinitions = buildProject.ItemDefinitions["Link"];
                        string outputFile = linkDefinitions.GetMetadataValue("OutputFile");
                        string outputDirectory = System.IO.Path.GetDirectoryName(outputFile);
                        if (!System.IO.Directory.Exists(outputDirectory))
                        {
                            System.IO.Directory.CreateDirectory(outputDirectory);
                        }
                        string profileGuidedDatabase = linkDefinitions.GetMetadataValue("ProfileGuidedDatabase");
                        string ilkDBFile = System.IO.Path.Combine(project.rootDir_, linkDefinitions.GetMetadataValue("IncrementalLinkDatabaseFile"));
                        string ltcgObjFile = System.IO.Path.Combine(project.rootDir_, linkDefinitions.GetMetadataValue("LinkTimeCodeGenerationObjectFile"));
                        string ltcgOptim = linkDefinitions.GetMetadataValue("LinkTimeCodeGeneration");
                        ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.Link"));
                        string linkerOptions = GenerateTaskCommandLine(task, new string[] { "OutputFile", "ProfileGuidedDatabase", "ProgramDataBaseFileName", "XMLDocumentationFileName", "DiagnosticsFormat", "LinkTimeCodeGenerationObjectFile", "IncrementalLinkDatabaseFile" }, linkDefinitions.Metadata);
                        linkerOptions = linkerOptions.Replace("'", "^'");
                        StringBuilder optionBuilder = buildContext.optionBuilder_.Clear();
                        optionBuilder.Append("\"%1[0]\" /OUT:\"%2\" /pdb:$LinkerPDB$");
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

                        stringBuilder.AppendLine($"    .LinkerOptions = '{optionBuilder.ToString()}'");
                        stringBuilder.AppendLine($"    .LinkerOutput = '{outputFile}'");

                        stringBuilder.AppendLine("    .Libraries =");
                        stringBuilder.AppendLine("    {");
                        addStringList(stringBuilder, objTargets, "      ");
                        stringBuilder.AppendLine("    }");
                        stringBuilder.AppendLine("    .LinkerType = 'msvc'");
                        stringBuilder.AppendLine("    .LinkerLinkObjects = false");
                        stringBuilder.AppendLine("  }");
                        buildContext.targets_.Add(outputFile);
                    }
                    break;
                case BuildType.StaticLib:
                    {
                        string compilerOptions = project.CompileItems.Where(el => el.Type == ItemType.CXX).First().Options;
                        stringBuilder.AppendLine($"  Library('{lastTargetName}')");
                        stringBuilder.AppendLine("  {");
                        if (!string.IsNullOrEmpty(dependenciesName))
                        {
                            stringBuilder.AppendLine("    .PreBuildDependencies =");
                            stringBuilder.AppendLine("    {");
                            if (string.IsNullOrEmpty(fxctargets_deps))
                            {
                                stringBuilder.AppendLine($"      '{dependenciesName}'");
                            }
                            else
                            {
                                stringBuilder.AppendLine($"      '{dependenciesName}',");
                                stringBuilder.AppendLine($"      '{fxctargets_deps}'");
                            }
                            stringBuilder.AppendLine("    }");
                        }
                        stringBuilder.AppendLine($"    .Compiler = .{buildContext.compilerDummyName_}");
                        if (!string.IsNullOrEmpty(compilerOptions))
                        {
                            stringBuilder.AppendLine($"    .CompilerOptions = '\"%1\" /Fo\"%2\" /c {compilerOptions}'");
                        }
                        stringBuilder.AppendLine($"    .CompilerOutputPath = '{project.intDir_}'");
                        stringBuilder.AppendLine($"    .Environment = .{buildContext.envName_}");
                        stringBuilder.AppendLine($"    .Librarian = '{buildContext.LibrarianPath_}'");

                        ProjectItemDefinition libDefinitions = buildProject.ItemDefinitions["Lib"];
                        string ltcgOptim = libDefinitions.GetMetadataValue("LinkTimeCodeGeneration");
                        ToolTask task = (ToolTask)Activator.CreateInstance(buildContext.CppTaskAssembly_.GetType("Microsoft.Build.CPPTasks.LIB"));
                        string linkerOptions = GenerateTaskCommandLine(task, new string[] { "OutputFile", "ProgramDataBaseFileName", "XMLDocumentationFileName", "DiagnosticsFormat" }, libDefinitions.Metadata);
                        StringBuilder optionBuilder = buildContext.optionBuilder_.Clear();
                        string outputFile = System.IO.Path.GetFullPath(System.IO.Path.Combine(project.rootDir_, libDefinitions.GetMetadataValue("OutputFile")));
                        optionBuilder.Append("\"%1\" /OUT:\"%2\"");
                        if (!string.IsNullOrEmpty(ltcgOptim) && ltcgOptim == "true")
                        {
                            optionBuilder.Append(" /LTCG");
                        }
                        optionBuilder.Append($" {linkerOptions}");
                        stringBuilder.AppendLine($"    .LibrarianOptions = '{optionBuilder.ToString()}'");
                        stringBuilder.AppendLine($"    .LibrarianOutput = '{outputFile}'");

                        stringBuilder.AppendLine("    .LibrarianAdditionalInputs =");
                        stringBuilder.AppendLine("    {");
                        addStringList(stringBuilder, objTargets, "      ");
                        stringBuilder.AppendLine("    }");
                        stringBuilder.AppendLine("    .LibrarianType = 'msvc'");
                        stringBuilder.AppendLine("    .LibrarianAllowResponseFile = false");
                        stringBuilder.AppendLine("  }");
                        buildContext.targets_.Add(outputFile);
                    }
                    break;
                case BuildType.DynamicLib:
                    {
                        stringBuilder.AppendLine($"  DLL('{lastTargetName}')");
                        stringBuilder.AppendLine("  {");
                        if (!string.IsNullOrEmpty(dependenciesName))
                        {
                            stringBuilder.AppendLine("    .PreBuildDependencies =");
                            stringBuilder.AppendLine("    {");
                            if (string.IsNullOrEmpty(fxctargets_deps))
                            {
                                stringBuilder.AppendLine($"      '{dependenciesName}'");
                            }
                            else
                            {
                                stringBuilder.AppendLine($"      '{dependenciesName}',");
                                stringBuilder.AppendLine($"      '{fxctargets_deps}'");
                            }
                            stringBuilder.AppendLine("    }");
                        }
                        stringBuilder.AppendLine($"    .Environment = .{buildContext.envName_}");
                        stringBuilder.AppendLine($"    .Linker = '{buildContext.LinkerPath_}'");
                        ProjectItemDefinition linkDefinitions = buildProject.ItemDefinitions["Link"];
                        string ilkDBFile = System.IO.Path.Combine(project.rootDir_, linkDefinitions.GetMetadataValue("IncrementalLinkDatabaseFile"));
                        string ltcgObjFile = System.IO.Path.Combine(project.rootDir_, linkDefinitions.GetMetadataValue("LinkTimeCodeGenerationObjectFile"));
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
                        optionBuilder.Append("\"%1[0]\" /OUT:\"%2\" /pdb:$LinkerPDB$");
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

                        stringBuilder.AppendLine($"    .LinkerOptions = '{optionBuilder.ToString()}'");
                        stringBuilder.AppendLine($"    .LinkerOutput = '{outputFile}'");

                        stringBuilder.AppendLine("    .Libraries =");
                        stringBuilder.AppendLine("    {");
                        addStringList(stringBuilder, objTargets, "      ");
                        stringBuilder.AppendLine("    }");
                        stringBuilder.AppendLine("    .LinkerType = 'auto'");
                        stringBuilder.AppendLine("    .LinkerLinkObjects = false");
                        stringBuilder.AppendLine("  }");
                        buildContext.targets_.Add(outputFile);
                    }
                    break;
            }

            {
                ICollection<Microsoft.Build.Evaluation.ProjectItem> postBuildEvents = buildProject.GetItems("PostBuildEvent");
                int count = 0;
                List<string> postbuilds = new List<string>();
                foreach (Microsoft.Build.Evaluation.ProjectItem buildEvent in postBuildEvents)
                {
                    ProjectMetadata metadata = buildEvent.GetMetadata("Command");
                    if (null == metadata)
                    {
                        continue;
                    }
                    string command = metadata.EvaluatedValue;
                    string dummy = System.IO.Path.Combine(project.intDir_, $"{project.targetName_}_postbuild_dummy_{count}");
                    TouchFile(dummy);

                    string name = $"{project.targetName_}_postbuild{count}";
                    string postBuildBatchFile = System.IO.Path.Combine(project.intDir_, $"{name}.bat");
                    System.IO.File.WriteAllText(postBuildBatchFile, command);
                    postbuilds.Add(name);
                    stringBuilder.AppendLine($"  Exec('{name}')");
                    stringBuilder.AppendLine("  {");
                    stringBuilder.AppendLine("    .PreBuildDependencies =");
                    stringBuilder.AppendLine("    {");
                    stringBuilder.AppendLine($"      '{lastTargetName}'");
                    stringBuilder.AppendLine("    }");
                    stringBuilder.AppendLine("    .ExecExecutable = 'C:/Windows/System32/cmd.exe'");
                    stringBuilder.AppendLine($"    .ExecArguments = '/C \"%1\"'");
                    stringBuilder.AppendLine($"    .ExecInput = {{'{postBuildBatchFile}'}}");
                    stringBuilder.AppendLine($"    .ExecOutput = '{dummy}'");
                    stringBuilder.AppendLine("    .ExecUseStdOutAsOutput = true");
                    stringBuilder.AppendLine("    .ExecAlways = true");
                    stringBuilder.AppendLine($"    .Environment = .{buildContext.envName_}");
                    stringBuilder.AppendLine("  }");
                    ++count;
                }
                if (0 < postbuilds.Count)
                {
                    string postbuild = $"{project.targetName_}";
                    stringBuilder.AppendLine($"  Alias('{postbuild}')");
                    stringBuilder.AppendLine("  {");
                    stringBuilder.AppendLine("    .Targets =");
                    stringBuilder.AppendLine("    {");
                    addStringList(stringBuilder, postbuilds, "      ");
                    stringBuilder.AppendLine("    }");
                    stringBuilder.AppendLine("  }");
                    project.postDepend_ = postbuild;
                }
            }
            stringBuilder.AppendLine("}");
        }

        private static Dictionary<string, string> GetVCEnvironments(string toolsInstall)
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

                Dictionary<string, string> environments = new Dictionary<string, string>(16);
                if (exitCode != 0)
                {
                    return environments;
                }
                string output = stringOutput.ToString();
                using (StringReader reader = new StringReader(output))
                {
                    while (true)
                    {
                        string line = reader.ReadLine();
                        if (null == line)
                        {
                            break;
                        }
                        if (line.Contains("[vcvarsall.bat]"))
                        {
                            break;
                        }
                    }
                    while (true)
                    {
                        string line = reader.ReadLine();
                        if (null == line)
                        {
                            break;
                        }
                        string[] splits = line.Split('=');
                        if (null == splits || splits.Length < 2)
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
                        environments.Add(splits[0], splits[1]);
                    }
                }
                return environments;
            }
        }

        private static string ExecWhere(BuildContext buildContext, string path)
        {
            if (System.IO.Path.IsPathRooted(path))
            {
                return path;
            }
            foreach (string dir in buildContext.pathes_)
            {
                string fullPath = System.IO.Path.Combine(dir, path);
                if (System.IO.File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            return path;
        }
    }
}
