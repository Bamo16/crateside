using System.Diagnostics;
using Crateside.Core;
using Crateside.Core.Library;

namespace Crateside;

/// <summary><c>retag-sources --matches &lt;file&gt; [--apply]</c>: a dry run unless told to apply (#148).</summary>
public static class RetagSourcesCommand
{
    public static int Run(CratesidePaths paths, string[] args)
    {
        var apply = args.Contains("--apply");

        if (
            args.SkipWhile(arg => arg != "--matches").Skip(1).FirstOrDefault() is not { } listPath
            || !File.Exists(listPath)
        )
        {
            Console.Error.WriteLine(
                "Usage: retag-sources --matches <file of '<videoId> <song>' lines> [--apply]"
            );

            return 2;
        }

        if (!SourceRetag.TryParse(File.ReadAllText(listPath), out var matches, out var problem))
        {
            Console.Error.WriteLine(problem);

            return 2;
        }

        var plan = SourceRetag.Plan(paths.LibraryFolder, matches);

        foreach (var match in matches.Where(match => plan.All(file => file.Match != match)))
            Console.WriteLine($"no files   {match.Song}");

        var failed = 0;

        foreach (var file in plan)
            if (file.Fate is not RetagFate.Retag)
                Console.WriteLine($"{Describe(file.Fate), -10} {file.Path}");
            else if (!apply)
                Console.WriteLine($"would tag  {file.Path}  <- {file.Match.Source}");
            else if (SourceRetag.TryWrite(file, out var why))
                Console.WriteLine($"tagged     {file.Path}");
            else
            {
                failed++;
                Console.WriteLine($"FAILED     {file.Path}: {why}");
            }

        Console.WriteLine(
            apply
                ? $"Tagged {plan.Count(file => file.Fate is RetagFate.Retag) - failed} files, {failed} failed."
                : $"Dry run: {plan.Count(file => file.Fate is RetagFate.Retag)} files would be tagged. Add --apply to write."
        );

        return failed > 0 ? 1 : 0;
    }

    private static string Describe(RetagFate fate) =>
        fate switch
        {
            RetagFate.AlreadyTagged => "already",
            RetagFate.HasOtherSource => "other src",
            _ => throw new UnreachableException(),
        };
}
