using System.Threading.Tasks.Dataflow;
using MonoTorrent;
using MonoTorrent.Client;
using ShellProgressBar;

const string dir = "/Volumes/TESLADRIVE/Torrents/";

var cts = new CancellationTokenSource();
var eventQueue = new BufferBlock<string>(new DataflowBlockOptions(){CancellationToken = cts.Token, TaskScheduler = TaskScheduler.Current, EnsureOrdered = false});
var addedFiles = new HashSet<string>();

var watcher = new FileSystemWatcher()
{
    Path = dir,
    NotifyFilter = NotifyFilters.LastWrite,
    Filter = "*.torrent",
    EnableRaisingEvents = true,
};

watcher.Changed += (_, e) =>
{
    if (!addedFiles.Contains(e.FullPath))
    {
        addedFiles.Add(e.FullPath);
        eventQueue.Post(e.FullPath);
    }
};

var mainOptions = new ProgressBarOptions()
{
    CollapseWhenFinished = true,
    DisableBottomPercentage = false,
    ShowEstimatedDuration = false,
    DenseProgressBar = false,
    DisplayTimeInRealTime = false
};

ProgressBar mbar = new IndeterminateProgressBar("[SERVICE] Waiting for torrents...", new ProgressBarOptions()
{
    DenseProgressBar = false,
    ProgressBarOnBottom = true,
});

void Download(string file) // change the parameter to non-nullable
{
    using var bar = mbar.Spawn(100, $"Downloading {Path.GetFileNameWithoutExtension(file)}...", mainOptions);
    using var engine = new ClientEngine();
    try
    {
        var tor = Torrent.Load(file);
        engine.AddAsync(tor, Directory.GetCurrentDirectory()).Wait(); // use synchronous method
        engine.StartAllAsync().Wait();
        
        while (engine.Torrents[0].State != TorrentState.Seeding && engine.IsRunning)
        {
            bar.Tick(Convert.ToInt32(engine.Torrents[0].Progress));
        }

        if (engine.Torrents[0].State == TorrentState.Seeding)
        {
            engine.StopAllAsync().Wait();
        }

        File.Delete(file);
    }
    catch
    {
        // ignored
    }
}

var downloadAction = new ActionBlock<string>(Download, new ExecutionDataflowBlockOptions()
{
    BoundedCapacity = -1, CancellationToken = cts.Token, MaxDegreeOfParallelism = Environment.ProcessorCount, TaskScheduler = TaskScheduler.Current
});

eventQueue.LinkTo(downloadAction, new DataflowLinkOptions()
{
    PropagateCompletion = false, Append = false
});

await eventQueue.Completion;
await downloadAction.Completion;
