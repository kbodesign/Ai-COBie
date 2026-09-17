using System.Collections.Generic;

namespace OmniClass.Core.Model
{
    /// <summary>
    /// The same values Autodesk's Assign Classification / Standardized Data Tool writes
    /// when someone picks an OmniClass Table 13 space. There is no public API for that
    /// button, so we reproduce its result by setting these parameters.
    /// </summary>
    public static class AssignClassificationValues
    {
        public const string SystemName = "OmniClass Table 13";

        public const string NumberParameter = "Classification.Space.Number";
        public const string DescriptionParameter = "Classification.Space.Description";
        public const string CobieCategoryParameter = "COBie.Space.Category";
        public const string IfcClassificationCodeParameter = "ClassificationCode";

        public static string Category(string number, string title)
        {
            return OmniClassFormatting.Category(number, title);
        }

        /// <summary>
        /// IFC exporter form: [system]number: description.
        /// Example: [OmniClass Table 13]13-23 17 11: Men's Restroom
        /// </summary>
        public static string IfcClassificationCode(string number, string title)
        {
            var category = Category(number, title);
            return category.Length == 0 ? string.Empty : "[" + SystemName + "]" + category;
        }

        public static IReadOnlyList<KeyValuePair<string, string>> ForSpace(string number, string title)
        {
            return new[]
            {
                new KeyValuePair<string, string>(NumberParameter, (number ?? string.Empty).Trim()),
                new KeyValuePair<string, string>(DescriptionParameter, (title ?? string.Empty).Trim()),
                new KeyValuePair<string, string>(CobieCategoryParameter, Category(number, title)),
                new KeyValuePair<string, string>(IfcClassificationCodeParameter, IfcClassificationCode(number, title))
            };
        }
    }
}
