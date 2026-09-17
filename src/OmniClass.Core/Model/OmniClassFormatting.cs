namespace OmniClass.Core.Model
{
    /// <summary>
    /// How the number and name are written together into COBie.Space.Category.
    /// </summary>
    public static class OmniClassFormatting
    {
        public static string Category(string number, string title)
        {
            number = (number ?? string.Empty).Trim();
            title = (title ?? string.Empty).Trim();

            if (number.Length == 0) return title;
            if (title.Length == 0) return number;
            return number + ": " + title;
        }
    }
}
