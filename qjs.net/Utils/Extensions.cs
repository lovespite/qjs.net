using System.Text;

namespace QuickJsNet.Utils;

internal static class Extensions
{
    internal static string FlatMessage(this Exception? ex)
    {
        var sb = new StringBuilder();
        var depth = 1;
        while (ex != null)
        {
            sb
                .Append(ex.GetType().Name)
                .Append(": ")
                .Append(ex.Message)
                .Append('\n')
                .Append(new string(' ', depth * 2))
                .Append("└ ");
            ex = ex.InnerException;
            ++depth;
        }
        return sb.ToString().TrimEnd([' ', '\n', '└']);
    }
}
