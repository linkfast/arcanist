using Arcanist.Core.Application;

namespace Arcanist.Core.Api;

public static class Problems
{
    public static IResult Create(int statusCode, string code, string? detail = null) =>
        Results.Problem(statusCode: statusCode, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult ToResult(this ValidationErrors errors) =>
        Results.ValidationProblem(errors.ToDictionary(), extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationFailed });
}
