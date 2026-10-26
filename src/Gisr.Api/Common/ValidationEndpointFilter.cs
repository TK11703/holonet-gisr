using FluentValidation;

namespace Gisr.Api.Common;

public sealed class ValidationEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var cancellationToken = context.HttpContext.RequestAborted;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var argument in context.Arguments)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), cancellationToken);
            foreach (var group in result.Errors.GroupBy(e => ToCamelCase(e.PropertyName), StringComparer.Ordinal))
            {
                errors[group.Key] = [.. errors.TryGetValue(group.Key, out var existing) ? existing : [], .. group.Select(e => e.ErrorMessage)];
            }
        }

        return errors.Count > 0 ? Results.ValidationProblem(errors) : await next(context);
    }

    private static string ToCamelCase(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(segment =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
}
