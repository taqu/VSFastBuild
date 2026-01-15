using Microsoft.VisualStudio.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VSFastBuildVSIX.ToolWindows
{
    internal static class ToolImages
    {
        private static bool initialized_ = false;
        public static ImageBrush IconRunning = new ImageBrush();
        public static ImageBrush SuccessCodeBrush = new ImageBrush();
        public static ImageBrush SuccessNonCodeBrush = new ImageBrush();
        public static ImageBrush SuccessPreprocessedBrush = new ImageBrush();
        public static ImageBrush SuccessCachedBrush = new ImageBrush();
        public static ImageBrush FailedBrush = new ImageBrush();
        public static ImageBrush TimeoutBrush = new ImageBrush();
        public static ImageBrush RunningBrush = new ImageBrush();
        public static ImageBrush RacingIconBrush = new ImageBrush();
        public static ImageBrush RacingWinIconBrush = new ImageBrush();
        public static ImageBrush RacingLostIconBrush = new ImageBrush();

        public static ImageBrush StatusProgressBrush = new ImageBrush();

        public static SolidColorBrush StatusInitialBrush = Brushes.Lime;

        private static BitmapImage GetBitmapImage(System.Drawing.Bitmap bitmap, MemoryStream memory)
        {
            memory.Position = 0;
            BitmapImage bitmapImage = new BitmapImage();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
            memory.Position = 0;
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memory;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            return bitmapImage;
        }

        public static void Initialize()
        {
            if (initialized_)
            {
                return;
            }
            using (MemoryStream memory = new MemoryStream())
            {
                IconRunning.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.Icon_animated, memory);
                SuccessCodeBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.success_code, memory);
                SuccessNonCodeBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.success_noncode, memory);
                SuccessPreprocessedBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.success_preprocess, memory);
                SuccessCachedBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.success_cache, memory);
                FailedBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.failed, memory);
                TimeoutBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.timeout, memory);
                RunningBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.running, memory);
                RacingIconBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.race_flag, memory);
                RacingWinIconBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.race_flag_win, memory);
                RacingLostIconBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.race_flag_lost, memory);
                StatusProgressBrush.ImageSource = GetBitmapImage(VSFastBuildVSIX.Resources.Images.progressbar, memory);
            }
            initialized_ = true;
        }
    }
}
