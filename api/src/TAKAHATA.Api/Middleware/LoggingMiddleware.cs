using System.Text;

namespace TAKAHATA.Api.Middleware
{
    public class LoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LoggingMiddleware> _logger;

        public LoggingMiddleware(
            RequestDelegate next,
            ILogger<LoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            context.Request.EnableBuffering();

            string requestBody = "";

            using (var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                leaveOpen: true))
            {
                requestBody = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
            }

            _logger.LogInformation(
                """
                HTTP Request
                Method: {Method}
                Path: {Path}
                Body: {Body}
                """,
                context.Request.Method,
                context.Request.Path,
                requestBody);

            var originalBodyStream = context.Response.Body;

            using var responseBody = new MemoryStream();

            context.Response.Body = responseBody;

            try
            {
                await _next(context);
            }
            catch
            {
                context.Response.Body = originalBodyStream;
                throw;
            }

            responseBody.Seek(0, SeekOrigin.Begin);

            var responseText =
                await new StreamReader(responseBody)
                    .ReadToEndAsync();

            _logger.LogInformation(
                """
                HTTP Response
                StatusCode: {StatusCode}
                Body: {Body}
                """,
                context.Response.StatusCode,
                responseText);

            responseBody.Seek(0, SeekOrigin.Begin);

            await responseBody.CopyToAsync(originalBodyStream);
        }
    }
}
