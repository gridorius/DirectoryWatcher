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
            (sender, eventArgs) => CopyFromExtension(eventArgs.FullPath),
            (sender, eventArgs) => CopyFromExtension(eventArgs.FullPath),
            (sender, eventArgs) => DeleteFromExtension(eventArgs.FullPath),
            (sender, eventArgs) =>
            {
                DeleteFromExtension(eventArgs.OldFullPath);
                CopyFromExtension(eventArgs.FullPath);
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
        SyncDirectories();
        break;
}

void SyncDirectories()
{
    var extensionFiles = Directory
        .GetFiles(extensionPath, "*.*", SearchOption.AllDirectories)
        .Distinct();
    var extensionChunks = extensionFiles.Chunk(1000);
    List<Task> tasks = new List<Task>();
    foreach (var chunk in extensionChunks)
        tasks.Add(Task.Run(() =>
        {
            controlCopySemaphore.Wait();
            foreach (var path in chunk)
                CopyFromExtension(path);

            controlCopySemaphore.Release();
        }));

    Task.WaitAll(tasks.ToArray());
    Console.WriteLine($"End sync from {extensionPath} to {sourcePath}");
}

void CopyFromExtension(string path)
{
    var relativePath = path.Substring(extPrefixLength);
    var targetFilePath = sourcePath + relativePath;
    var file = new FileInfo(path);
    Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath));
    file.CopyTo(targetFilePath, true);
    Console.WriteLine($"File {relativePath} copied");
}

void DeleteFromExtension(string path)
{
    var relativePath = path.Substring(extPrefixLength);
    var targetFilePath = sourcePath + relativePath;
    var targetFile = new FileInfo(targetFilePath);
    if (targetFile.Exists)
    {
        File.Delete(targetFilePath);
        Console.WriteLine($"File {relativePath} deleted");
    }
}

FileSystemWatcher CreateWatcher(string path, FileSystemEventHandler onCreated,
    FileSystemEventHandler onChanged, FileSystemEventHandler onDeleted, RenamedEventHandler onRenamed)
{
    FileSystemWatcher watcher = new FileSystemWatcher();
    watcher.Path = path;
    watcher.NotifyFilter = NotifyFilters.Attributes
                           | NotifyFilters.FileName
                           | NotifyFilters.Size;
    watcher.Filter = "*.*";
    watcher.IncludeSubdirectories = true;
    watcher.EnableRaisingEvents = true;
    watcher.Created += onCreated;
    watcher.Changed += onChanged;
    watcher.Deleted += onDeleted;
    watcher.Renamed += onRenamed;
    return watcher;
}