using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Threading;
using System.Diagnostics;
using System.Threading.Tasks;

namespace VSFastBuildVSIX
{
    public static class Log
    {
        private static string AddNewLine(string message)
        {
            if(!message.EndsWith(Environment.NewLine)) {
                return message + Environment.NewLine;
            }
            return message;
        }

        public const string PaneDebug = "Debug";
        public const string PaneBuild = "Build";

        public static async Task AddOutputPaneAsync(string name)
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DTE2 dte2 = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) as DTE2;
            EnvDTE.OutputWindow outputWindow = dte2.ToolWindows.OutputWindow;
            if (null == outputWindow)
            {
                return;
            }
            foreach (EnvDTE.OutputWindowPane window in outputWindow.OutputWindowPanes)
            {
                if (window.Name == name)
                {
                    return;
                }
            }
            outputWindow.OutputWindowPanes.Add(name);
        }

        public static async Task ClearPanelAsync(string name)
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DTE2 dte2 = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) as DTE2;
            EnvDTE.OutputWindow outputWindow = dte2.ToolWindows.OutputWindow;
            if (null == outputWindow)
            {
                return;
            }
            foreach (EnvDTE.OutputWindowPane window in outputWindow.OutputWindowPanes)
            {
                if (window.Name == name)
                {
                    window.Clear();
                }
            }
        }

        /// <summary>
        /// Print a message to the editor's output
        /// </summary>
        public static void OutputDebug(string message)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                DTE2 dte2 = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) as DTE2;
                EnvDTE.OutputWindow outputWindow = dte2.ToolWindows.OutputWindow;
                if (null == outputWindow)
                {
                    return;
                }
                foreach (EnvDTE.OutputWindowPane window in outputWindow.OutputWindowPanes)
                {
                    if (window.Name == PaneDebug)
                    {
                        window.OutputString(message);
                    }
                }
                Trace.WriteLine(message);
            });
        }

        /// <summary>
        /// Print a message to the editor's output
        /// </summary>
        public static async Task OutputDebugAsync(string message)
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DTE2 dte2 = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) as DTE2;
            EnvDTE.OutputWindow outputWindow = dte2.ToolWindows.OutputWindow;
            if (null == outputWindow)
            {
                return;
            }
            foreach (EnvDTE.OutputWindowPane window in outputWindow.OutputWindowPanes)
            {
                if (window.Name == PaneDebug)
                {
                    window.OutputString(message);
                }
            }
            Trace.WriteLine(message);
        }

        /// <summary>
        /// Print a message to the editor's output
        /// </summary>
        public static void OutputDebugLine(string message)
        {
            message = AddNewLine(message);
            OutputDebug(message);
        }

		/// <summary>
		/// Print a message to the editor's output
		/// </summary>
		public static async Task OutputDebugLineAsync(string message)
        {
            message = AddNewLine(message);
            await OutputDebugAsync(message);
        }

        /// <summary>
        /// Print a message to the editor's output
        /// </summary>
        public static void OutputBuild(string message)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                DTE2 dte2 = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) as DTE2;
                EnvDTE.OutputWindow outputWindow = dte2.ToolWindows.OutputWindow;
                if (null == outputWindow)
                {
                    return;
                }
                foreach (EnvDTE.OutputWindowPane window in outputWindow.OutputWindowPanes)
                {
                    if (window.Name == PaneBuild)
                    {
                        window.OutputString(message);
                    }
                }
                Trace.WriteLine(message);
            });
        }

        /// <summary>
		/// Print a message to the editor's output
		/// </summary>
		public static async Task OutputBuildAsync(string message)
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DTE2 dte2 = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) as DTE2;
            EnvDTE.OutputWindow outputWindow = dte2.ToolWindows.OutputWindow;
            if(null == outputWindow) {
                return;
            }
            foreach(EnvDTE.OutputWindowPane window in outputWindow.OutputWindowPanes) {
                if (window.Name == PaneBuild)
                {
                    window.OutputString(message);
                }
            }
            Trace.WriteLine(message);
        }

        /// <summary>
        /// Print a message to the editor's output
        /// </summary>
        public static void OutputBuildLine(string message)
        {
            message = AddNewLine(message);
            OutputBuild(message);
        }

        /// <summary>
		/// Print a message to the editor's output
		/// </summary>
		public static async Task OutputBuildLineAsync(string message)
        {
            message = AddNewLine(message);
            await OutputBuildAsync(message);
        }
    }
}
