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
var sourcePrefixLength = sourcePath.Length;

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
        SyncDirectories();
        break;
    case "restore":
        Console.WriteLine($"Start restore {sourcePath}");
        RestoreSource();
        break;
}

void SyncDirectories()
{
    var extensionFiles = Directory.GetFiles(extensionPath, "*.*", SearchOption.AllDirectories);
    var extensionChunks = extensionFiles.Chunk(1000);
    List<Task> tasks = new List<Task>();
    foreach (var chunk in extensionChunks)
        tasks.Add(Task.Run(() =>
        {
            controlCopySemaphore.Wait();
            foreach (var path in chunk)
                CreateSymLinkFromExtension(path);

            controlCopySemaphore.Release();
        }));

    Task.WaitAll(tasks.ToArray());
}

void RestoreSource()
{
    var replacedFiles = Directory.GetFiles(sourcePath, "*.replaced", SearchOption.AllDirectories);
    var replacedChunks = replacedFiles.Chunk(1000);
    List<Task> tasks = new List<Task>();
    foreach (var chunk in replacedChunks)
        tasks.Add(Task.Run(() =>
        {
            controlCopySemaphore.Wait();
            foreach (var path in chunk)
            {
                var relativePath = path.Substring(sourcePrefixLength);
                var fileInfo = new FileInfo(path);
                var extensionFilePath = extensionPath + relativePath.Substring(0, relativePath.Length - 9);
                if (!File.Exists(extensionFilePath))
                {
                    fileInfo.MoveTo(path.Substring(0, path.Length - 9));
                    Console.WriteLine($"Symlink {relativePath} restored");
                }
            }

            controlCopySemaphore.Release();
        }));
    Task.WaitAll(tasks.ToArray());
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