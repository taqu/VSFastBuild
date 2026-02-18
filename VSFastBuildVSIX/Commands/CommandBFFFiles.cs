using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO.Packaging;
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
        public class BFFFile
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
        }

        protected override void BeforeQueryStatus(EventArgs e)
        {
            VSFastBuildVSIXPackage package;
            if (!VSFastBuildVSIXPackage.TryGetPackage(out package))
            {
                return;
            }
            EnvDTE80.DTE2 dte = package.DTE;
            if (null == dte.Solution || string.IsNullOrEmpty(dte.Solution.FullName))
            {
                return;
            }

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
            GatherBFFs(files, dte.Solution);
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
                    TraverseProjectItems(files, project, string.Empty, 1);
                    continue;
                }
            }
            SortFiles(files);
            IServiceProvider serviceProvider = package;
            OleMenuCommandService menuCommandService = serviceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (menuCommandService == null)
            {
                return;
            }
            if(IsSame(files_, files))
            {
                RebuildMenus(package, menuCommandService);
                return;
            }
            files_ = files;
            nodes_.Clear();
            BuildTree();
            //PrintTree();
            RebuildMenus(package, menuCommandService);
        }

        private void RebuildMenus(VSFastBuildVSIXPackage package, OleMenuCommandService menuCommandService)
        {
            ClearMenus(menuCommandService);
            List<BaseCommand> menus = package.Menus;
            List<BaseCommand> commands = package.Commands;
            for(int i=0; i<nodes_.Count; ++i)
            {
                menus[i].Command.Visible = true;
                menus[i].Command.Enabled = true;
                menus[i].Command.Supported = true;
                menus[i].Command.Text = string.IsNullOrEmpty(nodes_[i].name_)? "{root}" : nodes_[i].name_;
                BaseCommand commandStart = commands[i];
                for(int j=0; j < nodes_[i].siblings_.Count; ++j)
                {
                    int index = nodes_[i].siblings_[j].index_;
                    BFFFile file = files_[index];
                    CommandID id = new CommandID(commandStart.Command.CommandID.Guid, commandStart.Command.CommandID.ID+j);
                    OleMenuCommand command = menuCommandService.FindCommand(id) as OleMenuCommand;
                    if(null == command)
                    {
                        command = new OleMenuCommand(CommandExecute, id);
                        menuCommandService.AddCommand(command);
                    }
                    command.Text = file.fileName_;
                    command.Visible = true;
                    command.Enabled = true;
                    command.Supported = true;
                    command.Properties["filepath"] = file.filePath_;
                    file.id_ = id;
                }
            }
            for(int i=nodes_.Count; i<menus.Count; ++i)
            {
                menus[i].Command.Visible = false;
            }
        }
        private void CommandExecute(object sender, EventArgs e)
        {
            OleMenuCommand command = sender as OleMenuCommand;
            if(null == command)
            {
                return;
            }
            if(!command.Properties.Contains("filepath")){
                return;
            }
            string filepath = command.Properties["filepath"] as string;
            if(string.IsNullOrEmpty(filepath) || !System.IO.File.Exists(filepath))
            {
                return;
            }
            VSFastBuildVSIXPackage package;
            if (!VSFastBuildVSIXPackage.TryGetPackage(out package))
            {
                return;
            }
            CommandBuildProject.RunProcessAsync(package, filepath).FireAndForget();
        }

        private static void GatherBFFs(List<BFFFile> files, EnvDTE.Project project, string parentDir, int level)
        {
            string directoryPath = System.IO.Path.GetDirectoryName(project.FullName);
            try {
                foreach (string filepath in System.IO.Directory.GetFiles(directoryPath, "*.bff"))
                {
                    if(Contains(files, filepath))
                    {
                        continue;
                    }
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

        private static bool Contains(List<BFFFile> files, string path)
        {
            foreach(BFFFile file in files)
            {
                if(file.filePath_ == path)
                {
                    return true;
                }
            }
            return false;
        }

        private static void GatherBFFs(List<BFFFile> files, EnvDTE.Solution solution)
        {
            string directoryPath = System.IO.Path.GetDirectoryName(solution.FullName);
            try {
                foreach (string filepath in System.IO.Directory.GetFiles(directoryPath, "*.bff"))
                {
                    if(Contains(files, filepath))
                    {
                        continue;
                    }
                    string filename = System.IO.Path.GetFileName(filepath);
                    BFFFile bff = new BFFFile();
                    bff.id_ = new CommandID(Guid.Empty, -1);
                    bff.filePath_ = filepath;
                    bff.relativePath_ = string.Empty;
                    bff.fileName_ = filename;
                    bff.level_ = 0;
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
                    TraverseProjectItems(files, subProject, parentDir, level+1);
                    continue;
                }
            }
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

        private void PrintTree()
        {
            for(int i=0; i<nodes_.Count; ++i)
            {
                Log.OutputDebugLine($"[{i}] {nodes_[i].name_} - {nodes_[i].index_}");
                for(int j=0; j < nodes_[i].siblings_.Count; ++j)
                {
                    Log.OutputDebugLine($"  [{j}] {nodes_[i].siblings_[j].name_} - {nodes_[i].siblings_[j].index_}");
                }
            }
        }


        private BFFNode FindDirectoryNode(string relativePath)
        {
            for(int i=0; i<nodes_.Count; ++i)
            {
                if (nodes_[i].name_ == relativePath)
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
                    string parentDir = (files_[i].relativePath_.Contains('\\'))? System.IO.Path.GetDirectoryName(files_[i].relativePath_) : string.Empty;
                    BFFNode directory = FindDirectoryNode(parentDir);
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
    }
}
