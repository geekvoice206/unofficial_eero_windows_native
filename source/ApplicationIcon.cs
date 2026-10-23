using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EeroDesktop
{
    internal static class ApplicationIcon
    {
        private static ImageSource windowImage;

        internal static ImageSource WindowImage
        {
            get
            {
                if (windowImage == null)
                {
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EeroDesktop.AppIcon.png"))
                    {
                        var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        bitmap.Freeze(); windowImage = bitmap;
                    }
                }
                return windowImage;
            }
        }

        internal static Icon CreateTrayIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EeroDesktop.AppIcon.ico"))
            using (var icon = new Icon(stream, 32, 32)) return (Icon)icon.Clone();
        }
    }
}
