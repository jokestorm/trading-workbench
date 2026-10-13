using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace TradingWorkbench;

public static partial class HostAccess
{
    // Use the transport peer, never a client-supplied forwarded IP.
    private static readonly IPAddress IngressGateway = IPAddress.Parse("172.30.32.2");
    [GeneratedRegex("^/[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeBasePath();

    public static bool Allowed(HttpContext context, bool ingress)
    {
        if (ingress)
        {
            var peer = context.Connection.RemoteIpAddress;
            if (peer?.IsIPv4MappedToIPv6 == true) peer = peer.MapToIPv4();
            return IngressGateway.Equals(peer)
                && BasePath(context) is not null
                && context.Request.Headers["Sec-Fetch-Site"] != "cross-site";
        }
        if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]")) return false;
        if (context.Request.Method is "POST" or "PUT" or "DELETE" or "PATCH")
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && origin != $"http://{context.Request.Host}") return false;
        }
        return true;
    }

    public static string? BasePath(HttpContext context)
    {
        var value = context.Request.Headers["X-Ingress-Path"].ToString().TrimEnd('/');
        return value.Length <= 4096 && SafeBasePath().IsMatch(value) ? value + "/" : null;
    }

    public static string IngressDocument(string html, HttpContext context)
        => html.Replace("<head>", "<head><base href=\"" + HtmlEncoder.Default.Encode(BasePath(context)!) + "\">", StringComparison.Ordinal);
}
