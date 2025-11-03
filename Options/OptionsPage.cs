using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VSFastBuildVSIX.Config
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
        [Category("Options")]
        [DisplayName("FBuild path")]
        [Description("Path to the FBuile.exe.")]
        [DefaultValue("FBuild.exe")]
        public string Path { get; set; }

        [Category("Options")]
        [DisplayName("Distributed")]
        [Description("Whether to compile distributed.")]
        [DefaultValue(true)]
        public bool Distributed { get; set; }

        [Category("Options")]
        [DisplayName("Arguments")]
        [Description("Arguments which will be passed to FASTBuild (default \"-dist -ide -monitor\").")]
        [DefaultValue("-dist -ide -monitor")]
        public string Arguments { get; set; }

        [Category("Options")]
        [DisplayName("Unity")]
        [Description("Whether to do unity build.")]
        [DefaultValue(false)]
        public bool Unity { get; set; }
    }
}
