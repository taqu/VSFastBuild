using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static VSFastBuildVSIX.CommandBuildProject;
using static VSFastBuildVSIX.Commands.CommandBFFFiles;

namespace VSFastBuildVSIX.Commands
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildBFFFiles)]
    internal sealed class CommandBFFFiles : BaseDynamicCommand<CommandBFFFiles, BFFFile>
    {
        public class BFFFile
        {
            public string FilePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
        }

        private List<BFFFile> files_ = new List<BFFFile>();

        protected override void BeforeQueryStatus(OleMenuCommand menuItem, EventArgs e, BFFFile item)
        {
            menuItem.Text = item.FileName;
        }

        protected override IReadOnlyList<BFFFile> GetItems()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            files_.Clear();
            VSFastBuildVSIXPackage package;
            if(!VSFastBuildVSIXPackage.TryGetPackage(out package))
            {
                return files_;
            }
            EnvDTE80.DTE2 dte = package.DTE;
            if (null == dte.Solution || string.IsNullOrEmpty(dte.Solution.FullName))
            {
                return files_;
            }
            string solutionDir = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            {
                foreach (string path in System.IO.Directory.GetFiles(solutionDir, "*.bff"))
                {
                    if(files_.Any(x=>x.FilePath == path))
                    {
                        continue;
                    }

                    BFFFile bff = new BFFFile();
                    bff.FilePath = path;
                    bff.FileName = System.IO.Path.GetFileName(path);
                    files_.Add(bff);
                }
            }
            foreach (EnvDTE.Project project in dte.Solution.Projects)
            {
                if(null == project || string.IsNullOrEmpty(project.FullName))
                {
                    continue;
                }
                string projectDir = System.IO.Path.GetDirectoryName(project.FullName);
                foreach(string path in System.IO.Directory.GetFiles(projectDir, "*.bff"))
                {
                    if(files_.Any(x=>x.FilePath == path))
                    {
                        continue;
                    }
                    string name;
                    if (path.StartsWith(solutionDir))
                    {
                        name = path.Substring(solutionDir.Length+1);
                    }
                    else
                    {
                        name = System.IO.Path.GetFileName(path);
                    }
                    BFFFile bff = new BFFFile();
                    bff.FilePath = path;
                    bff.FileName = name;
                    files_.Add(bff);
                }
            }
            return files_;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e, BFFFile bff)
        {
            VSFastBuildVSIXPackage package = await VSFastBuildVSIXPackage.GetPackageAsync();
            if (null == package)
            {
                return;
            }
            if (package.IsBuildProcessRunning())
            {
                return;
            }
            await Log.AddOutputPaneAsync(Log.PaneBuild);
            try
            {
                Result result = new Result();
                await Log.OutputBuildAsync($"--- VSFastBuild begin running {bff.FileName}---");
                await RunProcessAsync(package, bff.FilePath);
                await Log.OutputBuildAsync($"--- VSFastBuild end running {bff.FileName}---");
            }
            catch (Exception ex)
            {
                await Log.OutputDebugAsync(ex.Message);
            }
            package.LeaveBuildProcess();
        }
    }
}
