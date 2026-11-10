namespace EcommerceBackend.Middleware;

public sealed record ErrorResponse(int Code, string Message, string TraceId);
