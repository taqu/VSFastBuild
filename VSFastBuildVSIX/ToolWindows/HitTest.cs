namespace VSFastBuildVSIX.ToolWindows
{
    public class HitTest(BuildHost host, CPUCore core, BuildEvent ev)
    {
        public BuildHost host_ = host;
        public CPUCore core_ = core;
        public BuildEvent event_ = ev;
    }
}
