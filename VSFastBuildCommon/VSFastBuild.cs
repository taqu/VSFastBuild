using Microsoft.Build.Evaluation;
using Microsoft.Build.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace VSFastBuildCommon
{
    public class VSFastBuild
    {
        public const string MSVC = "msvc";
        private enum BuildType
        {
            Application,
            StaticLib,
            DynamicLib
        }

        private enum PrecomiledHeaderType
        {
            NotUsing,
            Create,
            Use,
        }
        private class FastBuildProject
        {
            public Microsoft.Build.Evaluation.Project project_;
            public List<FastBuildProject> dependents_ = new List<FastBuildProject>();
            public string additionalLinks_ = string.Empty;
        }

        public class ObjectListNode
        {
            private string compiler_;
            private string compilerOutputPath_;
            private string compilerOptions_;
            private string compilerOutputExtension_;
            private string precompiledHeaderFile_;
            private List<string> compilerInputFiles_;

            public ObjectListNode(string inputFile, string compiler, string compilerOutputPath, string compilerOptions, string precompiledHeaderFile="", string compilerOutputExtension = "")
            {
                compilerInputFiles_ = new List<string>();
                compilerInputFiles_.Add(inputFile);
                compiler_ = compiler;
                compilerOutputPath_ = compilerOutputPath;
                compilerOptions_ = compilerOptions.Replace("/TP", string.Empty).Replace("/TC", string.Empty);
                compilerOutputExtension_ = compilerOutputExtension;
                precompiledHeaderFile_ = precompiledHeaderFile;

            }

            public bool AddIfMatches(string inputFile, string compiler, string compilerOutputPath, string compilerOptions, string precompiledHeaderFile="")
            {
                if (compiler_ == compiler && compilerOutputPath_ == compilerOutputPath && compilerOptions_ == compilerOptions && precompiledHeaderFile_ == precompiledHeaderFile)
                {
                    compilerInputFiles_.Add(inputFile);
                    return true;
                }
                return false;
            }

            public string ToString(int actionNumber, string preBuildBatchFile, bool unityBuild)
            {
                StringBuilder stringBuilder = new StringBuilder(128);
                bool usedUnity = false;
                if (unityBuild && compiler_ != "rc" && 1 < compilerInputFiles_.Count)
                {
                    stringBuilder.AppendFormat("Unity('unity_{0}')\n{{\n", actionNumber);
                    stringBuilder.AppendFormat("\t.UnityInputFiles = {{ {0} }}\n", string.Join(",", compilerInputFiles_.ConvertAll(el => string.Format("'{0}'", el)).ToArray()));
                    stringBuilder.AppendFormat("\t.UnityOutputPath = \"{0}\"\n", compilerOutputPath_);
                    stringBuilder.AppendFormat("\t.UnityNumFiles = {0}\n", 1 + compilerInputFiles_.Count / 10);
                    stringBuilder.Append("}\n\n");
                    usedUnity = true;
                }

                stringBuilder.AppendFormat("ObjectList('action_{0}')\n{{\n", actionNumber);
                stringBuilder.AppendFormat("\t.Compiler = '{0}'\n", compiler_);
                stringBuilder.AppendFormat("\t.CompilerOutputPath = \"{0}\"\n", compilerOutputPath_);
                if (usedUnity)
                {
                    stringBuilder.AppendFormat("\t.CompilerInputUnity = {{ {0} }}\n", string.Format("'unity_{0}'", actionNumber));
                }
                else
                {
                    stringBuilder.AppendFormat("\t.CompilerInputFiles = {{ {0} }}\n", string.Join(",", compilerInputFiles_.ConvertAll(el => string.Format("'{0}'", el)).ToArray()));
                }
                stringBuilder.AppendFormat("\t.CompilerOptions = '{0}'\n", compilerOptions_);
                if (!string.IsNullOrEmpty(compilerOutputExtension_))
                {
                    stringBuilder.AppendFormat("\t.CompilerOutputExtension = '{0}'\n", compilerOutputExtension_);
                }
                if (!string.IsNullOrEmpty(precompiledHeaderFile_))
                {
                    stringBuilder.AppendFormat("\t.PCHOutputFile = '{0}'\n", precompiledHeaderFile_);
                }
                if (!string.IsNullOrEmpty(preBuildBatchFile))
                {
                    stringBuilder.Append("\t.PreBuildDependencies  = 'prebuild'\n");
                }
                stringBuilder.Append("}\n\n");
                return stringBuilder.ToString();
            }
        }

        public string RootDirectory
        {
            get
            {
                return rootDirectory_;
            }
            set
            {
                rootDirectory_ = GetDirectoryPath(value);
            }
        }

        private string GetDirectoryPath(string path)
        {
            path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (path.Last() != Path.DirectorySeparatorChar)
            {
                path += Path.DirectorySeparatorChar;
            }
            return path;
        }

        public bool GenerateOnly { get; set; } = false;
        public List<string> ProjectFiles { get; private set; } = new List<string>();
        public string Configuration { get; set; } = "Debug";
        public string Platform { get; set; } = "x64";
        public string FBuildPath { get; set; } = "FBuild.exe";
        public string FBuildArgs { get; set; } = "-dist -cache";
        public bool UnityBuild { get; set; } = false;
        private string rootDirectory_ = string.Empty;
        private VSEnvironment vSEnvironment_;
        private string VCTargetsPath_;
        private string MSBuildPath_;

        public VSFastBuild()
        {
            vSEnvironment_ = VSEnvironment.Create("17", "10.0");
            string vsVersion = string.Format("v{0}0", vSEnvironment_.VSVersion);
            VCTargetsPath_ = Path.Combine(vSEnvironment_.ToolsInstall, "MSBuild", "Microsoft", "VC", vsVersion) + Path.DirectorySeparatorChar;
            MSBuildPath_ = Path.Combine(vSEnvironment_.ToolsInstall, "MSBuild", "Current", "Bin");
        }

        private static bool HasFileChanged(string inputFile, string platform, string config, string bbfOutputFilePath, out string hash)
        {
            System.Security.Cryptography.SHA256Managed sha256managed = new System.Security.Cryptography.SHA256Managed();
            using (FileStream stream = File.OpenRead(inputFile))
            {
                byte[] computedhash = sha256managed.ComputeHash(stream);
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.AppendFormat("// {0}_{1}_{2}_", inputFile, platform, config);
                foreach (byte b in computedhash)
                {
                    stringBuilder.AppendFormat("{0:X2}", b);
                }
                hash = stringBuilder.ToString();
            }
            if (!File.Exists(bbfOutputFilePath))
            {
                return true;
            }

            string firstLine = File.ReadLines(bbfOutputFilePath).First();
            return firstLine != hash;
        }

        public IEnumerable<Process> Build()
        {
            List<FastBuildProject> projects = new List<FastBuildProject>();
            bool hasCompileActions = true;
            foreach (string projectPath in ProjectFiles)
            {
                EvaluateProjectReferences(projectPath, projects, null);
            }

            string bffSafix = "_" + Configuration.Replace(" ", string.Empty) + "_" + Platform.Replace(" ", string.Empty) + ".bff";

            int projectsBuilt = 0;
            foreach (FastBuildProject project in projects)
            {
                string VCTargetsPath = project.project_.GetPropertyValue("VCTargetsPathEffective");
                if (string.IsNullOrEmpty(VCTargetsPath))
                {
                    VCTargetsPath = project.project_.GetPropertyValue("VCTargetsPath");
                }
                if (string.IsNullOrEmpty(VCTargetsPath))
                {
#if DEBUG
                    Console.WriteLine("Failed to evaluate VCTargetsPath variable on " + Path.GetFileName(project.project_.FullPath) + "!");
#endif
                    continue;
                }

                bool foundDll = false;
                string BuildDllName = "Microsoft.Build.CPPTasks.Common.dll";
                string BuildDllPath = Path.Combine(VCTargetsPath, BuildDllName);
                Assembly CPPTasksAssembly = null;
                if (File.Exists(BuildDllPath))
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
                            foundDll = true;
                        }
                    }
                }
                if (!foundDll)
                {
#if DEBUG
                    Console.WriteLine("Failed to find dll " + BuildDllPath);
#endif
                    continue;
                }

                string bffOutputFilePath = Path.Combine(Path.GetDirectoryName(project.project_.FullPath), Path.GetFileName(project.project_.FullPath) + bffSafix);
                string VCBasePath = string.Empty;
                string WindowsSDKTarget = string.Empty;
                GenerateBffFromVcxproj(bffOutputFilePath, project, CPPTasksAssembly, ref hasCompileActions, out VCBasePath, out WindowsSDKTarget);

                if (!GenerateOnly)
                {
                    if (hasCompileActions)
                    {
                        System.Diagnostics.Process FBProcess = ExecuteBffFile(bffOutputFilePath, project.project_.FullPath, Platform, VCBasePath, WindowsSDKTarget);
                        if (null == FBProcess)
                        {
                            break;
                        }
                        ++projectsBuilt;
                        yield return FBProcess;
                    }
                    else
                    {
                        ++projectsBuilt;
                    }
                }
            }

