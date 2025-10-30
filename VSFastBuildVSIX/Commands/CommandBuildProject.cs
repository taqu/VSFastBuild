using EnvDTE;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell.Interop;
using System.Linq;

namespace VSFastBuildVSIX
{
    [Command(PackageGuids.VSFastBuildVSIXString, PackageIds.CommandBuild)]
    internal sealed class CommandBuildProject : BaseCommand<CommandBuildProject>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            VSFastBuildVSIXPackage package;
            if (!VSFastBuildVSIXPackage.TryGetPackage(out package))
            {
                return;
            }
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            EnvDTE80.DTE2 dte = package.DTE;
            SelectedItems selectedItems = dte.SelectedItems;
            if(null == selectedItems || selectedItems.Count<=0)
            {
                return;
            }
            EnvDTE.Project target = null;
            foreach (SelectedItem item in selectedItems)
            {
                if (item.Project is EnvDTE.Project)
                {
                    target = item.Project;
                }
            }
            if (null == target)
            {
                return;
            }
            IVsHierarchy vsHierarchy = GetVsHierarchyForProject(target);

            Microsoft.Build.Evaluation.ProjectCollection projectCollection = Microsoft.Build.Evaluation.ProjectCollection.GlobalProjectCollection;
            Microsoft.Build.Evaluation.Project msbuildProject = new Microsoft.Build.Evaluation.Project(target.FileName);

            if (msbuildProject != null)
            {
                // Enumerate all properties
                foreach (ProjectProperty property in msbuildProject.AllEvaluatedProperties)
                {
                    try
                    {
                        await Log.OutputAsync(string.Format($"Property Name: {property.Name}, Value: {property.EvaluatedValue}\n"));
                    }
                    catch
                    {
                    }
                }
            }

            //if (vsHierarchy is IVsBuildPropertyStorage buildPropertyStorage)
            //{
            //    string activeConfigName = dte.Solution.SolutionBuild.ActiveConfiguration.Name;
            //    uint storageType = (uint)_PersistStorageType.PST_PROJECT_FILE;
            //    string propertyValue;

            //    int hr = buildPropertyStorage.GetPropertyValue(
            //        propertyName,
            //        activeConfigName,
            //        storageType,
            //        out propertyValue);

            //    if (hr == 0)
            //    {
            //        return propertyValue;
            //    }
            //}
            await VS.MessageBox.ShowWarningAsync("CommandBuild", "Button clicked");
        }

        private static IVsHierarchy GetVsHierarchyForProject(EnvDTE.Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsHierarchy vsHierarchy = null;
            IVsSolution vsSolution = (IVsSolution)ServiceProvider.GlobalProvider.GetService(typeof(IVsSolution));
            vsSolution.GetProjectOfUniqueName(project.UniqueName, out vsHierarchy);
            return vsHierarchy;
        }
    }
}
