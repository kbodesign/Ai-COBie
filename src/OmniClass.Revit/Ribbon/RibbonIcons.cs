using System;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace OmniClass.Revit.Ribbon
{
    internal static class RibbonIcons
    {
        public static BitmapImage Load(string fileName)
        {
            var resource = "OmniClass.Revit.Resources." + fileName;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null)
                    throw new InvalidOperationException("Missing ribbon icon resource '" + resource + "'.");

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}
