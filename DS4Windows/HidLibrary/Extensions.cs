using System.Text;

namespace DS4Windows
{
    public static class Extensions
    {
        public static string ToUTF8String(this byte[] buffer)
        {
            var value = Encoding.UTF8.GetString(buffer);
            int end = value.IndexOf((char)0);
            return end >= 0 ? value.Remove(end) : value;
        }

        public static string ToUTF16String(this byte[] buffer)
        {
            var value = Encoding.Unicode.GetString(buffer);
            int end = value.IndexOf((char)0);
            return end >= 0 ? value.Remove(end) : value;
        }
    }
}