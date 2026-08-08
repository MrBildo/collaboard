namespace Collaboard.Api.Hosting;

internal static class MethodNotAllowedResponse
{
    // Routing answers a method-restricted route (e.g. GET+PATCH on the card description
    // route) with a 405 that carries a correct `Allow` header but an EMPTY body. To an API
    // consumer that never inspects headers — a bot integrator especially — an empty error
    // reads like an auth or routing failure and costs real diagnosis time. This fills that
    // empty 405 with a short readable message naming the supported methods, sourced from the
    // same `Allow` header the response already carries.
    //
    // Deliberately global and deliberately narrow: it acts only on a bodyless 405, so it
    // covers every current and future method-restricted route uniformly while leaving the
    // `Allow` header, the status code, and every other response (including any 405 that ever
    // sets its own body) untouched. The body is a bare JSON string to match the API's error
    // convention — Results.BadRequest("...") and its siblings serialize a plain message the
    // same way — so a 405 reads on the wire exactly like every other error here.
    public static IApplicationBuilder UseMethodNotAllowedBody(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next();

            var response = context.Response;

            var isBodylessMethodNotAllowed =
                response.StatusCode == StatusCodes.Status405MethodNotAllowed
                && !response.HasStarted
                && response.ContentLength is null
                && string.IsNullOrEmpty(response.ContentType);

            if (!isBodylessMethodNotAllowed)
            {
                return;
            }

            var allow = response.Headers.Allow.ToString();

            var message = string.IsNullOrEmpty(allow)
                ? "Method not allowed."
                : $"Method not allowed. Supported: {allow}.";

            await response.WriteAsJsonAsync(message, context.RequestAborted);
        });
}
