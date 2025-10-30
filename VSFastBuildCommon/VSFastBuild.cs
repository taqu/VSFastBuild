using Microsoft.Build.Evaluation;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Data.HashFunction.xxHash;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace VSFastBuildCommon
{
    public class VSFastBuild
    {
        public string RootDirectory
        {
            get
            {
                return rootDirectory_;
            }
            set
            {
                rootDirectory_ = value.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (rootDirectory_.Last() != Path.AltDirectorySeparatorChar)
                {
                    rootDirectory_ += Path.AltDirectorySeparatorChar;
                }
            }
        }
        public List<string> ProjectFiles { get; private set;} = new List<string>();
        public string Configuration { get; set; } = "Debug";
        public string Platform { get; set; } = "x64";
        public string FBuildPath { get; set; } = "FBuild.exe";
        public string FBuildArgs { get; set; } = "-dist";
        public bool UnityBuild { get; set; } = false;

		private enum BuildType
		{
		    Application,
		    StaticLib,
		    DynamicLib
		}

		private class FastBuildProject
		{
			public Microsoft.Build.Evaluation.Project project_;
			public List<FastBuildProject> dependents_ = new List<FastBuildProject>();
			public string additionalLinks_ = string.Empty;
		}

		private static bool HasFileChanged(string inputFile, string platform, string config, string bbfOutputFilePath, out string hash)
		{
			{
                xxHashConfig xxHashConfig = new xxHashConfig()
                {
                    Seed = 12297049036950667264,
                    HashSizeInBits = 64
                };
                IxxHash xxHash = xxHashFactory.Instance.Create(xxHashConfig);
				using (FileStream stream = File.OpenRead(inputFile))
				{
                    System.Data.HashFunction.IHashValue bytehash = xxHash.ComputeHash(stream);
					hash = ";" + inputFile + "_" + platform + "_" + config + "_" + bytehash.AsHexString().ToLower();
				}

            }
			if(!File.Exists(bbfOutputFilePath)) {
				return true;
			}
			
			string firstLine = File.ReadLines(bbfOutputFilePath).First();
			return firstLine != hash;
		}

		public void Build()
		{
			List<FastBuildProject> projects = new List<FastBuildProject>();

			foreach (string projectPath in ProjectFiles)
			{
				EvaluateProjectReferences(projectPath, projects, null);
			}

			string bffSafix = "_" + Configuration.Replace(" ", string.Empty) + "_" + Platform.Replace(" ", string.Empty) + ".bff";

			int projectsBuilt = 0;
			foreach(FastBuildProject project in projects)
			{
				FastBuildProject currentProject = project;

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
					if (CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.CL") != null
						&& CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.RC") != null
						&& CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.Link") != null
						&& CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.LIB") != null)
					{
						foundDll = false;
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
				GenerateBffFromVcxproj(Configuration, Platform);

				if (!CommandLineOptions.GenerateOnly)
				{
					if (HasCompileActions && !ExecuteBffFile(CurrentProject.Proj.FullPath, CommandLineOptions.Platform))
						break;
					else
						projectsBuilt++;
				}
			}

			Console.WriteLine(projectsBuilt + "/" + EvaluatedProjects.Count + " built.");
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
						if (dependent != null) {
							newProj.dependents_.Add(dependent);
						}
					}
					else
					{
						ProjectCollection projColl = new ProjectCollection();
						if (!string.IsNullOrEmpty(RootDirectory)) {
							projColl.SetGlobalProperty("SolutionDir", RootDirectory);
						}
						newProj = new FastBuildProject();
						Project proj = projColl.LoadProject(projectPath);

						if (null != proj)
						{
							proj.SetGlobalProperty("Configuration", Configuration);
							proj.SetGlobalProperty("Platform", Platform);
							if (!string.IsNullOrEmpty(RootDirectory)) {
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
                    Console.WriteLine("Exception: " + e.Message);
#endif
                    return;
				}
			}
		}

		private void GenerateBffFromVcxproj(string config, string platform, string bffOutputFilePath, FastBuildProject project)
		{
			Project activeProject = project.project_;
			string xxHash = string.Empty;
			PreBuildBatchFile = "";
			PostBuildBatchFile = "";
			bool FileChanged = HasFileChanged(activeProject.FullPath, platform, config, bffOutputFilePath, out xxHash);

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

			StringBuilder outputString = new StringBuilder(xxHash + "\n\n");

			outputString.AppendFormat(".VSBasePath = '{0}'\n", activeProject.GetProperty("VSInstallDir").EvaluatedValue);
			string VCBasePath = activeProject.GetProperty("VCInstallDir").EvaluatedValue;
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
			outputString.AppendFormat(".VCExePath = '{0}'\n", VCExePath );

			string WindowsSDKTarget = activeProject.GetProperty("WindowsTargetPlatformVersion") != null ? activeProject.GetProperty("WindowsTargetPlatformVersion").EvaluatedValue : "10.0";

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

			if(File.Exists(compilerRoot + "1033/clui.dll")) //Check English first...
			{
				compilerString.Append("\t\t'$Root$/1033/clui.dll'\n");
			}
			else
			{
				var numericDirectories = Directory.GetDirectories(compilerRoot).Where(d => Path.GetFileName(d).All(char.IsDigit));
				var cluiDirectories = numericDirectories.Where(d => Directory.GetFiles(d, "clui.dll").Any());
				if(cluiDirectories.Any())
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
					if(!string.IsNullOrEmpty(mdPi.EvaluatedValue))
					{
						string BatchText = "call \"" + VCBasePath + "Auxiliary\\Build\\vcvarsall.bat\" "
							+ (platform == "Win32" ? "x86" : "x64") + " " + WindowsSDKTarget + "\n";
						PreBuildBatchFile = Path.Combine(activeProject.DirectoryPath, Path.GetFileNameWithoutExtension(activeProject.FullPath) + "_prebuild.bat");
						File.WriteAllText(PreBuildBatchFile, BatchText + mdPi.EvaluatedValue);						
						outputString.Append("Exec('prebuild') \n{\n");
						outputString.AppendFormat("\t.ExecExecutable = '{0}' \n", PreBuildBatchFile);
						outputString.AppendFormat("\t.ExecInput = '{0}' \n", PreBuildBatchFile);
						outputString.AppendFormat("\t.ExecOutput = '{0}' \n", PreBuildBatchFile + ".txt");
						outputString.Append("\t.ExecUseStdOutAsOutput = true \n");
						outputString.Append("}\n\n");
					}
				}
			}

			string CompilerOptions = "";

			List<ObjectListNode> ObjectLists = new List<ObjectListNode>();
			var CompileItems = activeProject.GetItems("ClCompile");
			string PrecompiledHeaderString = "";

			foreach (var Item in CompileItems)
			{
				if (Item.DirectMetadata.Any())
				{
					if (Item.DirectMetadata.Where(dmd => dmd.Name == "ExcludedFromBuild" && dmd.EvaluatedValue == "true").Any())
						continue;
					if (Item.DirectMetadata.Where(dmd => dmd.Name == "PrecompiledHeader" && dmd.EvaluatedValue == "Create").Any())
					{
						ToolTask CLtask = (ToolTask)Activator.CreateInstance(CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.CL"));
						CLtask.GetType().GetProperty("Sources").SetValue(CLtask, new TaskItem[] { new TaskItem() });
						string pchCompilerOptions = GenerateTaskCommandLine(CLtask, new string[] { "PrecompiledHeaderOutputFile", "ObjectFileName", "AssemblerListingLocation" }, Item.Metadata) + " /FS";
						PrecompiledHeaderString = "\t.PCHOptions = '" + string.Format("\"%1\" /Fp\"%2\" /Fo\"%3\" {0} '\n", pchCompilerOptions);
						PrecompiledHeaderString += "\t.PCHInputFile = '" + Item.EvaluatedInclude + "'\n";
						PrecompiledHeaderString += "\t.PCHOutputFile = '" + Item.GetMetadataValue("PrecompiledHeaderOutputFile") + "'\n";
						break; //Assumes only one pch...
					}
				}
			}

			foreach (var Item in CompileItems)
			{
				bool ExcludePrecompiledHeader = false;
				if (Item.DirectMetadata.Any())
				{
					if (Item.DirectMetadata.Where(dmd => dmd.Name == "ExcludedFromBuild" && dmd.EvaluatedValue == "true").Any())
						continue;
					if (Item.DirectMetadata.Where(dmd => dmd.Name == "PrecompiledHeader" && dmd.EvaluatedValue == "Create").Any())
						continue;
					if (Item.DirectMetadata.Where(dmd => dmd.Name == "PrecompiledHeader" && dmd.EvaluatedValue == "NotUsing").Any())
						ExcludePrecompiledHeader = true;
				}

				ToolTask Task = (ToolTask) Activator.CreateInstance(CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.CL"));
				Task.GetType().GetProperty("Sources").SetValue(Task, new TaskItem[] { new TaskItem() }); //CPPTasks throws an exception otherwise...
				string TempCompilerOptions = GenerateTaskCommandLine(Task, new string[] { "ObjectFileName", "AssemblerListingLocation" }, Item.Metadata) + " /FS";
				if (Path.GetExtension(Item.EvaluatedInclude) == ".c")
					TempCompilerOptions += " /TC";
				else
					TempCompilerOptions += " /TP";
				CompilerOptions = TempCompilerOptions;
				string FormattedCompilerOptions = string.Format("\"%1\" /Fo\"%2\" {0}", TempCompilerOptions);
				var MatchingNodes = ObjectLists.Where(el => el.AddIfMatches(Item.EvaluatedInclude, "msvc", intDir, FormattedCompilerOptions, ExcludePrecompiledHeader ? "" : PrecompiledHeaderString));
				if(!MatchingNodes.Any())
				{
					ObjectLists.Add(new ObjectListNode(Item.EvaluatedInclude, "msvc", intDir, FormattedCompilerOptions, ExcludePrecompiledHeader ? "" : PrecompiledHeaderString));
				}
			}

			PrecompiledHeaderString = "";

			var ResourceCompileItems = activeProject.GetItems("ResourceCompile");
			foreach (var Item in ResourceCompileItems)
			{
				if (Item.DirectMetadata.Any())
				{
					if (Item.DirectMetadata.Where(dmd => dmd.Name == "ExcludedFromBuild" && dmd.EvaluatedValue == "true").Any())
						continue;
				}
			
				ToolTask Task = (ToolTask)Activator.CreateInstance(CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.RC"));
				string ResourceCompilerOptions = GenerateTaskCommandLine(Task, new string[] { "ResourceOutputFileName", "DesigntimePreprocessorDefinitions" }, Item.Metadata);
			
				string formattedCompilerOptions = string.Format("{0} /fo\"%2\" \"%1\"", ResourceCompilerOptions);
				var MatchingNodes = ObjectLists.Where(el => el.AddIfMatches(Item.EvaluatedInclude, "rc", intDir, formattedCompilerOptions, PrecompiledHeaderString));
				if (!MatchingNodes.Any())
				{
					ObjectLists.Add(new ObjectListNode(Item.EvaluatedInclude, "rc", intDir, formattedCompilerOptions, PrecompiledHeaderString, ".res"));
				}
			}

			int ActionNumber = 0;
			foreach (ObjectListNode ObjList in ObjectLists)
			{
				outputString.Append(ObjList.ToString(ActionNumber));
				ActionNumber++;		
			}

			if (ActionNumber > 0)
			{
				HasCompileActions = true;
			}
			else
			{
				HasCompileActions = false;
				Console.WriteLine("Project has no actions to compile.");
			}

			string CompileActions = string.Join(",", Enumerable.Range(0, ActionNumber).ToList().ConvertAll(x => string.Format("'action_{0}'", x)).ToArray());

			if (BuildOutput == BuildType.Application || BuildOutput == BuildType.DynamicLib)
			{
				outputString.AppendFormat("{0}('output')\n{{", BuildOutput == BuildType.Application ? "Executable" : "DLL");
				outputString.Append("\t.Linker = '$VCExePath$\\link.exe'\n");
		
				var LinkDefinitions = activeProject.ItemDefinitions["Link"];
				string OutputFile = LinkDefinitions.GetMetadataValue("OutputFile").Replace('\\', '/');

				if(HasCompileActions)
				{
					string DependencyOutputPath = LinkDefinitions.GetMetadataValue("ImportLibrary");
					if (Path.IsPathRooted(DependencyOutputPath))
						DependencyOutputPath = DependencyOutputPath.Replace('\\', '/');
					else
						DependencyOutputPath = Path.Combine(activeProject.DirectoryPath, DependencyOutputPath).Replace('\\', '/');

					foreach (var deps in CurrentProject.Dependents)
					{
						deps.AdditionalLinkInputs += " \"" + DependencyOutputPath + "\" ";
					}
				}

				ToolTask Task = (ToolTask)Activator.CreateInstance(CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.Link"));
				string LinkerOptions = GenerateTaskCommandLine(Task, new string[] { "OutputFile", "ProfileGuidedDatabase" }, LinkDefinitions.Metadata);

				if (!string.IsNullOrEmpty(CurrentProject.AdditionalLinkInputs))
				{
					LinkerOptions += CurrentProject.AdditionalLinkInputs;
				}
				outputString.AppendFormat("\t.LinkerOptions = '\"%1\" /OUT:\"%2\" {0}'\n", LinkerOptions.Replace("'","^'"));
				outputString.AppendFormat("\t.LinkerOutput = '{0}'\n", OutputFile);

				outputString.Append("\t.Libraries = { ");
				outputString.Append(CompileActions);
				outputString.Append(" }\n");

				outputString.Append("}\n\n");
			}
			else if(BuildOutput == BuildType.StaticLib)
			{
				outputString.Append("Library('output')\n{");
				outputString.Append("\t.Compiler = 'msvc'\n");
				outputString.Append(string.Format("\t.CompilerOptions = '\"%1\" /Fo\"%2\" /c {0}'\n", CompilerOptions));
				outputString.Append(string.Format("\t.CompilerOutputPath = \"{0}\"\n", intDir));
				outputString.Append("\t.Librarian = '$VCExePath$\\lib.exe'\n");

				var LibDefinitions = activeProject.ItemDefinitions["Lib"];
				string OutputFile = LibDefinitions.GetMetadataValue("OutputFile").Replace('\\','/');

				if(HasCompileActions)
				{
					string DependencyOutputPath = "";
					if (Path.IsPathRooted(OutputFile))
						DependencyOutputPath = Path.GetFullPath(OutputFile).Replace('\\', '/');
					else
						DependencyOutputPath = Path.Combine(activeProject.DirectoryPath, OutputFile).Replace('\\', '/');

					foreach (var deps in CurrentProject.Dependents)
					{
						deps.AdditionalLinkInputs += " \"" + DependencyOutputPath + "\" ";
					}
				}

				ToolTask task = (ToolTask)Activator.CreateInstance(CPPTasksAssembly.GetType("Microsoft.Build.CPPTasks.LIB"));
				string linkerOptions = GenerateTaskCommandLine(task, new string[] { "OutputFile" }, LibDefinitions.Metadata);
				if(!string.IsNullOrEmpty(CurrentProject.AdditionalLinkInputs))
				{
					linkerOptions += CurrentProject.AdditionalLinkInputs;
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
					if(!string.IsNullOrEmpty(MetaData.EvaluatedValue))
					{
						string BatchText = "call \"" + VCBasePath + "Auxiliary\\Build\\vcvarsall.bat\" "
							+ (platform == "Win32" ? "x86" : "x64") + " " + WindowsSDKTarget + "\n";
						PostBuildBatchFile = Path.Combine(activeProject.DirectoryPath, Path.GetFileNameWithoutExtension(activeProject.FullPath) + "_postbuild.bat");
						File.WriteAllText(PostBuildBatchFile, BatchText + MetaData.EvaluatedValue);
						outputString.Append("Exec('postbuild') \n{\n");
						outputString.AppendFormat("\t.ExecExecutable = '{0}' \n", PostBuildBatchFile);
						outputString.AppendFormat("\t.ExecInput = '{0}' \n", PostBuildBatchFile);
						outputString.AppendFormat("\t.ExecOutput = '{0}' \n", PostBuildBatchFile + ".txt");
						outputString.Append("\t.PreBuildDependencies = 'output' \n");
						outputString.Append("\t.ExecUseStdOutAsOutput = true \n");
						outputString.Append("}\n\n");
					}
				}
			}

			outputString.AppendFormat("Alias ('all')\n{{\n\t.Targets = {{ '{0}' }}\n}}", string.IsNullOrEmpty(PostBuildBatchFile) ? "output" : "postbuild");

			if(FileChanged || CommandLineOptions.AlwaysRegenerate)
			{
				File.WriteAllText(BFFOutputFilePath, outputString.ToString());
			}		   
		}

        private string GenerateTaskCommandLine(ToolTask task, string[] propertiesToSkip, IEnumerable<ProjectMetadata> metaDataList)
        {
            foreach (ProjectMetadata metaData in metaDataList)
            {
                if (propertiesToSkip.Contains(metaData.Name)) {
                    continue;
				}

                IEnumerable<PropertyInfo> matchingProps = task.GetType().GetProperties().Where(prop => prop.Name == metaData.Name);
				if (matchingProps.Any() && !string.IsNullOrEmpty(metaData.EvaluatedValue))
				{
					string EvaluatedValue = metaData.EvaluatedValue.Trim();
					if(metaData.Name == "AdditionalIncludeDirectories")
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

        private string rootDirectory_ = string.Empty;
    }
}
