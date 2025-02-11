var arguments = Environment.GetCommandLineArgs();
if (arguments.Length < 4)
    throw new Exception("Invalid command line arguments(need target_path extension_path)");
var command = arguments[1];
var sourcePath = Path.GetFullPath(arguments[2]);
var extensionPath = Path.GetFullPath(arguments[3]);
var controlCopySemaphore = new SemaphoreSlim(10, 10);
if (!Directory.Exists(sourcePath))
    throw new DirectoryNotFoundException("Target directory does not exist");
if (!Directory.Exists(extensionPath))
    throw new DirectoryNotFoundException("Extension directory does not exist");
var extPrefixLength = extensionPath.Length;

switch (command)
{
    case "watch":
        var extWatcher = CreateWatcher(extensionPath,
            (sender, eventArgs) => CreateSymLinkFromExtension(eventArgs.FullPath),
            (sender, eventArgs) => DeleteFromExtension(eventArgs.FullPath),
            (sender, eventArgs) =>
            {
                DeleteFromExtension(eventArgs.OldFullPath);
                CreateSymLinkFromExtension(eventArgs.FullPath);
            });
        Console.WriteLine($"Start watching from {extensionPath} to {sourcePath}");
        await Task.Run(() =>
        {
            while (true)
                extWatcher.WaitForChanged(WatcherChangeTypes.All);
        });
        break;
    case "sync":
        Console.WriteLine($"Start sync from {extensionPath} to {sourcePath}");
        InitialCopy();
        break;
}

void InitialCopy()
{
    var extensionFiles = Directory.GetFiles(extensionPath, "*.*", SearchOption.AllDirectories);
    var extensionChunks = extensionFiles.Chunk(1000);
    foreach (var chunk in extensionChunks)
        Task.Run(() =>
        {
            controlCopySemaphore.Wait();
            foreach (var path in chunk)
                CreateSymLinkFromExtension(path);
            controlCopySemaphore.Release();
        });
}


void CreateSymLinkFromExtension(string path)
{
    var relativePath = path.Substring(extPrefixLength);
    var targetFilePath = sourcePath + relativePath;
    var targetFile = new FileInfo(targetFilePath);
    if (targetFile.Exists)
    {
        if (targetFile.LinkTarget != null)
            return;
        targetFile.MoveTo(targetFilePath + ".replaced");
        Console.WriteLine($"Source file {relativePath} saved");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath));
    File.CreateSymbolicLink(targetFilePath, path);
    Console.WriteLine($"Symlink {relativePath} created");
}

void DeleteFromExtension(string path)
{
    var relativePath = path.Substring(extPrefixLength);
    var targetFilePath = sourcePath + relativePath;
    var targetFile = new FileInfo(targetFilePath);
    var replacedFile = new FileInfo(targetFilePath + ".replaced");
    if (targetFile.Exists)
    {
        File.Delete(targetFilePath);
        Console.WriteLine($"Symlink {relativePath} deleted");
    }

    if (replacedFile.Exists)
    {
        replacedFile.MoveTo(replacedFile.FullName.Substring(0, replacedFile.FullName.Length - 9));
        Console.WriteLine($"Symlink {relativePath} restored");
    }
}

FileSystemWatcher CreateWatcher(string path, FileSystemEventHandler onCreated,
    FileSystemEventHandler onDeleted, RenamedEventHandler onRenamed)
{
    FileSystemWatcher watcher = new FileSystemWatcher();
    watcher.Path = path;
    watcher.NotifyFilter = NotifyFilters.Attributes
                           | NotifyFilters.FileName;
    watcher.Filter = "*.*";
    watcher.IncludeSubdirectories = true;
    watcher.EnableRaisingEvents = true;
    watcher.Created += onCreated;
    watcher.Deleted += onDeleted;
    watcher.Renamed += onRenamed;
    return watcher;
}