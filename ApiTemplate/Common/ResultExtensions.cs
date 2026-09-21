using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ApiTemplate.Common;

public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase controller) =>
        result.IsSuccess
            ? controller.Ok(result.Value)
            : Problem(controller, result.Error, result.Message);

    public static IActionResult ToCreatedResult<T>(
        this Result<T> result, ControllerBase controller, Func<T, string> location) =>
        result.IsSuccess
            ? controller.Created(location(result.Value!), result.Value)
            : Problem(controller, result.Error, result.Message);

    public static IActionResult ToNoContentResult(this Result result, ControllerBase controller) =>
        result.IsSuccess
            ? controller.NoContent()
            : Problem(controller, result.Error, result.Message);

    private static IActionResult Problem(ControllerBase controller, ResultError error, string? message) =>
        controller.Problem(statusCode: StatusCodeFor(error), title: message);

    private static int StatusCodeFor(ResultError error) => error switch
    {
        ResultError.Validation => StatusCodes.Status400BadRequest,
        ResultError.Unauthorized => StatusCodes.Status401Unauthorized,
        ResultError.Forbidden => StatusCodes.Status403Forbidden,
        ResultError.NotFound => StatusCodes.Status404NotFound,
        ResultError.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };
}
