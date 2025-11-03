using Microsoft.Build.Construction;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSFastBuildCommon;

namespace VSFastBuildCLI
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            RootCommand rootCommand  = new RootCommand
            {
                Description = "Run FastBuild from Visual Studio project file.",
            };
            Argument<string> projectFileArgument = new Argument<string>("project file").AcceptLegalFilePathsOnly();
            rootCommand.Arguments.Add(projectFileArgument);

            Option<string> configOption = new Option<string>(
                "--config",
                "-c"
            )
            { Description = "Target configuration to build.", DefaultValueFactory = (ArgumentResult) => "Debug" };
            rootCommand.Options.Add(configOption);

            Option<string> platformOption = new Option<string>(
                "--platform",
                "-p"
            )
            { Description = "Target platform to build.", DefaultValueFactory = (ArgumentResult) => "x64"};
            rootCommand.Options.Add(platformOption);

            Option<string> fbPathOption = new Option<string>(
                "--fbpath",
                "-f"
            )
            { Description = "FBuild.exe path.", DefaultValueFactory = (ArgumentResult) => "FBuild.exe"};
            rootCommand.Options.Add(fbPathOption);

            Option<string> fbArgsOption = new Option<string>(
                "--fbargs",
                "-a"
            )
            { Description = "Arguments which will be passed to FBuild.exe.", DefaultValueFactory = (ArgumentResult) => "-dist"};
            rootCommand.Options.Add(fbArgsOption);

            Option<bool> genOnlyOption = new Option<bool>(
                "--generateonly",
                "-g"
            )
            { Description = "Generate bff file only.", DefaultValueFactory = (ArgumentResult) => false};
            rootCommand.Options.Add(genOnlyOption);

            Option<bool> unityOption = new Option<bool>(
                "--unity",
                "-u"
            )
            { Description = "Whether to do unity build.", DefaultValueFactory = (ArgumentResult) => false};
            rootCommand.Options.Add(unityOption);

            ParseResult parseResult = rootCommand.Parse(args);
            int result = await parseResult.InvokeAsync();
            if(0 != result || 0<parseResult.Errors.Count)
            {
                return;
            }
            string projectFile = parseResult.GetValue(projectFileArgument);
            string config = parseResult.GetValue(configOption);
            string platform = parseResult.GetValue(platformOption);
            string fbPath = parseResult.GetValue(fbPathOption);
            string fbArgs = parseResult.GetValue(fbArgsOption);
            bool genOnly = parseResult.GetValue(genOnlyOption);
            bool unity = parseResult.GetValue(unityOption);

            VSFastBuild vsFastBuild = new VSFastBuild()
            {
                Configuration = config,
                Platform = platform,
                FBuildPath = fbPath,
                GenerateOnly = genOnly,
                UnityBuild = unity
            };
            if (!File.Exists(projectFile))
            {
                #if DEBUG
                Console.WriteLine($"File not found: {projectFile}");
                #endif
                return;
            }
            {
                string ext = Path.GetExtension(projectFile);
                if(ext !=".sln" && ext != ".vcxproj")
                {
                    return;
                }
                string fullPath = Path.GetFullPath(projectFile);
                if(ext == ".vcxproj")
                {
                    vsFastBuild.ProjectFiles.Add(fullPath);
                }
                else
                {
                    List<ProjectInSolution> solutionProjects = SolutionFile.Parse(fullPath).ProjectsInOrder.Where(x => x.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat).ToList();
                    solutionProjects.Sort((x0, x1) =>
                    {
                        if (x0.Dependencies.Contains(x1.ProjectGuid)){
                            return 1;
                        }
                        if (x1.Dependencies.Contains(x0.ProjectGuid)){
                            return -1;
                        }
                        return 0;
                    });
                    List<string> projectFiles = solutionProjects.ConvertAll(x => x.AbsolutePath);
                    vsFastBuild.ProjectFiles.AddRange(projectFiles);                }
            }
            foreach (System.Diagnostics.Process process in vsFastBuild.Build())
            {
                try
                {
                    using (System.Diagnostics.Process proc = process)
                    {
                        proc.EnableRaisingEvents = true;
                        proc.OutputDataReceived += (sender, ev) =>
                        {
                            if (null != ev.Data)
                            {
                                Console.WriteLine(ev.Data);

                            }
                        };
                        proc.ErrorDataReceived += (sender, ev) =>
                        {
                            if (null != ev.Data)
                            {
                                Console.WriteLine(ev.Data);
                            }
                        };
                        proc.Start();
                        proc.BeginErrorReadLine();
                        proc.BeginOutputReadLine();
                        proc.WaitForExit();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }
}
