global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using System;
global using Task = System.Threading.Tasks.Task;
using System.Runtime.InteropServices;
using System.Threading;

namespace VSFastBuildVSIX
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.VSFastBuildVSIXString)]
    public sealed class VSFastBuildVSIXPackage : ToolkitPackage
    {
        public static bool TryGetPackage(out VSFastBuildVSIXPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (null != package_ && package_.TryGetTarget(out package))
            {
                return true;
            }
            package = null;
            return false;
        }

        private static WeakReference<VSFastBuildVSIXPackage> package_;

        public EnvDTE80.DTE2 DTE { get { return dte2_; } }

        private EnvDTE80.DTE2 dte2_;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await this.RegisterCommandsAsync();

            package_ = new WeakReference<VSFastBuildVSIXPackage>(this);
            dte2_ = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
        }

    }
}