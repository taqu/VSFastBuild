global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using System;
global using Task = System.Threading.Tasks.Task;
using EnvDTE;
using Microsoft.VisualStudio.Shell.Interop;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VSFastBuildVSIX.Options;

namespace VSFastBuildVSIX
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(OptionsProvider.OptionsPageOptions), "FASTBuild", "General", 0, 0, true, SupportsProfiles = true)]
    [ProvideToolWindow(typeof(ToolWindowMonitor.Pane), Style = VsDockStyle.Tabbed, Window = WindowGuids.MainWindow, Orientation = ToolWindowOrientation.Left)]
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

        public static async Task<VSFastBuildVSIXPackage> GetPackageAsync()
        {
			VSFastBuildVSIXPackage package;
            if (null != package_ && package_.TryGetTarget(out package))
            {
                return package;
            }
            package = await Task.Run<VSFastBuildVSIXPackage>(() =>
			{
                Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                IVsShell shell = GetGlobalService(typeof(SVsShell)) as IVsShell;
                if (null == shell)
                {
                    return null;
                }
                IVsPackage vsPackage = null;
                shell.LoadPackage(ref PackageGuids.VSFastBuildVSIX, out vsPackage);
				return vsPackage as VSFastBuildVSIXPackage;
            });

            return package;
        }

        public static OptionsPage Options
		{
			get
			{
                return VSFastBuildVSIX.Options.OptionsPage.Instance;
			}
		}

        private static WeakReference<VSFastBuildVSIXPackage> package_;

        public EnvDTE80.DTE2 DTE { get { return dte2_; } }

        public bool EnterBuildProcess()
        {
            lock (lock_)
            {
                if (inProcess_)
                {
                    return false;
                }
                else
                {
                    inProcess_ = true;
                    return true;
                }
            }
        }

        public void LeaveBuildProcess()
        {
            lock (lock_)
            {
                inProcess_ = false;
                isCacellable_ = false;
            }
        }

        public void CancelBuildProcess()
        {
            lock (lock_)
            {
                if(inProcess_ && isCacellable_)
                {
                    cancellationTokenSource_.Cancel();
                    isCacellable_ = false;
                }
            }
        }

        public CancellationToken CancellationToken
        {
            get
            {
                lock (lock_)
                {
                    isCacellable_ = true;
                    return cancellationTokenSource_.Token;
                }
            }
        }

        private EnvDTE80.DTE2 dte2_;
        private bool inProcess_ = false;
        private bool isCacellable_ = false;
        private readonly object lock_ = new();
        private CancellationTokenSource cancellationTokenSource_ = new CancellationTokenSource();

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await this.RegisterCommandsAsync();
            this.RegisterToolWindows();

            package_ = new WeakReference<VSFastBuildVSIXPackage>(this);
            dte2_ = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            this.DisposalToken.Register(() =>
            {
                cancellationTokenSource_.Cancel();
                cancellationTokenSource_.Dispose();
                cancellationTokenSource_ = null;
            });
        }

    }
}