using Crateside.Demo;

if (args is not [var root])
{
    Console.Error.WriteLine("usage: Crateside.Demo <folder to write the demo library into>");

    return 2;
}

if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
{
    Console.Error.WriteLine($"{root} is not empty; the demo library is written into a new folder.");

    return 2;
}

new DemoLibrary(Path.GetFullPath(root), DateTimeOffset.UtcNow).Write();

Console.WriteLine($"Demo library written to {Path.GetFullPath(root)}");

return 0;
