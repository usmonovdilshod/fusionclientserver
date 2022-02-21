namespace FusionBlog.Abstractions
{
    public static class IDictionaryExtension
    {
        public static string Serialize(this IDictionary<string,string> dictionary)
        {
            string[] array = new string[dictionary.Count];
            int num = 0;
            using (IEnumerator<KeyValuePair<string, string>> enumerator = dictionary.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    KeyValuePair<string, string> current = enumerator.Current;
                    string key = current.Key;
                    string str = Convert.ToBase64String(Encoding.UTF8.GetBytes(current.Value));
                    array[num++] = key + " " + str;
                }
            }

            return string.Join(",", array);
        }
    }


}
