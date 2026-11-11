using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;

namespace Crateside.Api;

/// <summary>Turns a request the binder rejected under <c>/api</c> into a 400 ProblemDetails that names the parameter and, for an enum, lists its values.</summary>
public static partial class ApiBindingErrors
{
    [GeneratedRegex(
        """Failed to bind parameter "(?<Type>[^"]+) (?<Name>[^"]+)" from "(?<Value>[^"]*)"\.?""",
        RegexOptions.ExplicitCapture
    )]
    private static partial Regex BindFailure { get; }

    public static IApplicationBuilder UseCratesideApiBindingErrors(this IApplicationBuilder app) =>
        app.Use(
            async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                    MatchEnumsIgnoringCase(context);

                try
                {
                    await next(context);
                }
                catch (BadHttpRequestException ex)
                    when (context.Request.Path.StartsWithSegments("/api")
                        && !context.Response.HasStarted
                    )
                {
                    context.Response.Clear();
                    context.Response.StatusCode = ex.StatusCode;
                    await Results
                        .Problem(
                            Describe(context, ex),
                            statusCode: ex.StatusCode,
                            title: "Bad request"
                        )
                        .ExecuteAsync(context);
                }
            }
        );

    // The binder matches an enum's names case-sensitively; a caller's "instaging" should reach InStaging.
    private static void MatchEnumsIgnoringCase(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<MethodInfo>() is not { } method)
            return;

        var query = context.Request.Query;
        var rewritten = new Dictionary<string, StringValues>(query);
        var changed = false;

        foreach (var parameter in method.GetParameters())
        {
            var type =
                Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

            if (
                !type.IsEnum
                || parameter.Name is not { } name
                || !query.TryGetValue(name, out var values)
            )
                continue;

            rewritten[name] = new StringValues([
                .. values.Select(value =>
                    Enum.GetNames(type)
                        .FirstOrDefault(known =>
                            known.Equals(value, StringComparison.OrdinalIgnoreCase)
                        )
                    ?? value
                ),
            ]);
            changed = true;
        }

        if (changed)
            context.Request.QueryString = QueryString.Create(rewritten);
    }

    private static string Describe(HttpContext context, BadHttpRequestException ex)
    {
        if (BindFailure.Match(ex.Message) is not { Success: true } failure)
            return ex.Message;

        var name = failure.Groups["Name"].Value;
        var value = failure.Groups["Value"].Value;
        var type = context
            .GetEndpoint()
            ?.Metadata.GetMetadata<MethodInfo>()
            ?.GetParameters()
            .FirstOrDefault(parameter => parameter.Name == name)
            ?.ParameterType;

        return (Nullable.GetUnderlyingType(type ?? typeof(void)) ?? type) switch
        {
            { IsEnum: true } enumType =>
                $"{name} must be one of: {string.Join(", ", Enum.GetNames(enumType))} (got \"{value}\").",
            _ => $"{name} cannot be read from \"{value}\".",
        };
    }
}
