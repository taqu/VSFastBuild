
namespace VSFastBuildCommon
{
    public struct Version
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
            if (str.Contains("."))
            {
                numbers = str.Split('.');
            }
            else if (str.Contains(","))
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

        public static int Compare(in Version x0, in Version x1)
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
}
