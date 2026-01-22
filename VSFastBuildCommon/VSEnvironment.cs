using Microsoft.VisualStudio.Setup.Configuration;
using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VSFastBuildCommon
{
    public class VSEnvironment
    {
        private const int REGDB_E_CLASSNOTREG = unchecked((int)0x80040154);

        public string VSVersion => VSVersion_;
        public string ToolsInstall => toolsInstall_;
        public string ToolsBinPath => toolsBinPath_;
        public string ToolsLibPath => toolsLibPath_;
        public string ToolsIncludePath => toolsIncludePath_;
        public string SdkVersion => sdkVersion_;
        public string SdkBasePath => sdkBasePath_;
        public string SdkBinPath => sdkBinPath_;
        public string SdkLibPath => sdkLibPath_;
        public string SdkIncludePath => sdkIncludePath_;

        private string VSVersion_ = string.Empty;
        private string toolsInstall_ = string.Empty;
        private string toolsBinPath_ = string.Empty;
        private string toolsLibPath_ = string.Empty;
        private string toolsIncludePath_ = string.Empty;
        private string sdkVersion_ = string.Empty;
        private string sdkBasePath_ = string.Empty;
        private string sdkBinPath_ = string.Empty;
        private string sdkLibPath_ = string.Empty;
        private string sdkIncludePath_ = string.Empty;

        private VSEnvironment()
        {
        }

        public static VSEnvironment Create(string vsVersion, string winSDKVersion)
        {
            try
            {
                var query = new SetupConfiguration();
                var query2 = (ISetupConfiguration2)query;
                var e = query2.EnumAllInstances();

                var helper = (ISetupHelper)query;

                string installPath = string.Empty;
                int fetched;
                var instances = new ISetupInstance[1];
                do
                {
                    e.Next(1, instances, out fetched);
                    if (fetched > 0)
                    {
                        var instance2 = (ISetupInstance2)instances[0];
                        string installationVersion = instance2.GetInstallationVersion();
                        if (installationVersion.StartsWith(vsVersion))
                        {
                            installPath = instance2.GetInstallationPath();
                            break;
                        }
                    }
                }
                while (fetched > 0);
                if (string.IsNullOrEmpty(installPath))
                {
                    return null;
                }
                string toolsRoot = Path.Combine(installPath, "VC", "Tools", "MSVC");
                Version version = new Version();
                string latestToolRoot = string.Empty;
                foreach (string directory in Directory.EnumerateDirectories(toolsRoot))
                {
                    Version newVersion = new Version();
                    if (!newVersion.TryParse(Path.GetFileName(directory)))
                    {
                        continue;
                    }
                    if (Version.Compare(version, newVersion) < 0)
                    {
                        latestToolRoot = directory;
                        version = newVersion;
                    }
                }
                if (string.IsNullOrEmpty(latestToolRoot))
                {
                    return null;
                }
                toolsRoot = latestToolRoot;
                VSEnvironment environment = new VSEnvironment();
                environment.VSVersion_ = vsVersion;
                environment.toolsInstall_ = installPath;
                environment.toolsBinPath_ = Path.Combine(toolsRoot, "bin", "Hostx64", "x64");
                environment.toolsLibPath_ = Path.Combine(toolsRoot, "lib", "x64");
                environment.toolsIncludePath_ = Path.Combine(toolsRoot, "include");

                string sdkRoot = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Microsoft\\Microsoft SDKs\\Windows\\v10.0", "InstallationFolder", null) as string;
                if (string.IsNullOrEmpty(sdkRoot))
                {
                    sdkRoot = Registry.GetValue("HKEY_CURRENT_USER\\SOFTWARE\\WOW6432Node\\Microsoft\\Microsoft SDKs\\Windows\\v10.0", "InstallationFolder", null) as string;
                    if (string.IsNullOrEmpty(sdkRoot))
                    {
                        return null;
                    }
                }
                sdkRoot = sdkRoot.TrimEnd(Path.DirectorySeparatorChar);
                environment.sdkBasePath_ = sdkRoot;
                environment.sdkIncludePath_ = Path.Combine(sdkRoot, "include", winSDKVersion);// "ucrt");
                environment.sdkLibPath_ = Path.Combine(sdkRoot, "lib", winSDKVersion);// "ucrt", "x64");
                environment.sdkBinPath_ = Path.Combine(sdkRoot, "bin", winSDKVersion, "x64");

                string sdkVersion = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Microsoft\\Microsoft SDKs\\Windows\\v10.0", "ProductVersion", null) as string;
                if (string.IsNullOrEmpty(sdkVersion))
                {
                    sdkVersion = Registry.GetValue("HKEY_CURRENT_USER\\SOFTWARE\\WOW6432Node\\Microsoft\\Microsoft SDKs\\Windows\\v10.0", "ProductVersion", null) as string;
                    if (string.IsNullOrEmpty(sdkVersion))
                    {
                        return null;
                    }
                }
                environment.sdkVersion_ = sdkVersion;

                return environment;
            }
            catch (COMException ex) when (ex.HResult == REGDB_E_CLASSNOTREG)
            {
                Console.WriteLine("The query API is not registered. Assuming no instances are installed.");
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error 0x{ex.HResult:x8}: {ex.Message}");
                return null;
            }
        }
    }
}
