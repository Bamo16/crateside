using System.Text.Json.Serialization;
using Crateside.Core.Plans;

namespace Crateside.Core.Imports;

/// <summary>A named plan a track is taken in with (ADR 0077).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Recipe>))]
public enum Recipe
{
    /// <summary>The usual pair of stems and a warp guide.</summary>
    Standard,

    /// <summary>The source and the drum guide, for a track with no vocal to separate.</summary>
    Instrumental,
}

public static class Recipes
{
    public static StemPlan PlanOf(Recipe recipe) =>
        recipe switch
        {
            Recipe.Standard => StemPlan.Default,
            Recipe.Instrumental => StemPlan.InstrumentalRelease,
            _ => throw new ArgumentOutOfRangeException(nameof(recipe)),
        };

    /// <summary>The recipe a folder name asks for, compared without case; null for any other name.</summary>
    public static bool TryParse(string? name, out Recipe recipe) =>
        Enum.TryParse(name?.Trim(), ignoreCase: true, out recipe) && Enum.IsDefined(recipe);
}

/// <summary>The recipe Settings chose, kept in <c>%APPDATA%\crateside\recipe.json</c>.</summary>
public sealed class RecipeStore(CratesidePaths paths)
{
    private readonly string _path = paths.RecipeFile;
    private readonly Lock _gate = new();

    /// <summary>Never throws: an unreadable file reads as Standard.</summary>
    public Recipe Default
    {
        get
        {
            lock (_gate)
                return RecipeJsonContext.Default.RecipeChoice.ReadFileOrDefault(_path)?.Default
                    ?? Recipe.Standard;
        }
    }

    public void Choose(Recipe recipe)
    {
        lock (_gate)
        {
            try
            {
                RecipeJsonContext.Default.RecipeChoice.WriteFile(_path, new RecipeChoice(recipe));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal sealed record RecipeChoice(Recipe Default);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(RecipeChoice))]
internal sealed partial class RecipeJsonContext : JsonSerializerContext { }
