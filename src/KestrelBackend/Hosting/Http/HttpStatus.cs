namespace KestrelBackend;

internal static class HttpStatus
{
    public const int Ok = 200;
    public const int Created = 201;
    public const int NoContent = 204;
    public const int BadRequest = 400;
    public const int NotFound = 404;
    public const int MethodNotAllowed = 405;
    public const int InternalServerError = 500;

    public static string Phrase(int status) => status switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _ => "Unknown"
    };
}
