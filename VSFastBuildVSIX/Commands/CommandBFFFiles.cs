using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Security.RightsManagement;
using static VSFastBuildVSIX.CommandBFFFiles;
using static VSFastBuildVSIX.CommandBuildProject;

namespace VSFastBuildVSIX
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandFBuildDynamicLevel00)]
    internal sealed class CommandBFFFiles : BaseCommand<CommandBFFFiles>
    {
        public enum Type
        {
            Menu,
            Button,
        }
        public struct BFFFile
        {
            public CommandID id_;
            public int order_;
            public string filePath_;
            public string relativePath_;
            public string fileName_;
        }

        public struct BFFNode
        {
            public int index_;
            public string name_;
            public List<BFFNode> siblings_;
            public List<BFFNode> children_;
        }

        private List<BFFFile> files_ = new List<BFFFile>();
        private List<BFFNode> nodes_ = new List<BFFNode>();

        private void MenuCommandBeforeQueryStatus(object sender, EventArgs e)
        {

        }
        protected override Task InitializeCompletedAsync()
        {
            return base.InitializeCompletedAsync();
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

            ClearMenus(menuCommandService);
            string solutionDir = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            GatherBFFs(solutionDir, solutionDir);
            OleMenuCommand rootCommand = Command;
            foreach (EnvDTE.Project project in dte.Solution.Projects)
            {
                if (null == project)
                {
                    continue;
                }
                if (SupportedProject(project) && !string.IsNullOrEmpty(project.FullName))
                {
                    string projectDir = System.IO.Path.GetDirectoryName(project.FullName);
                    GatherBFFs(projectDir, solutionDir);
                    continue;
                }
                if (ProjectTypes.ProjectFolders == project.Kind)
                {
                    TraverseProjectItems(project, solutionDir);
                    continue;
                }
            }
            AddNodeFiles();
            BuildTree();
            rootCommand.Visible = false;
            //PrintTree();
            CommandID commandID = new CommandID(Command.CommandID.Guid, Command.CommandID.ID + 1);
            if (null != menuCommandService.FindCommand(commandID))
            {
                return;
            }
            //OleMenuCommand menuCommand = new OleMenuCommand(MenuCommandBeforeQueryStatus, commandID);
            //menuCommand.Text = "MenuCommand_Test";
            //menuCommandService.AddCommand(menuCommand);
        }

        private void GatherBFFs(string root, string solutionDir)
        {
            foreach (string path in System.IO.Directory.GetFiles(root, "*.bff"))
            {
                if (files_.Any(x => x.filePath_ == path))
                {
                    continue;
                }
                string relativePath;
                string name;
                if (path.StartsWith(solutionDir))
                {
                    relativePath = path.Substring(solutionDir.Length + 1);
                    name = System.IO.Path.GetFileName(path);
                }
                else
                {
                    relativePath = string.Empty;
                    name = System.IO.Path.GetFileName(path);
                }
                BFFFile bff = new BFFFile();
                bff.id_ = new CommandID(Guid.Empty, -1);
                bff.filePath_ = path.Replace('/', '\\');
                bff.relativePath_ = relativePath.Replace('/', '\\');
                bff.fileName_ = name;
                bff.order_ = bff.relativePath_.Count(x => '\\' == x);
                files_.Add(bff);
            }
        }

        private void TraverseProjectItems(EnvDTE.Project project, string solutionDir)
        {
            foreach (EnvDTE.ProjectItem projectItem in project.ProjectItems)
            {
                EnvDTE.Project subProject = projectItem.Object as EnvDTE.Project;
                if (null == subProject)
                {
                    continue;
                }
                if (SupportedProject(subProject) && !string.IsNullOrEmpty(subProject.FullName))
                {
                    string projectDir = System.IO.Path.GetDirectoryName(subProject.FullName);
                    GatherBFFs(projectDir, solutionDir);
                    continue;
                }
                if (ProjectTypes.ProjectFolders == subProject.Kind)
                {
                    TraverseProjectItems(subProject, solutionDir);
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
                        return path.Substring(0, i + 1);
                    }
                }
            }
            return string.Empty;
        }

        private int FindNodeFile(string name)
        {
            for (int i = 0; i < files_.Count; ++i)
            {
                if (files_[i].fileName_ == name)
                {
                    return i;
                }
            }
            return -1;
        }

        private void AddNodeFiles()
        {
            int count = files_.Count;
            for (int i = 0; i < count; ++i)
            {
                string name = GetName(files_[i].relativePath_, files_[i].order_);
                int index = FindNodeFile(name);
                if (index < 0)
                {
                    BFFFile file = new BFFFile();
                    file.id_ = new CommandID(Guid.Empty, -1);
                    file.order_ = files_[i].order_;
                    file.filePath_ = string.Empty;
                    file.relativePath_ = name;
                    file.fileName_ = name;
                    files_.Add(file);
                }
            }
            files_.Sort((BFFFile x0, BFFFile x1) =>
            {
                if (x0.order_ == x1.order_)
                {
                    return string.Compare(x0.relativePath_, x1.relativePath_);
                }
                return x0.order_ - x1.order_;
            });
        }

        private int FindChildNode(BFFNode node, string name)
        {
            for (int i = 0; i < node.children_.Count; ++i)
            {
                if (node.children_[i].name_ == name)
                {
                    return i;
                }
            }
            return -1;
        }

        private string GetFirstNodeName(string path)
        {
            int index = path.IndexOf('\\');
            if (index <= 0)
            {
                return string.Empty;
            }
            return path.Substring(0, index);
        }

        private BFFNode FindDirectoryNode(BFFNode node, string path)
        {
            string name = GetFirstNodeName(path);
            if (string.IsNullOrEmpty(name))
            {
                return node;
            }
            int index = FindChildNode(node, name);
            if (index < 0)
            {
                return new BFFNode();
            }
            path = path.Substring(name.Length + 1);
            return FindDirectoryNode(node.children_[index], path);
        }

        private BFFNode AddNode(BFFNode root, string path)
        {
            string name = GetFirstNodeName(path);
            if (string.IsNullOrEmpty(name))
            {
                return new BFFNode();
            }
            int index = FindChildNode(root, name);
            BFFNode node = new BFFNode();
            if (index < 0)
            {
                node.name_ = name;
                node.index_ = -1;
                node.siblings_ = new List<BFFNode>();
                node.children_ = new List<BFFNode>();
                root.children_.Add(node);
            }
            else
            {
                node = root.children_[index];
            }
            string rest = path.Substring(name.Length + 1);
            if (string.IsNullOrEmpty(rest) || rest == "\\")
            {
                return node;
            }
            return AddNode(node, rest);
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
            foreach (BFFNode child in node.children_)
            {
                PrintNode(child, level);
            }
        }

        private void PrintTree()
        {
            if (nodes_.Count <= 0)
            {
                return;
            }
            PrintNode(nodes_[0], 0);
        }


        private void BuildTree()
        {
            for (int i = 0; i < files_.Count; ++i)
            {
                if (string.IsNullOrEmpty(files_[i].filePath_))
                {
                    if (nodes_.Count <= 0)
                    {
                        BFFNode node = new BFFNode();
                        node.name_ = files_[i].relativePath_;
                        node.index_ = i;
                        node.siblings_ = new List<BFFNode>();
                        node.children_ = new List<BFFNode>();
                        nodes_.Add(node);
                    }
                    else
                    {
                        AddNode(nodes_[0], files_[i].relativePath_);
                    }
                }
                else
                {
                    BFFNode directory = FindDirectoryNode(nodes_[0], files_[i].relativePath_);
                    System.Diagnostics.Debug.Assert(null != directory.name_);
                    if (null == directory.name_)
                    {
                        continue;
                    }
                    BFFNode node = new BFFNode();
                    node.name_ = files_[i].relativePath_;
                    node.index_ = i;
                    node.siblings_ = null;
                    node.children_ = null;
                    directory.siblings_.Add(node);
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
