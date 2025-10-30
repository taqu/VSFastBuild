using Microsoft.Build.Construction;
using System.CommandLine;
using System.CommandLine.Parsing;
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
            Argument<string> projectFileArgument = new Argument<string>("project file").AcceptLegalFileNamesOnly();
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
            bool unity = parseResult.GetValue(unityOption);

            VSFastBuild vsFastBuild = new VSFastBuild()
            {
                Configuration = config,
                Platform = platform,
                FBuildPath = fbPath,
                UnityBuild = unity
            };
            {
                string ext = Path.GetExtension(projectFile);
                if(ext !="sln" && ext != "vcxproj")
                {
                    return;
                }
                string fullPath = Path.GetFullPath(projectFile);
                if(ext == "vcxproj")
                {
                    vsFastBuild.ProjectFiles.Add(fullPath);
                }
                else
                {
                    List<ProjectInSolution> solutionProjects = SolutionFile.Parse(fullPath).ProjectsInOrder.Where(x => x.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat).ToList();
                    solutionProjects.Sort((x0, x1) =>
                    {
                        if (x0.Dependencies.Contains(x1.ProjectGuid)) return 1;
                        if (x1.Dependencies.Contains(x0.ProjectGuid)) return -1;
                        return 0;
                    });
                    List<string> projectFiles = solutionProjects.ConvertAll(x => x.AbsolutePath);
                    vsFastBuild.ProjectFiles.AddRange(projectFiles);                }
            }
            vsFastBuild.Build();
        }
    }
}
