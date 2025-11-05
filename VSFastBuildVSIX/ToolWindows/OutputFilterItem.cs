using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSFastBuildVSIX.ToolWindows
{
    internal class OutputFilterItem
    {
        private string name_ = string.Empty;
         private BuildEvent buildEvent_;

        public OutputFilterItem(string name)
        {
            name_ = name;
        }

        public OutputFilterItem(BuildEvent buildEvent)
        {
            buildEvent_ = buildEvent;
        }
        public BuildEvent BuildEvent => buildEvent_;

        public string Name
        {
            get
            {
                string result;

                if (null != buildEvent_)
                {
                    result = buildEvent_._name.Substring(1, _buildEvent._name.Length - 2);
                }
                else
                {
                    // fallback
                    result = _internalName;
                }

                const int charactersToDisplay = 50;

                if (result.Length > charactersToDisplay)
                {
                    result = result.Substring(result.IndexOf('\\', result.Length - charactersToDisplay));
                }

                return result;
            }

            set
            {
                _internalName = value;
            }
        }
    }
}
