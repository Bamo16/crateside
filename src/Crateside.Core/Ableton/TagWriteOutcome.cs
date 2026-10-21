namespace Crateside.Core.Ableton;

/// <summary>How one write to a folder's tag store turned out.</summary>
public abstract record TagWriteOutcome
{
    private TagWriteOutcome() { }

    /// <param name="Applied">How many files were given keywords, or renamed.</param>
    public sealed record Written(int Applied) : TagWriteOutcome;

    /// <summary>The store moved underneath the write, or could not be opened; trying again fixes it.</summary>
    public sealed record Stale(string Error) : TagWriteOutcome;

    /// <summary>The request itself was wrong, so trying again will not fix it.</summary>
    public sealed record Rejected(string Error) : TagWriteOutcome;

    /// <summary>Runs one of AbleKit's tag writes, turning what it throws into an outcome.</summary>
    public static TagWriteOutcome Of(Func<int> write)
    {
        try
        {
            return new Written(write());
        }
        catch (ArgumentException ex)
        {
            return new Rejected(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Stale(ex.Message);
        }
    }
}
