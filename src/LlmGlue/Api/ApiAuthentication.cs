using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace LlmGlue.Api;

public sealed record ApiAuthenticationOptions(string? ReadToken, string? WriteToken, bool AllowUnauthenticatedLoopback = true);

public sealed class ApiAuthenticationMiddleware(RequestDelegate next, ApiAuthenticationOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (options.AllowUnauthenticatedLoopback && address is not null && IPAddress.IsLoopback(address))
        {
            await next(context);
            return;
        }

        var required = HttpMethods.IsPost(context.Request.Method) ? options.WriteToken : options.ReadToken;
        if (string.IsNullOrEmpty(required) || !TryBearer(context.Request.Headers.Authorization, required))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized" }, context.RequestAborted);
            return;
        }
        await next(context);
    }

    public static bool TryBearer(string? header, string expected)
    {
        const string prefix = "Bearer ";
        if (header is null || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var supplied = Encoding.UTF8.GetBytes(header[prefix.Length..]);
        var target = Encoding.UTF8.GetBytes(expected);
        return supplied.Length == target.Length && CryptographicOperations.FixedTimeEquals(supplied, target);
    }
}
