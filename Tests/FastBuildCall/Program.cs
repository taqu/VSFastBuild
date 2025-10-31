using Microsoft.VisualStudio.Setup.Configuration;
using Microsoft.Win32;
using System.Runtime.InteropServices;
namespace FastBuildCall
{
    internal class Program
    {
        private const int REGDB_E_CLASSNOTREG = unchecked((int)0x80040154);

        private struct VersionNumber
        {
            public int major_;
            public int minor_;
            public int patch_;

            public bool TryParse(string str)
            {
                major_ = 0;
                minor_ = 0;
                patch_ = 0;
                string[] numbers = null;
                if (str.Contains('.'))
                {
                    numbers = str.Split('.');
                }
                else if (str.Contains(','))
                {
                    numbers = str.Split(',');
                }
                else
                {
                    return int.TryParse(str, out major_);
                }
                if (null == numbers || numbers.Length <= 0)
                {
                    return false;
                }
                if (!int.TryParse(numbers[0], out major_))
                {
                    return false;
                }
                if (numbers.Length <= 1)
                {
                    return true;
                }

                if (!int.TryParse(numbers[1], out minor_))
                {
                    major_ = 0;
                    return false;
                }
                if (numbers.Length <= 2)
                {
                    return true;
                }

                if (!int.TryParse(numbers[1], out patch_))
                {
                    major_ = minor_ = 0;
                    return false;
                }
                return true;
            }

            public static int Compare(VersionNumber x0, VersionNumber x1)
            {
                if (x0.major_ == x1.major_)
                {
                    if (x0.minor_ == x1.minor_)
                    {
                        return x0.patch_ - x1.patch_;
                    }
                    return x0.minor_ - x1.minor_;
                }
                return x0.major_ - x1.major_;
            }
        }
        internal static int Main(string[] args)
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
                        //PrintInstance(instances[0], helper);
                        var instance2 = (ISetupInstance2)instances[0];
                        if (instance2.GetInstallationVersion().StartsWith("17"))
                        {
                            installPath = instance2.GetInstallationPath();
                            break;
                        }
                    }
                }
                while (fetched > 0);
                if (string.IsNullOrEmpty(installPath))
                {
                    return 0;
                }
                string toolsRoot = Path.Combine(installPath, "VC", "Tools", "MSVC");
                VersionNumber version = new VersionNumber();
                string latestToolRoot = string.Empty;
                foreach (string directory in Directory.EnumerateDirectories(toolsRoot))
                {
                    VersionNumber newVersion = new VersionNumber();
                    if (!newVersion.TryParse(Path.GetFileName(directory)))
                    {
                        continue;
                    }
                    if (VersionNumber.Compare(version, newVersion) < 0)
                    {
                        latestToolRoot = directory;
                        version = newVersion;
                    }
                }
                if (string.IsNullOrEmpty(latestToolRoot))
                {
                    return 0;
                }
                toolsRoot = latestToolRoot;
                string toolsBinPath = Path.Combine(toolsRoot, "bin", "Hostx64", "x64");
                string toolsLibPath = Path.Combine(toolsRoot, "lib", "x64");
                string toolsIncludePath = Path.Combine(toolsRoot, "include");

                string sdkRoot = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Microsoft\\Microsoft SDKs\\Windows\\v10.0", "InstallationFolder", null) as string;
                if (string.IsNullOrEmpty(sdkRoot))
                {
                    sdkRoot = Registry.GetValue("HKEY_CURRENT_USER\\SOFTWARE\\WOW6432Node\\Microsoft\\Microsoft SDKs\\Windows\\v10.0", "InstallationFolder", null) as string;
                    if (string.IsNullOrEmpty(sdkRoot))
                    {
                        return 0;
                    }
                }
                const string SDKVersion = "10.0.26100.0";
                string sdkIncludePath = Path.Combine(sdkRoot, "include", SDKVersion, "ucrt");
                string sdkLibPath = Path.Combine(sdkRoot, "lib", SDKVersion, "ucrt", "x64");
                string sdkBinPath = Path.Combine(sdkRoot, "bin", SDKVersion, "x64");

                return 0;
            }
            catch (COMException ex) when (ex.HResult == REGDB_E_CLASSNOTREG)
            {
                Console.WriteLine("The query API is not registered. Assuming no instances are installed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error 0x{ex.HResult:x8}: {ex.Message}");
                return ex.HResult;
            }
        }

        private static void PrintInstance(ISetupInstance instance, ISetupHelper helper)
        {
            var instance2 = (ISetupInstance2)instance;
            var state = instance2.GetState();
            Console.WriteLine($"InstanceId: {instance2.GetInstanceId()} ({(state == InstanceState.Complete ? "Complete" : "Incomplete")})");

            var installationVersion = instance.GetInstallationVersion();
            var version = helper.ParseVersion(installationVersion);

            Console.WriteLine($"InstallationVersion: {installationVersion} ({version})");

            if ((state & InstanceState.Local) == InstanceState.Local)
            {
                Console.WriteLine($"InstallationPath: {instance2.GetInstallationPath()}");
            }

            var catalog = instance as ISetupInstanceCatalog;
            if (catalog != null)
            {
                Console.WriteLine($"IsPrerelease: {catalog.IsPrerelease()}");
            }

            if ((state & InstanceState.Registered) == InstanceState.Registered)
            {
                Console.WriteLine($"Product: {instance2.GetProduct().GetId()}");
                Console.WriteLine("Workloads:");

                PrintWorkloads(instance2.GetPackages());
            }

            var properties = instance2.GetProperties();
            if (properties != null)
            {
                Console.WriteLine("Custom properties:");
                PrintProperties(properties);
            }

            properties = catalog?.GetCatalogInfo();
            if (properties != null)
            {
                Console.WriteLine("Catalog properties:");
                PrintProperties(properties);
            }

            Console.WriteLine();
        }

        private static void PrintProperties(ISetupPropertyStore store)
        {
            var properties = from name in store.GetNames()
                             orderby name
                             select new { Name = name, Value = store.GetValue(name) };

            foreach (var prop in properties)
            {
                Console.WriteLine($"    {prop.Name}: {prop.Value}");
            }
        }

        private static void PrintWorkloads(ISetupPackageReference[] packages)
        {
            var workloads = from package in packages
                            where string.Equals(package.GetType(), "Workload", StringComparison.OrdinalIgnoreCase)
                            orderby package.GetId()
                            select package;

            foreach (var workload in workloads)
            {
                Console.WriteLine($"    {workload.GetId()}");
            }
        }
    }
}
