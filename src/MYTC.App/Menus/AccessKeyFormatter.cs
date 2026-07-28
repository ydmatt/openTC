using System.Text;

namespace MYTC.App.Menus;

public static class AccessKeyFormatter
{
    public static string ToWpfHeader(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var result = new StringBuilder(label.Length + 4);
        for (var index = 0; index < label.Length; index++)
        {
            var character = label[index];
            if (character == '_')
            {
                result.Append("__");
                continue;
            }

            if (character != '&')
            {
                result.Append(character);
                continue;
            }

            if (index + 1 >= label.Length)
            {
                result.Append('&');
                continue;
            }

            var next = label[++index];
            if (next == '&')
            {
                result.Append('&');
            }
            else
            {
                result.Append('_');
                result.Append(next);
            }
        }

        return result.ToString();
    }
}
