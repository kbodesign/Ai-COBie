using System.IO;
using OmniClass.Core.Loading;
using OmniClass.Core.Model;
using OmniClass.Core.Text;

namespace OmniClass.Core.Tests
{
    internal static class Fixture
    {
        /// <summary>The rows from the spreadsheet screenshot this project started from.</summary>
        public const string RestroomSheet =
            "Number,Title,Level,Alias 1,Alias 2,Alias 3,Alias 4\n" +
            "13-23 17,Restroom,3,RR,R Room,RestRoom,Rest_Room\n" +
            "13-23 17 11,Men's Restroom,4,MR,M Room,Men's Restroom,Men's-Restroom\n" +
            "13-23 17 13,Women's Restroom,4,WR,W Room,Women's Restroom,Women's_Restroom\n" +
            "13-23 17 15,Unisex Restroom,4,XR,NB,Unisex Restroom,Unisex-Restroom\n";

        public static ClassificationDictionary Load(string csv, NormalizerOptions options = null)
        {
            using (var reader = new StringReader(csv))
            {
                return AliasSheetLoader.Load(reader, options);
            }
        }

        public static ClassificationDictionary Restrooms() => Load(RestroomSheet);

        public static string ShippedDictionaryPath =>
            Path.Combine(Path.GetDirectoryName(typeof(Fixture).Assembly.Location), "data", "room_aliases.csv");
    }
}
