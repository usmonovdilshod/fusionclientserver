using System.Text.RegularExpressions;

namespace FusionBlog.Services;

public class SlugHelper
{
    public static string GenerateSlug(string phrase)
    {
        string str = RemoveAccent(phrase).ToLower();
        // invalid chars
        str = Regex.Replace(str, @"[^a-z0-9\s-.]", "");
        // convert multiple spaces into one space
        str = Regex.Replace(str, @"\s+", " ").Trim();
        // cut and trim
        str = str.Substring(0, str.Length <= 45 ? str.Length : 45).Trim();
        str = Regex.Replace(str, @"\s", "-"); // hyphens
        return str;
    }
    public static string RemoveAccent(string txt)
    {
        System.Text.EncodingProvider provider = System.Text.CodePagesEncodingProvider.Instance;
        Encoding.RegisterProvider(provider);
        byte[] bytes = System.Text.Encoding.GetEncoding("Cyrillic").GetBytes(txt);
        return System.Text.Encoding.ASCII.GetString(bytes);
    }
}