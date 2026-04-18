using SmoothMice.Infrastructure.Windows;

var outPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "src", "SmoothMice.App", "SmoothMice.ico"));

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
IconFactory.SaveApplicationIconFile(outPath);
Console.WriteLine(outPath);
