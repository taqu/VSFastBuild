using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace VSFastBuildVSIX
{
    public class GIFImage : System.Windows.Controls.Image
    {
        public string GifSource
        {
            get { return (string)GetValue(GifSourceProperty); }
            set { SetValue(GifSourceProperty, value); }
        }

        public int FrameIndex
        {
            get { return (int)GetValue(FrameIndexProperty); }
            set { SetValue(FrameIndexProperty, value); }
        }

        /// <summary>
        /// Defines whether the animation starts on it's own
        /// </summary>
        public bool AutoStart
        {
            get { return (bool)GetValue(AutoStartProperty); }
            set { SetValue(AutoStartProperty, value); }
        }

        public static readonly DependencyProperty AutoStartProperty = DependencyProperty.Register("AutoStart", typeof(bool), typeof(GIFImage), new UIPropertyMetadata(false, AutoStartPropertyChanged));

        private static void AutoStartPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) {
                (sender as GIFImage).StartAnimation();
            }
        }
        public static readonly DependencyProperty GifSourceProperty = DependencyProperty.Register("GifSource", typeof(string), typeof(GIFImage), new UIPropertyMetadata(string.Empty, GifSourcePropertyChanged));

        private static void GifSourcePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            (sender as GIFImage).Initialize();
        }

        private static GifBitmapDecoder GetGifBitmapDecoder(string gifResourceName)
        {
            GifBitmapDecoder bitMapDecoder = null;

            object obj = VSFastBuildVSIX.Resources.Images.ResourceManager.GetObject(gifResourceName, VSFastBuildVSIX.Resources.Images.Culture);

            if (obj != null)
            {
                System.Drawing.Bitmap bitmapObject = obj as System.Drawing.Bitmap;
                MemoryStream memory = new MemoryStream();
                bitmapObject.Save(memory, System.Drawing.Imaging.ImageFormat.Gif);
                memory.Position = 0;
                bitMapDecoder = new GifBitmapDecoder(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            }

            return bitMapDecoder;
        }

        private bool initialized_ = false;
        private GifBitmapDecoder gifDecoder_;
        private Int32Animation animation_;
        
        private void Initialize()
        {
            gifDecoder_ = GetGifBitmapDecoder(GifSource);
            animation_ = new Int32Animation(0, gifDecoder_.Frames.Count - 1, new Duration(new TimeSpan(0, 0, 0, gifDecoder_.Frames.Count / 10, (int)((gifDecoder_.Frames.Count / 10.0 - gifDecoder_.Frames.Count / 10) * 1000))));
            animation_.RepeatBehavior = RepeatBehavior.Forever;
            this.Source = gifDecoder_.Frames[0];
            initialized_ = true;
        }

        static GIFImage()
        {
            VisibilityProperty.OverrideMetadata(typeof(GIFImage), new FrameworkPropertyMetadata(VisibilityPropertyChanged));
        }

        private static void VisibilityPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if ((Visibility)e.NewValue == Visibility.Visible)
            {
                ((GIFImage)sender).StartAnimation();
            }
            else
            {
                ((GIFImage)sender).StopAnimation();
            }
        }

        public static readonly DependencyProperty FrameIndexProperty = DependencyProperty.Register("FrameIndex", typeof(int), typeof(GIFImage), new UIPropertyMetadata(0, new PropertyChangedCallback(ChangingFrameIndex)));

        private static void ChangingFrameIndex(DependencyObject obj, DependencyPropertyChangedEventArgs ev)
        {
            var gifImage = obj as GIFImage;
            gifImage.Source = gifImage.gifDecoder_.Frames[(int)ev.NewValue];
        }

        
        /// <summary>
        /// Starts the animation
        /// </summary>
        public void StartAnimation()
        {
            if (!initialized_) {
                Initialize();
            }
            BeginAnimation(FrameIndexProperty, animation_);
        }

        /// <summary>
        /// Stops the animation
        /// </summary>
        public void StopAnimation()
        {
            BeginAnimation(FrameIndexProperty, null);
        }
    }
}
