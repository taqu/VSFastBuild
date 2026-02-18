global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using System;
global using Task = System.Threading.Tasks.Task;
using EnvDTE;
using Microsoft.VisualStudio.Shell.Interop;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;

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
                return VSFastBuildVSIX.OptionsPage.Instance;
			}
		}

        private static WeakReference<VSFastBuildVSIXPackage> package_;

        public EnvDTE80.DTE2 DTE { get { return dte2_; } }

        public bool IsBuildProcessRunning()
        {
            lock (lock_)
            {
                return null != process_;
            }
        }

        public bool EnterBuildProcess(System.Diagnostics.Process process)
        {
            lock (lock_)
            {
                if(null != process_)
                {
                    return false;
                }
                else
                {
                    process_ = process;
                    return true;
                }
            }
        }

        public void LeaveBuildProcess()
        {
            CancelBuildProcess();
        }

        public void CancelBuildProcess()
        {
            lock (lock_)
            {
                if(null == process_)
                {
                    return;
                }
                try
                {
                    if (process_.HasExited)
                    {
                        process_.Dispose();
                        process_ = null;
                        cancelable_ = false;
                        return;
                    }
                }
                catch { }
                if (cancelable_)
                {
                    cancellationTokenSource_.Cancel();
                    cancelable_ = false;
                    System.Diagnostics.Process process = process_;
                    process_ = null;
                    _ = Task.Run(() =>
                    {
                        if (!process.WaitForExit(5000))
                        {
                            process.Kill();
                        }
                        process.Dispose();
                    });
                }
                else
                {
                    System.Diagnostics.Process process = process_;
                    process_ = null;
                    _ = Task.Run(() =>
                    {
                        process.Kill();
                        process.Dispose();
                    });

                }
            }
        }

        public CancellationToken CancellationToken
        {
            get
            {
                lock (lock_)
                {
                    cancelable_ = true;
                    return cancellationTokenSource_.Token;
                }
            }
        }

        public System.Collections.Generic.List<BaseCommand> Menus => menus_;
        public System.Collections.Generic.List<BaseCommand> Commands => commands_;

        public void AddMenu(BaseCommand menu)
        {
            lock (lock_)
            {
                if(menus_.Contains(menu))
                {
                    return;
                }
                menus_.Add(menu);
            }
        }

        public void AddCommand(BaseCommand command)
        {
            lock (lock_)
            {
                if(commands_.Contains(command))
                {
                    return;
                }
                commands_.Add(command);
            }
        }

        private EnvDTE80.DTE2 dte2_;
        private System.Diagnostics.Process process_;
        private bool cancelable_ = false;
        private readonly object lock_ = new();
        private CancellationTokenSource cancellationTokenSource_ = new CancellationTokenSource();
        private System.Collections.Generic.List<BaseCommand> menus_ = new System.Collections.Generic.List<BaseCommand>();
        private System.Collections.Generic.List<BaseCommand> commands_ = new System.Collections.Generic.List<BaseCommand>();

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            package_ = new WeakReference<VSFastBuildVSIXPackage>(this);
            dte2_ = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            this.DisposalToken.Register(() =>
            {
                cancellationTokenSource_.Cancel();
                cancellationTokenSource_.Dispose();
                cancellationTokenSource_ = null;
            });
            await this.RegisterCommandsAsync();
            this.RegisterToolWindows();
        }

    }
}