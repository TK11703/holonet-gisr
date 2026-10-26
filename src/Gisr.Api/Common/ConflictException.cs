namespace Gisr.Api.Common;

public sealed class ConflictException(string detail)
    : ProblemException(StatusCodes.Status409Conflict, "Conflict", detail);
