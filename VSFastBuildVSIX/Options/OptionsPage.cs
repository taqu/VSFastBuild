using System.CodeDom;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VSFastBuildVSIX
{
    internal partial class OptionsProvider
    {
        // Register the options with this attribute on your package class:
        // [ProvideOptionPage(typeof(OptionsProvider.OptionsPageOptions), "VSFastBuildVSIX.Config", "OptionsPage", 0, 0, true, SupportsProfiles = true)]
        [ComVisible(true)]
        public class OptionsPageOptions : BaseOptionPage<OptionsPage> { }
    }

    public class OptionsPage : BaseOptionModel<OptionsPage>
    {
        public const string DefaultPath = "FBuild.exe";
        public const bool DefaultDistributed = true;
        public const string DefaultArguments = "-dist -ide -monitor";
        public const bool DefaultEnableGeneration = true;
        public const bool DefaultGenOnly = false;
        public const bool DefaultUnity = false;
        public const bool DefaultOpenMonitor = true;
        public const bool DefaultShowSystemPerformance = false;

        [Category("Options")]
        [DisplayName("FBuild Path")]
        [Description("Path to the FBuile.exe.")]
        [DefaultValue(true)]
        public string Path { get; set; } = DefaultPath;

        [Category("Options")]
        [DisplayName("Distributed")]
        [Description("Whether to compile distributed.")]
        [DefaultValue(true)]
        public bool Distributed { get; set; } = DefaultDistributed;

        [Category("Options")]
        [DisplayName("Arguments")]
        [Description("Arguments which will be passed to FASTBuild (default \"-dist -cache -ide -monitor\").")]
        [DefaultValue(true)]
        public string Arguments { get; set; } = DefaultArguments;

        [Category("Options")]
        [DisplayName("Enable Generation")]
        [Description("Enable bff file generation.")]
        [DefaultValue(true)]
        public bool EnableGeneration { get; set; } = DefaultEnableGeneration;

        [Category("Options")]
        [DisplayName("Generate Only")]
        [Description("Generate bff file only.")]
        [DefaultValue(true)]
        public bool GenOnly { get; set; } = DefaultGenOnly;

        [Category("Options")]
        [DisplayName("Unity")]
        [Description("Whether to do unity build.")]
        [DefaultValue(true)]
        public bool Unity { get; set; } = DefaultUnity;

        [Category("Options")]
        [DisplayName("Open Monitor")]
        [Description("Whether to open monitor window automatically.")]
        [DefaultValue(true)]
        public bool OpenMonitor { get; set; } = DefaultOpenMonitor;

        [Category("Options")]
        [DisplayName("ShowSystemPerformance")]
        [Description("Whether to show system performance graph as default.")]
        [DefaultValue(true)]
        public bool ShowSystemPerformance { get; set; } = DefaultShowSystemPerformance;

        [Category("Options")]
        [DisplayName("Threads")]
        [Description("Number of threads to use.")]
        [DefaultValue(-1)]
        public int Threads { get; set; } = -1;
    }
}
