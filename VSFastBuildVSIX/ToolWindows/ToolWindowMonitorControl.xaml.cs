using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VSFastBuildVSIX
{
    public partial class ToolWindowMonitorControl : UserControl
    {
        public static BitmapImage GetBitmapImage(System.Drawing.Bitmap bitmap)
        {
            BitmapImage bitmapImage = new BitmapImage();

            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
            }

            return bitmapImage;
        }

        public ToolWindowMonitorControl()
        {
            InitializeComponent();
        }

        private void MonitorControl_SelectionChanged(object sender, RoutedEventArgs e)
        {
            //VS.MessageBox.Show("ToolWindowMonitorControl", "Button clicked");
        }

        private void ScrollViewer_ScrollChanged(object sender, RoutedEventArgs e)
        {
            //VS.MessageBox.Show("ToolWindowMonitorControl", "Button clicked");
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            //VS.MessageBox.Show("ToolWindowMonitorControl", "Button clicked");
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            //VS.MessageBox.Show("ToolWindowMonitorControl", "Button clicked");
        }

        private void Hyperlink_RequestNavigate(object sender, RoutedEventArgs e)
        {
            //VS.MessageBox.Show("ToolWindowMonitorControl", "Button clicked");
        }
    }
}
