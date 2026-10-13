using System.Net;

namespace TradingWorkbench;

public static class HostAccessChecks
{
    public static void Run()
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("127.0.0.1", 5175);
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        Check(HostAccess.Allowed(context, false), "desktop localhost access preserved");
        context.Request.Method = "POST";
        context.Request.Headers.Origin = "https://other.example";
        Check(!HostAccess.Allowed(context, false), "desktop cross-origin writes rejected");
        context.Request.Headers["X-Ingress-Path"] = "/api/hassio_ingress/example-token";
        context.Request.Headers["X-Forwarded-For"] = "172.30.32.2";
        Check(!HostAccess.Allowed(context, true), "spoofed ingress headers cannot bypass peer restriction");
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:172.30.32.2");
        Check(HostAccess.Allowed(context, true), "authenticated ingress gateway accepted");
        Check(HostAccess.IngressDocument("<html><head></head></html>", context).Contains("<base href=\"/api/hassio_ingress/example-token/\">"), "ingress base injected in document");
        context.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        Check(!HostAccess.Allowed(context, true), "cross-site ingress requests rejected");
        context.Request.Headers.Remove("Sec-Fetch-Site");
        foreach (var path in new[] { "", "//other.example", "/a/../b", "/a?query", "/a\" onclick=\"x" })
        {
            context.Request.Headers["X-Ingress-Path"] = path;
            Check(!HostAccess.Allowed(context, true), "invalid ingress path rejected");
        }
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAIL " + label);
        Console.WriteLine("PASS " + label);
    }
}
