using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Security.RightsManagement;
using System.Windows.Forms.Design;
using static VSFastBuildVSIX.CommandBFFFiles;
using static VSFastBuildVSIX.CommandBuildProject;

namespace VSFastBuildVSIX
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildBFFFiles)]
    internal sealed class CommandBFFFiles : BaseCommand<CommandBFFFiles>
    {
        public const int MaxFileCount = 0xFE;
        private static readonly int[] MenuIDs = new int[]
        {
            PackageIds.VSFastBuildMenuBFF00,
            PackageIds.VSFastBuildMenuBFF01,
            PackageIds.VSFastBuildMenuBFF02,
            PackageIds.VSFastBuildMenuBFF03,
            PackageIds.VSFastBuildMenuBFF04,
            PackageIds.VSFastBuildMenuBFF05,
            PackageIds.VSFastBuildMenuBFF06,
            PackageIds.VSFastBuildMenuBFF07,
            PackageIds.VSFastBuildMenuBFF08,
            PackageIds.VSFastBuildMenuBFF09,
            PackageIds.VSFastBuildMenuBFF0A,
            PackageIds.VSFastBuildMenuBFF0B,
            PackageIds.VSFastBuildMenuBFF0C,
            PackageIds.VSFastBuildMenuBFF0D,
            PackageIds.VSFastBuildMenuBFF0E,
            PackageIds.VSFastBuildMenuBFF0F,
            PackageIds.VSFastBuildMenuBFF10,
            PackageIds.VSFastBuildMenuBFF11,
            PackageIds.VSFastBuildMenuBFF12,
            PackageIds.VSFastBuildMenuBFF13,
            PackageIds.VSFastBuildMenuBFF14,
            PackageIds.VSFastBuildMenuBFF15,
            PackageIds.VSFastBuildMenuBFF16,
            PackageIds.VSFastBuildMenuBFF17,
            PackageIds.VSFastBuildMenuBFF18,
            PackageIds.VSFastBuildMenuBFF19,
            PackageIds.VSFastBuildMenuBFF1A,
            PackageIds.VSFastBuildMenuBFF1B,
            PackageIds.VSFastBuildMenuBFF1C,
            PackageIds.VSFastBuildMenuBFF1D,
            PackageIds.VSFastBuildMenuBFF1E,
            PackageIds.VSFastBuildMenuBFF1F,
        };

        private static readonly int[] ButtonIDs = new int[]
        {
            PackageIds.CommandFBuildBFF00,
            PackageIds.CommandFBuildBFF01,
            PackageIds.CommandFBuildBFF02,
            PackageIds.CommandFBuildBFF03,
            PackageIds.CommandFBuildBFF03,
            PackageIds.CommandFBuildBFF04,
            PackageIds.CommandFBuildBFF05,
            PackageIds.CommandFBuildBFF06,
            PackageIds.CommandFBuildBFF07,
            PackageIds.CommandFBuildBFF08,
            PackageIds.CommandFBuildBFF09,
            PackageIds.CommandFBuildBFF0A,
            PackageIds.CommandFBuildBFF0B,
            PackageIds.CommandFBuildBFF0C,
            PackageIds.CommandFBuildBFF0D,
            PackageIds.CommandFBuildBFF0E,
            PackageIds.CommandFBuildBFF0F,
            PackageIds.CommandFBuildBFF10,
            PackageIds.CommandFBuildBFF11,
            PackageIds.CommandFBuildBFF12,
            PackageIds.CommandFBuildBFF13,
            PackageIds.CommandFBuildBFF14,
            PackageIds.CommandFBuildBFF15,
            PackageIds.CommandFBuildBFF16,
            PackageIds.CommandFBuildBFF17,
            PackageIds.CommandFBuildBFF18,
            PackageIds.CommandFBuildBFF19,
            PackageIds.CommandFBuildBFF1A,
            PackageIds.CommandFBuildBFF1B,
            PackageIds.CommandFBuildBFF1C,
            PackageIds.CommandFBuildBFF1D,
            PackageIds.CommandFBuildBFF1E,
            PackageIds.CommandFBuildBFF1F,
        };

        public enum Type
        {
            Menu,
            Button,
        }
        public struct BFFFile
        {
            public CommandID id_;
            public int level_;
            public string filePath_;
            public string relativePath_;
            public string fileName_;
        }

        public struct BFFNode
        {
            public int index_;
            public string name_;
            public List<BFFNode> siblings_;
        }

        private List<MenuCommand> menus_ = new List<MenuCommand>();
        private List<BFFFile> files_ = new List<BFFFile>();
        private List<BFFNode> nodes_ = new List<BFFNode>();

        private static bool IsSame(List<BFFFile> x0, List<BFFFile> x1)
        {
            if(x0.Count != x1.Count)
            {
                return false;
            }
            for(int i=0; i<x0.Count; ++i)
            {
                if(x0[i].relativePath_ != x1[i].relativePath_)
                {
                    return false;
                }
            }
            return true;
        }

        private void MenuCommandBeforeQueryStatus(object sender, EventArgs e)
        {
        }

        private void ClearMenus(OleMenuCommandService menuCommandService)
        {
            foreach (BFFFile file in files_)
            {
                MenuCommand menuCommand = menuCommandService.FindCommand(file.id_);
                if (null == menuCommand)
                {
                    continue;
                }
                menuCommandService.RemoveCommand(menuCommand);
            }
            files_.Clear();
            nodes_.Clear();
        }

        protected override void BeforeQueryStatus(EventArgs e)
        {
            VSFastBuildVSIXPackage package;
            if (!VSFastBuildVSIXPackage.TryGetPackage(out package))
            {
                return;
            }
            IServiceProvider serviceProvider = package;
            OleMenuCommandService menuCommandService = serviceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (menuCommandService == null)
            {
                return;
            }
            EnvDTE80.DTE2 dte = package.DTE;
            if (null == dte.Solution || string.IsNullOrEmpty(dte.Solution.FullName))
            {
                return;
            }

            OleMenuCommand rootCommand = Command;
            List<BFFFile> files = new List<BFFFile>();
            {
                BFFFile bff = new BFFFile();
                bff.id_ = new CommandID(Guid.Empty, -1);
                bff.filePath_ = string.Empty;
                bff.relativePath_ = string.Empty;
                bff.fileName_ = string.Empty;
                bff.level_ = 0;
                files.Add(bff);
            }
            foreach (EnvDTE.Project project in dte.Solution.Projects)
            {
                if (null == project)
                {
                    continue;
                }
                if (SupportedProject(project) && !string.IsNullOrEmpty(project.FullName))
                {
                    GatherBFFs(files, project, string.Empty, 0);
                    continue;
                }
                if (ProjectTypes.ProjectFolders == project.Kind)
                {
                    TraverseProjectItems(files, project, string.Empty, 0);
                    continue;
                }
            }
            SortFiles(files);
            if(IsSame(files_, files))
            {
                return;
            }

            ClearMenus(menuCommandService);
            files_ = files;
            BuildTree();
            PrintTree();
            rootCommand.Visible = true;
        }

        private static void GatherBFFs(List<BFFFile> files, EnvDTE.Project project, string parentDir, int level)
        {
            string directoryPath = System.IO.Path.GetDirectoryName(project.FullName);
            try {
                foreach (string filepath in System.IO.Directory.GetFiles(directoryPath, "*.bff"))
                {
                    string filename = System.IO.Path.GetFileName(filepath);
                    if (!filename.Contains(project.Name))
                    {
                        continue;
                    }
                    BFFFile bff = new BFFFile();
                    bff.id_ = new CommandID(Guid.Empty, -1);
                    bff.filePath_ = filepath;
                    bff.relativePath_ = System.IO.Path.Combine(parentDir, project.Name);
                    bff.fileName_ = filename;
                    bff.level_ = level;
                    files.Add(bff);
                }
            }
            catch
            {
            }
        }

        private static void TraverseProjectItems(List<BFFFile> files, EnvDTE.Project project, string parentDir, int level)
        {
            parentDir = System.IO.Path.Combine(parentDir, project.Name);
            if(FindFile(files, parentDir) < 0)
            {
                BFFFile bff = new BFFFile();
                bff.id_ = new CommandID(Guid.Empty, -1);
                bff.filePath_ = string.Empty;
                bff.relativePath_ = parentDir;
                bff.fileName_ = string.Empty;
                bff.level_ = level;
                files.Add(bff);
            }
            foreach (EnvDTE.ProjectItem projectItem in project.ProjectItems)
            {
                EnvDTE.Project subProject = projectItem.Object as EnvDTE.Project;
                if (null == subProject)
                {
                    continue;
                }
                if (SupportedProject(subProject) && !string.IsNullOrEmpty(subProject.FullName))
                {
                    GatherBFFs(files, subProject, parentDir, level);
                    continue;
                }
                if (ProjectTypes.ProjectFolders == subProject.Kind)
                {
                    if("jpeg" == subProject.Name)
                    {
                        Log.OutputDebugLine("jpeg");
                    }
                    TraverseProjectItems(files, subProject, parentDir, level+1);
                    continue;
                }
            }
        }

        private string GetName(string path, int count)
        {
            if (count <= 0)
            {
                return string.Empty;
            }
            for (int i = 0; i < path.Length; ++i)
            {
                if (path[i] == '\\')
                {
                    --count;
                    if (count <= 0)
                    {
                        return path.Substring(0, i);
                    }
                }
            }
            return string.Empty;
        }

        private static int FindFile(List<BFFFile> files, string relativePath)
        {
            for (int i = 0; i < files.Count; ++i)
            {
                if (files[i].relativePath_ == relativePath)
                {
                    return i;
                }
            }
            return -1;
        }

        private static void SortFiles(List<BFFFile> files)
        {
            files.Sort((BFFFile x0, BFFFile x1) =>
            {
                if (x0.level_ == x1.level_)
                {
                    return string.Compare(x0.relativePath_, x1.relativePath_);
                }
                return x0.level_ - x1.level_;
            });
        }

        private void PrintNode(BFFNode node, int level)
        {
            string indent = string.Empty;
            string name = node.name_;
            for (int i = 0; i < level; ++i)
            {
                indent += "  ";
            }
            Log.OutputDebugLine($"{indent}+{level}.{name}");
            for (int i = 0; i < 2; ++i)
            {
                indent += " ";
            }
            foreach (BFFNode sibling in node.siblings_)
            {
                Log.OutputDebugLine($"{indent}-{System.IO.Path.GetFileName(sibling.name_)} - {sibling.index_}");
            }
            ++level;
        }

        private void PrintTree()
        {
            if (nodes_.Count <= 0)
            {
                return;
            }
            PrintNode(nodes_[0], 0);
        }


        private BFFNode FindDirectoryNode(string relativePath)
        {
            string path = System.IO.Path.GetDirectoryName(relativePath);
            if("3rdparth\\jpeg" == relativePath)
            {
                Log.OutputDebugLine("jpeg");
            }
            for(int i=0; i<nodes_.Count; ++i)
            {
                if (nodes_[i].name_ == path)
                {
                    return nodes_[i];
                }
            }
            return new BFFNode();
        }

        private void BuildTree()
        {
            for (int i = 0; i < files_.Count; ++i)
            {
                if (string.IsNullOrEmpty(files_[i].filePath_))
                {
                    BFFNode node = new BFFNode();
                    node.name_ = files_[i].relativePath_;
                    node.index_ = i;
                    node.siblings_ = new List<BFFNode>();
                    nodes_.Add(node);
                }
                else
                {
                    BFFNode directory = FindDirectoryNode(files_[i].relativePath_);
                    System.Diagnostics.Debug.Assert(null != directory.name_);
                    BFFNode node = new BFFNode();
                    node.name_ = files_[i].relativePath_;
                    node.index_ = i;
                    node.siblings_ = null;
                    if (directory.siblings_.Count < MaxFileCount) {
                        directory.siblings_.Add(node);
                    }
                }
            }
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
#if false
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
            await Log.ClearPanelAsync(Log.PaneBuild);
            OptionsPage optionPage = VSFastBuildVSIXPackage.Options;
            bool openMonitor = optionPage.OpenMonitor;
            if (openMonitor)
                {
                    await CommandBuildProject.StartMonitorAsync(package, true);
                }
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
            if (openMonitor)
            {
                await CommandBuildProject.StopMonitorAsync(package);
            }
            package.LeaveBuildProcess();
#endif
        }
    }
}