#if DEBUG
            Console.WriteLine(projectsBuilt + "/" + projects.Count + " built.");
#endif
        }

        private void EvaluateProjectReferences(string projectPath, List<FastBuildProject> evaluatedProjects, FastBuildProject dependent)
        {
            if (!string.IsNullOrEmpty(projectPath) && File.Exists(projectPath))
            {
                try
                {
                    FastBuildProject newProj = evaluatedProjects.Find(elem => elem.project_.FullPath == Path.GetFullPath(projectPath));
                    if (null != newProj)
                    {
                        if (dependent != null)
                        {
                            newProj.dependents_.Add(dependent);
                        }
                    }
                    else
                    {
                        ProjectCollection projColl = new ProjectCollection();
                        if (string.IsNullOrEmpty(RootDirectory))
                        {
                            projColl.SetGlobalProperty("SolutionDir", GetDirectoryPath(projectPath));

                        }
                        else
                        {
                            projColl.SetGlobalProperty("SolutionDir", RootDirectory);
                        }
                        if (!string.IsNullOrEmpty(VCTargetsPath_))
                        {
                            projColl.SetGlobalProperty("VCTargetsPath", VCTargetsPath_);
                            //Environment.SetEnvironmentVariable("VCTargetsPath", VCTargetsPath_);
                        }
                        if (!string.IsNullOrEmpty(MSBuildPath_))
                        {
                            //projColl.SetGlobalProperty("MSBuild", MSBuildPath_);
                            //Environment.SetEnvironmentVariable("MSBuild", MSBuildPath_);
                            //Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", MSBuildPath_);
                        }
                        newProj = new FastBuildProject();
                        Microsoft.Build.Evaluation.Project proj = projColl.LoadProject(projectPath);

                        if (null != proj)
                        {
                            proj.SetGlobalProperty("Configuration", Configuration);
                            proj.SetGlobalProperty("Platform", Platform);
                            if (string.IsNullOrEmpty(RootDirectory))
                            {
                                proj.SetGlobalProperty("SolutionDir", GetDirectoryPath(projectPath));
                            }
                            else
                            {
                                proj.SetGlobalProperty("SolutionDir", RootDirectory);
                            }
                            proj.ReevaluateIfNecessary();

                            newProj.project_ = proj;
                            if (null != dependent)
                            {
                                newProj.dependents_.Add(dependent);
                            }
                            IEnumerable<ProjectItem> projectReferences = proj.Items.Where(item => item.ItemType == "ProjectReference");
                            foreach (ProjectItem projItem in projectReferences)
                            {
                                if (projItem.GetMetadataValue("ReferenceOutputAssembly") == "true" || projItem.GetMetadataValue("LinkLibraryDependencies") == "true")
                                {
                                    EvaluateProjectReferences(Path.GetDirectoryName(proj.FullPath) + Path.DirectorySeparatorChar + projItem.EvaluatedInclude, evaluatedProjects, newProj);
                                }
                            }
                            evaluatedProjects.Add(newProj);
                        }
                    }
                }
                catch (Exception e)
                {
#if DEBUG
                    Console.WriteLine("Failed to parse project file " + projectPath + "!");
                    Console.WriteLine("StackTrace: " + e.StackTrace);
                    Console.WriteLine("Exception: " + e.Message);
#endif
                    return;
                }
            }
        }

        private static void AddExtraDlls(StringBuilder outputString, string rootDir, string pattern)
        {
            string[] dllFiles = Directory.GetFiles(rootDir, pattern);
            foreach (string dllFile in dllFiles)
            {
                outputString.AppendFormat("\t\t'$Root$/{0}'\n", Path.GetFileName(dllFile));
            }
        }

        private void GenerateBffFromVcxproj(
            string bffOutputFilePath,
            FastBuildProject project,
            Assembly CPPTasksAssembly,
            ref bool hasCompileActions,
            out string VCBasePath,
            out string WindowsSDKTarget)
        {
            string config = Configuration;
            string platform = Platform;
            Microsoft.Build.Evaluation.Project activeProject = project.project_;
            string filehash = string.Empty;
            string preBuildBatchFile = string.Empty;
            string postBuildBatchFile = string.Empty;
            bool fileChanged = HasFileChanged(activeProject.FullPath, platform, config, bffOutputFilePath, out filehash);
            VCBasePath = string.Empty;
            WindowsSDKTarget = string.Empty;

            string configType = activeProject.GetProperty("ConfigurationType").EvaluatedValue;
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

            string intDir = activeProject.GetProperty("IntDir").EvaluatedValue;
            string outDir = activeProject.GetProperty("OutDir").EvaluatedValue;

            StringBuilder outputString = new StringBuilder(filehash + "\n\n");

            outputString.AppendFormat(".VSBasePath = '{0}'\n", activeProject.GetProperty("VSInstallDir").EvaluatedValue);
            VCBasePath = activeProject.GetProperty("VCInstallDir").EvaluatedValue;
            outputString.AppendFormat(".VCBasePath = '{0}'\n", VCBasePath);

            string VCExePath = string.Empty;
            if (platform == "Win32" || platform == "x86")
            {
                VCExePath = activeProject.GetProperty("VC_ExecutablePath_x86_x86").EvaluatedValue;
            }
            else
            {
                VCExePath = activeProject.GetProperty("VC_ExecutablePath_x64_x64").EvaluatedValue;
            }
            outputString.AppendFormat(".VCExePath = '{0}'\n", VCExePath);

            WindowsSDKTarget = activeProject.GetProperty("WindowsTargetPlatformVersion") != null ? activeProject.GetProperty("WindowsTargetPlatformVersion").EvaluatedValue : "10.0";

            string winSdkDir = activeProject.GetProperty("WindowsSdkDir").EvaluatedValue;
            outputString.AppendFormat(".WindowsSDKBasePath = '{0}'\n\n", winSdkDir);

            outputString.Append("Settings\n{\n\t.Environment = \n\t{\n");
            outputString.AppendFormat("\t\t\"INCLUDE={0}\",\n", activeProject.GetProperty("IncludePath").EvaluatedValue);
            outputString.AppendFormat("\t\t\"LIB={0}\",\n", activeProject.GetProperty("LibraryPath").EvaluatedValue);
            outputString.AppendFormat("\t\t\"LIBPATH={0}\",\n", activeProject.GetProperty("ReferencePath").EvaluatedValue);
            outputString.AppendFormat("\t\t\"PATH={0}\"\n", activeProject.GetProperty("Path").EvaluatedValue);
            outputString.AppendFormat("\t\t\"TMP={0}\"\n", activeProject.GetProperty("Temp").EvaluatedValue);
            outputString.AppendFormat("\t\t\"TEMP={0}\"\n", activeProject.GetProperty("Temp").EvaluatedValue);
            outputString.AppendFormat("\t\t\"SystemRoot={0}\"\n", activeProject.GetProperty("SystemRoot").EvaluatedValue);
            outputString.Append("\t}\n}\n\n");

            StringBuilder compilerString = new StringBuilder("Compiler('msvc')\n{\n");

            string compilerRoot = VCExePath;
            compilerString.Append("\t.Root = '$VCExePath$'\n");
            compilerString.Append("\t.Executable = '$Root$/cl.exe'\n");
            compilerString.Append("\t.ExtraFiles =\n\t{\n");
            compilerString.Append("\t\t'$Root$/c1.dll'\n");
            compilerString.Append("\t\t'$Root$/c1xx.dll'\n");
            compilerString.Append("\t\t'$Root$/c2.dll'\n");

            if (File.Exists(compilerRoot + "1041/clui.dll")) //Check English first...
            {
                compilerString.Append("\t\t'$Root$/1041/clui.dll'\n");
            }
            else
            {
                var numericDirectories = Directory.GetDirectories(compilerRoot).Where(d => Path.GetFileName(d).All(char.IsDigit));
                var cluiDirectories = numericDirectories.Where(d => Directory.GetFiles(d, "clui.dll").Any());
                if (cluiDirectories.Any())
                {
                    compilerString.AppendFormat("\t\t'$Root$/{0}/clui.dll'\n", Path.GetFileName(cluiDirectories.First()));
                }
            }

            compilerString.Append("\t\t'$Root$/mspdbsrv.exe'\n");
            //CompilerString.Append("\t\t'$Root$/mspdbcore.dll'\n");

            //CompilerString.AppendFormat("\t\t'$Root$/mspft{0}.dll'\n", PlatformToolsetVersion);
            //CompilerString.AppendFormat("\t\t'$Root$/msobj{0}.dll'\n", PlatformToolsetVersion);
            //CompilerString.AppendFormat("\t\t'$Root$/mspdb{0}.dll'\n", PlatformToolsetVersion);
            //CompilerString.AppendFormat("\t\t'$VSBasePath$/VC/redist/{0}/Microsoft.VC{1}.CRT/msvcp{1}.dll'\n", Platform == "Win32" ? "x86" : "x64", PlatformToolsetVersion);
            //CompilerString.AppendFormat("\t\t'$VSBasePath$/VC/redist/{0}/Microsoft.VC{1}.CRT/vccorlib{1}.dll'\n", Platform == "Win32" ? "x86" : "x64", PlatformToolsetVersion);

            AddExtraDlls(compilerString, compilerRoot, "msobj*.dll");
            AddExtraDlls(compilerString, compilerRoot, "mspdb*.dll");
            AddExtraDlls(compilerString, compilerRoot, "mspft*.dll");
            AddExtraDlls(compilerString, compilerRoot, "msvcp*.dll");
            AddExtraDlls(compilerString, compilerRoot, "tbbmalloc.dll");
            AddExtraDlls(compilerString, compilerRoot, "vcmeta.dll");
            AddExtraDlls(compilerString, compilerRoot, "vcruntime*.dll");

            compilerString.Append("\t}\n"); //End extra files
            compilerString.Append("}\n\n"); //End compiler

            string rcPath = "\\bin\\" + WindowsSDKTarget + "\\x64\\rc.exe";
            if (!File.Exists(winSdkDir + rcPath))
            {
                rcPath = "\\bin\\x64\\rc.exe";
            }

            compilerString.Append("Compiler('rc')\n{\n");
            compilerString.Append("\t.Executable = '$WindowsSDKBasePath$" + rcPath + "'\n");
            compilerString.Append("\t.CompilerFamily = 'custom'\n");
            compilerString.Append("}\n\n"); //End rc compiler

            outputString.Append(compilerString);

            if (activeProject.GetItems("PreBuildEvent").Any())
            {
                var buildEvent = activeProject.GetItems("PreBuildEvent").First();
                if (buildEvent.Metadata.Any())
                {
                    var mdPi = buildEvent.Metadata.First();
                    if (!string.IsNullOrEmpty(mdPi.EvaluatedValue))
                    {
                        string BatchText = "call \"" + VCBasePath + "Auxiliary\\Build\\vcvarsall.bat\" " + (platform == "Win32" ? "x86" : "x64") + " " + WindowsSDKTarget + "\n";
                        preBuildBatchFile = Path.Combine(activeProject.DirectoryPath, Path.GetFileNameWithoutExtension(activeProject.FullPath) + "_prebuild.bat");
                        File.WriteAllText(preBuildBatchFile, BatchText + mdPi.EvaluatedValue);
                        outputString.Append("Exec('prebuild')\n{\n");
                        outputString.AppendFormat("\t.ExecExecutable = '{0}'\n", preBuildBatchFile);
                        outputString.AppendFormat("\t.ExecInput = '{0}'\n", preBuildBatchFile);
                        outputString.AppendFormat("\t.ExecOutput = '{0}'\n", preBuildBatchFile + ".txt");
                        outputString.Append("\t.ExecUseStdOutAsOutput = true\n");
                        outputString.Append("}\n\n");
                    }
                }
            }

            string CompilerOptions = string.Empty;

            List<ObjectListNode> objectLists = new List<ObjectListNode>();
            ICollection<ProjectItem> compileItems = activeProject.GetItems("ClCompile");
            List<Tuple<string, string, string>> precompiledHeaderBuilds = new List<Tuple<string, string, string>>();

            foreach (ProjectItem item in compileItems)
            {
                string evalInclude = item.EvaluatedInclude;
                if (item.DirectMetadata.Any())
                {
                    if (item.DirectMetadata.Where(dmd => dmd.Name == "ExcludedFromBuild" && dmd.EvaluatedValue == "true").Any()) {
                        continue;
                    }
                }
                if (item.Metadata.Where(dmd => dmd.Name == "PrecompiledHeader" && dmd.EvaluatedValue == "Create").Any())
                {
                    ToolTask CLtask = (ToolTask)Activator.CreateInstance(CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.CL"));
                    CLtask.GetType().GetProperty("Sources").SetValue(CLtask, new TaskItem[] { new TaskItem() });
                    string pchCompilerOptions = GenerateTaskCommandLine(CLtask, new string[] { "PrecompiledHeaderOutputFile", "ObjectFileName", "AssemblerListingLocation" }, item.Metadata) + " /FS";
                    pchCompilerOptions = pchCompilerOptions.Replace("/TP", string.Empty);
                    //PrecompiledHeaderString = "\t.PCHOptions = '" + string.Format("\"%1\" /Fp\"%2\" /Fo\"%3\" {0} '\n", pchCompilerOptions);
                    //PrecompiledHeaderString += "\t.PCHInputFile = '" + Item.EvaluatedInclude + "'\n";
                    //PrecompiledHeaderString += "\t.PCHOutputFile = '" + Item.GetMetadataValue("PrecompiledHeaderOutputFile") + "'\n";
                    Tuple<string, string, string> newPrecompiledHeaderBuild = new Tuple<string, string, string>(item.GetMetadataValue("PrecompiledHeaderFile"), item.GetMetadataValue("PrecompiledHeaderOutputFile"), pchCompilerOptions);
                    if (null == precompiledHeaderBuilds.Find((x) => newPrecompiledHeaderBuild.Equals(x)))
                    {
                        precompiledHeaderBuilds.Add(newPrecompiledHeaderBuild);
                    }
                }
            }

            int precompiledHeaderBuildIndex = 0;
            foreach (Tuple<string, string, string> precompiledHeaderBuild in precompiledHeaderBuilds)
            {
                outputString.AppendFormat("ObjectList('createPCH{0}'){{\n", precompiledHeaderBuildIndex);
                outputString.AppendFormat("\t.PCHInputFile = '{0}'\n", precompiledHeaderBuild.Item1);
                outputString.AppendFormat("\t.PCHOutputFile = '{0}'\n", precompiledHeaderBuild.Item2);
                if (!string.IsNullOrEmpty(precompiledHeaderBuild.Item3)) {
                    outputString.AppendFormat("\t.PCHOptions = '{0}'\n", precompiledHeaderBuild.Item3);
                }
                outputString.Append("}\n");
                ++precompiledHeaderBuildIndex;
            }

            foreach (var Item in compileItems)
            {
                string pchFile = string.Empty;
                if (Item.DirectMetadata.Any())
                {
                    if (Item.DirectMetadata.Where(dmd => dmd.Name == "ExcludedFromBuild" && dmd.EvaluatedValue == "true").Any())
                    {
                        continue;
                    }
                }
                {
                    if (Item.Metadata.Where(dmd => dmd.Name == "PrecompiledHeader" && dmd.EvaluatedValue == "Use").Any())
                    {
                        pchFile = Item.GetMetadataValue("PrecompiledHeaderOutputFile");
                    }
                    if (Item.Metadata.Where(dmd => dmd.Name == "PrecompiledHeader" && dmd.EvaluatedValue == "Create").Any())
                    {
                        pchFile = Item.GetMetadataValue("PrecompiledHeaderOutputFile");
                    }
                    if (Item.Metadata.Where(dmd => dmd.Name == "PrecompiledHeader" && dmd.EvaluatedValue == "NotUsing").Any())
                    {
                        pchFile = string.Empty;
                    }
                }

                ToolTask Task = (ToolTask)Activator.CreateInstance(CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.CL"));
                Task.GetType().GetProperty("Sources").SetValue(Task, new TaskItem[] { new TaskItem() }); //CPPTasks throws an exception otherwise...
                string TempCompilerOptions = GenerateTaskCommandLine(Task, new string[] { "ObjectFileName", "AssemblerListingLocation" }, Item.Metadata) + " /FS";
                //if (Path.GetExtension(Item.EvaluatedInclude) == ".c")
                //    TempCompilerOptions += " /TC";
                //else
                //    TempCompilerOptions += " /TP";
                CompilerOptions = TempCompilerOptions;
                string FormattedCompilerOptions = string.Format("\"%1\" /Fo\"%2\" {0}", TempCompilerOptions);
                var MatchingNodes = objectLists.Where(el => el.AddIfMatches(Item.EvaluatedInclude, "msvc", intDir, FormattedCompilerOptions, pchFile));
                if (!MatchingNodes.Any())
                {
                    objectLists.Add(new ObjectListNode(Item.EvaluatedInclude, "msvc", intDir, FormattedCompilerOptions, pchFile));
                }
            }

            var ResourceCompileItems = activeProject.GetItems("ResourceCompile");
            foreach (var Item in ResourceCompileItems)
            {
                if (Item.DirectMetadata.Any())
                {
                    if (Item.DirectMetadata.Where(dmd => dmd.Name == "ExcludedFromBuild" && dmd.EvaluatedValue == "true").Any()) {
                        continue;
                        }
                }

                ToolTask Task = (ToolTask)Activator.CreateInstance(CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.RC"));
                string ResourceCompilerOptions = GenerateTaskCommandLine(Task, new string[] { "ResourceOutputFileName", "DesigntimePreprocessorDefinitions" }, Item.Metadata);

                string formattedCompilerOptions = string.Format("{0} /fo\"%2\" \"%1\"", ResourceCompilerOptions);
                var MatchingNodes = objectLists.Where(el => el.AddIfMatches(Item.EvaluatedInclude, "rc", intDir, formattedCompilerOptions, string.Empty));
                if (!MatchingNodes.Any())
                {
                    objectLists.Add(new ObjectListNode(Item.EvaluatedInclude, "rc", intDir, formattedCompilerOptions, string.Empty, ".res"));
                }
            }

            int actionNumber = 0;
            foreach (ObjectListNode ObjList in objectLists)
            {
                outputString.Append(ObjList.ToString(actionNumber, preBuildBatchFile, UnityBuild));
                actionNumber++;
            }

            if (0 < actionNumber)
            {
                hasCompileActions = true;
            }
            else
            {
                hasCompileActions = false;
            }

            string CompileActions = string.Join(",", Enumerable.Range(0, actionNumber).ToList().ConvertAll(x => string.Format("'action_{0}'", x)).ToArray());

            if (buildType == BuildType.Application || buildType == BuildType.DynamicLib)
            {
                outputString.AppendFormat("{0}('output')\n{{", buildType == BuildType.Application ? "Executable" : "DLL");
                outputString.Append("\t.Linker = '$VCExePath$\\link.exe'\n");

                ProjectItemDefinition LinkDefinitions = activeProject.ItemDefinitions["Link"];
                string OutputFile = LinkDefinitions.GetMetadataValue("OutputFile").Replace('\\', '/');
                string OutputDirectory = Path.GetDirectoryName(OutputFile);
                if (!System.IO.Directory.Exists(OutputDirectory))
                {
                    System.IO.Directory.CreateDirectory(OutputDirectory);
                }

                if (hasCompileActions)
                {
                    string DependencyOutputPath = LinkDefinitions.GetMetadataValue("ImportLibrary");
                    if (Path.IsPathRooted(DependencyOutputPath))
                    {
                        DependencyOutputPath = DependencyOutputPath.Replace('\\', '/');
                    }
                    else
                    {
                        DependencyOutputPath = Path.Combine(activeProject.DirectoryPath, DependencyOutputPath).Replace('\\', '/');
                    }

                    foreach (var deps in project.dependents_)
                    {
                        deps.additionalLinks_ += " \"" + DependencyOutputPath + "\" ";
                    }
                }

                ToolTask Task = (ToolTask)Activator.CreateInstance(CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.Link"));
                string LinkerOptions = GenerateTaskCommandLine(Task, new string[] { "OutputFile", "ProfileGuidedDatabase" }, LinkDefinitions.Metadata);

                if (!string.IsNullOrEmpty(project.additionalLinks_))
                {
                    LinkerOptions += project.additionalLinks_;
                }
                outputString.AppendFormat("\t.LinkerOptions = '\"%1\" /OUT:\"%2\" {0}'\n", LinkerOptions.Replace("'", "^'"));
                outputString.AppendFormat("\t.LinkerOutput = '{0}'\n", OutputFile);

                outputString.Append("\t.Libraries = { ");
                outputString.Append(CompileActions);
                outputString.Append(" }\n");

                outputString.Append("}\n\n");
            }
            else if (buildType == BuildType.StaticLib)
            {
                outputString.Append("Library('output')\n{");
                outputString.Append("\t.Compiler = 'msvc'\n");
                outputString.Append(string.Format("\t.CompilerOptions = '\"%1\" /Fo\"%2\" /c {0}'\n", CompilerOptions));
                outputString.Append(string.Format("\t.CompilerOutputPath = \"{0}\"\n", intDir));
                outputString.Append("\t.Librarian = '$VCExePath$\\lib.exe'\n");

                var LibDefinitions = activeProject.ItemDefinitions["Lib"];
                string OutputFile = LibDefinitions.GetMetadataValue("OutputFile").Replace('\\', '/');
                string OutputDirectory = Path.GetDirectoryName(OutputFile);
                if (!System.IO.Directory.Exists(OutputDirectory))
                {
                    System.IO.Directory.CreateDirectory(OutputDirectory);
                }

                if (hasCompileActions)
                {
                    string DependencyOutputPath = "";
                    if (Path.IsPathRooted(OutputFile)) {
                        DependencyOutputPath = Path.GetFullPath(OutputFile).Replace('\\', '/');
                    }
                    else {
                        DependencyOutputPath = Path.Combine(activeProject.DirectoryPath, OutputFile).Replace('\\', '/');
                        }

                    foreach (var deps in project.dependents_)
                    {
                        deps.additionalLinks_ += " \"" + DependencyOutputPath + "\" ";
                    }
                }

                ToolTask task = (ToolTask)Activator.CreateInstance(CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.LIB"));
                string linkerOptions = GenerateTaskCommandLine(task, new string[] { "OutputFile" }, LibDefinitions.Metadata);
                if (!string.IsNullOrEmpty(project.additionalLinks_))
                {
                    linkerOptions += project.additionalLinks_;
                }
                outputString.AppendFormat("\t.LibrarianOptions = '\"%1\" /OUT:\"%2\" {0}'\n", linkerOptions);
                outputString.AppendFormat("\t.LibrarianOutput = '{0}'\n", OutputFile);

                outputString.Append("\t.LibrarianAdditionalInputs = { ");
                outputString.Append(CompileActions);
                outputString.Append(" }\n");

                outputString.Append("}\n\n");
            }

            if (activeProject.GetItems("PostBuildEvent").Any())
            {
                ProjectItem BuildEvent = activeProject.GetItems("PostBuildEvent").First();
                if (BuildEvent.Metadata.Any())
                {
                    ProjectMetadata MetaData = BuildEvent.Metadata.First();
                    if (!string.IsNullOrEmpty(MetaData.EvaluatedValue))
                    {
                        string BatchText = "call \"" + VCBasePath + "Auxiliary\\Build\\vcvarsall.bat\" "
                            + (platform == "Win32" ? "x86" : "x64") + " " + WindowsSDKTarget + "\n";
                        postBuildBatchFile = Path.Combine(activeProject.DirectoryPath, Path.GetFileNameWithoutExtension(activeProject.FullPath) + "_postbuild.bat");
                        File.WriteAllText(postBuildBatchFile, BatchText + MetaData.EvaluatedValue);
                        outputString.Append("Exec('postbuild') \n{\n");
                        outputString.AppendFormat("\t.ExecExecutable = '{0}' \n", postBuildBatchFile);
                        outputString.AppendFormat("\t.ExecInput = '{0}' \n", postBuildBatchFile);
                        outputString.AppendFormat("\t.ExecOutput = '{0}' \n", postBuildBatchFile + ".txt");
                        outputString.Append("\t.PreBuildDependencies = 'output' \n");
                        outputString.Append("\t.ExecUseStdOutAsOutput = true \n");
                        outputString.Append("}\n\n");
                    }
                }
            }

            outputString.AppendFormat("Alias ('all')\n{{\n\t.Targets = {{ '{0}' }}\n}}", string.IsNullOrEmpty(postBuildBatchFile) ? "output" : "postbuild");

            if (fileChanged)
            {
                File.WriteAllText(bffOutputFilePath, outputString.ToString());
            }
        }

        private string GenerateTaskCommandLine(ToolTask task, string[] propertiesToSkip, IEnumerable<ProjectMetadata> metaDataList)
        {
            foreach (ProjectMetadata metaData in metaDataList)
            {
                if (propertiesToSkip.Contains(metaData.Name))
                {
                    continue;
                }

                IEnumerable<PropertyInfo> matchingProps = task.GetType().GetProperties().Where(prop => prop.Name == metaData.Name);
                if (matchingProps.Any() && !string.IsNullOrEmpty(metaData.EvaluatedValue))
                {
                    string EvaluatedValue = metaData.EvaluatedValue.Trim();
                    if (metaData.Name == "AdditionalIncludeDirectories")
                    {
                        EvaluatedValue = EvaluatedValue.Replace("\\\\", "\\");
                        EvaluatedValue = EvaluatedValue.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }

                    PropertyInfo propInfo = matchingProps.First(); //Dubious
                    if (propInfo.PropertyType.IsArray && propInfo.PropertyType.GetElementType() == typeof(string))
                    {
                        propInfo.SetValue(task, Convert.ChangeType(EvaluatedValue.Split(';'), propInfo.PropertyType));
                    }
                    else
                    {
                        propInfo.SetValue(task, Convert.ChangeType(EvaluatedValue, propInfo.PropertyType));
                    }
                }
            }

            var GenCmdLineMethod = task.GetType().GetRuntimeMethods().Where(method => method.Name == "GenerateCommandLine").First();
            return GenCmdLineMethod.Invoke(task, new object[] { Type.Missing, Type.Missing }) as string;
        }

        private System.Diagnostics.Process ExecuteBffFile(string bffOutputFilePath, string projectPath, string platform, string VCBasePath, string WindowsSDKTarget)
        {
#if true
            string projectDir = Path.GetDirectoryName(projectPath);
            try
            {
                System.Diagnostics.Process FBProcess = new System.Diagnostics.Process();
                FBProcess.StartInfo.FileName = FBuildPath;
                FBProcess.StartInfo.Arguments = "-config \"" + bffOutputFilePath + "\" " + FBuildArgs;
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
#if DEBUG
                Console.WriteLine("Failed to launch FASTBuild!");
                Console.WriteLine("Exception: " + e.Message);
#endif
                return null;
            }

#else
            string projectDir = Path.GetDirectoryName(projectPath) + "\\";

            string BatchFileText = "@echo off\n"
                + "%comspec% /c \"\"" + VCBasePath + "Auxiliary\\Build\\vcvarsall.bat\" "
                + (platform == "Win32" ? "x86" : "x64") + " " + WindowsSDKTarget
                + " && \"" + FBuildPath + "\" %*\"";

            File.WriteAllText(projectDir + "fb.bat", BatchFileText);

#if DEBUG
            Console.WriteLine("Building " + Path.GetFileNameWithoutExtension(projectPath));
#endif

            try
            {
                System.Diagnostics.Process FBProcess = new System.Diagnostics.Process();
                FBProcess.StartInfo.FileName = projectDir + "fb.bat";
                FBProcess.StartInfo.Arguments = "-config \"" + bffOutputFilePath + "\" " + FBuildArgs;
                FBProcess.StartInfo.RedirectStandardOutput = true;
                FBProcess.StartInfo.RedirectStandardError = true;
                FBProcess.StartInfo.UseShellExecute = false;
                FBProcess.StartInfo.WorkingDirectory = projectDir;
                FBProcess.StartInfo.StandardOutputEncoding = Console.OutputEncoding;
                return FBProcess;
            }
            catch (Exception e)
            {
#if DEBUG
                Console.WriteLine("Failed to launch FASTBuild!");
                Console.WriteLine("Exception: " + e.Message);
#endif
                return null;
            }
#endif
        }

    }
}
