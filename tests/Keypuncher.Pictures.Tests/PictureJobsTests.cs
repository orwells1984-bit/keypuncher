using Keypuncher.Director;
using Keypuncher.Pictures;

namespace Keypuncher.Pictures.Tests;

public sealed class PictureJobsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-jobs-tests").FullName;
    private readonly DateTime _now = new(2026, 11, 6, 12, 0, 0);
    private readonly PictureFolder _folder;
    private readonly FakeFetcher _fetcher = new();
    private readonly List<string> _log = [];
    private bool _enabled = true;

    public PictureJobsTests() => _folder = new PictureFolder(Path.Combine(_dir, "pictures"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private PictureJobs Jobs() => new(_folder, _fetcher, () => _enabled, _log.Add, () => _now);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void A_thing_is_fetched_once_per_run_and_landed_is_raised_with_its_name()
    {
        using var jobs = Jobs();
        var landed = new List<string>();
        jobs.Landed += landed.Add;

        Assert.True(jobs.Enqueue("sable antelope", RealCategory.Animal));
        Assert.True(jobs.WaitIdle(Patience));
        Assert.False(jobs.Enqueue("sable antelope", RealCategory.Animal));
        Assert.False(jobs.Enqueue("Sable  antelope!", RealCategory.Animal));
        Assert.True(jobs.WaitIdle(Patience));

        Assert.Equal(["sable antelope"], landed);
        Assert.Equal(1, _fetcher.CallsFor("sable antelope"));
        Assert.Equal(RealCategory.Animal, _fetcher.Calls.Single().Category);
        var status = jobs.Status;
        Assert.Equal((0, null, 1, 1), (status.Queued, status.Fetching, status.Tried, status.Pictured));
        Assert.Equal("sable antelope: Pictured", status.Last);
        Assert.Contains(_log, l => l == "picture of sable antelope: Pictured");
    }

    [Fact]
    public void Asking_again_fetches_a_thing_tried_already()
    {
        using var jobs = Jobs();
        jobs.Enqueue("puffin", RealCategory.Animal);
        jobs.WaitIdle(Patience);
        Assert.True(jobs.Enqueue("puffin", RealCategory.Animal, again: true));
        jobs.WaitIdle(Patience);
        Assert.Equal(2, _fetcher.CallsFor("puffin"));
    }

    [Fact]
    public void Enqueueing_puts_the_thing_on_the_wanted_list_with_its_category_guess()
    {
        _fetcher.Default = FetchOutcome.NothingSuitable;
        using var jobs = Jobs();
        jobs.Enqueue("puffin", RealCategory.Animal, categoryGuessed: true);
        jobs.WaitIdle(Patience);

        var wanted = Assert.Single(_folder.Wanted.All());
        Assert.Equal(("puffin", RealCategory.Animal, true, _now), (wanted.Name, wanted.Category, wanted.CategoryGuessed, wanted.Since));
        Assert.DoesNotContain(_log, l => l.Contains("Pictured"));
    }

    [Fact]
    public void A_thing_with_a_picture_is_not_fetched_unless_another_was_asked_for()
    {
        TestPhotos.WritePng(_folder.PicturePath("puffin"));
        TestPhotos.WritePng(_folder.HiddenPath("owl"));
        using var jobs = Jobs();

        Assert.False(jobs.Enqueue("puffin", RealCategory.Animal));
        Assert.False(jobs.Enqueue("owl", RealCategory.Animal));
        Assert.Empty(_folder.Wanted.All());

        _folder.Wanted.Add("puffin", RealCategory.Animal, _now);
        _folder.Wanted.Bump("puffin", "try another", _now, countTry: false, exclude: "File:Puffin 1.jpg");
        Assert.True(jobs.Enqueue("puffin", RealCategory.Animal));
        jobs.WaitIdle(Patience);
        Assert.Equal(1, _fetcher.CallsFor("puffin"));
        Assert.Equal(0, _fetcher.CallsFor("owl"));
    }

    [Fact]
    public void Retry_wanted_queues_everything_on_the_list()
    {
        _folder.Wanted.Add("puffin", RealCategory.Animal, _now);
        _folder.Wanted.Add("oak tree", RealCategory.Plant, _now.AddMinutes(1));
        _folder.Wanted.Add("tractor", RealCategory.Vehicle, _now.AddMinutes(2));
        using var jobs = Jobs();

        Assert.Equal(3, jobs.RetryWanted());
        jobs.WaitIdle(Patience);
        Assert.Equal(["puffin", "oak tree", "tractor"], _fetcher.Calls.Select(c => c.Name));
        Assert.Equal(RealCategory.Vehicle, _fetcher.Calls[2].Category);
        Assert.Equal(0, jobs.RetryWanted());
        Assert.Equal(3, jobs.RetryWanted(again: true));
        jobs.WaitIdle(Patience);
        Assert.Equal(6, _fetcher.Calls.Count);
    }

    [Fact]
    public void Switched_off_nothing_is_fetched_but_the_thing_is_still_wanted()
    {
        _enabled = false;
        using var jobs = Jobs();
        Assert.False(jobs.Enqueue("puffin", RealCategory.Animal));
        jobs.WaitIdle(Patience);
        Assert.Empty(_fetcher.Calls);
        Assert.Single(_folder.Wanted.All());

        _enabled = true;
        Assert.Equal(1, jobs.RetryWanted());
        jobs.WaitIdle(Patience);
        Assert.Equal(1, _fetcher.CallsFor("puffin"));
    }

    [Fact]
    public void The_switch_is_read_again_when_the_job_runs()
    {
        _fetcher.Gate = new SemaphoreSlim(0);
        using var jobs = Jobs();
        jobs.Enqueue("puffin", RealCategory.Animal);
        jobs.Enqueue("owl", RealCategory.Animal);
        for (var i = 0; i < 200 && jobs.Status.Fetching is null; i++) Thread.Sleep(10);
        _enabled = false;
        _fetcher.Gate.Release();
        jobs.WaitIdle(Patience);

        Assert.Equal(1, _fetcher.CallsFor("puffin"));
        Assert.Equal(0, _fetcher.CallsFor("owl"));
        Assert.Contains(_log, l => l.Contains("owl") && l.Contains("switched off"));
    }

    [Fact]
    public void A_fetch_that_throws_is_logged_and_the_worker_goes_on()
    {
        _fetcher.OnFetch = name => { if (name == "puffin") throw new InvalidOperationException("boom"); };
        using var jobs = Jobs();
        jobs.Enqueue("puffin", RealCategory.Animal);
        jobs.Enqueue("owl", RealCategory.Animal);
        jobs.WaitIdle(Patience);

        Assert.Equal(1, _fetcher.CallsFor("owl"));
        Assert.Contains(_log, l => l.Contains("puffin") && l.Contains("boom"));
        Assert.Equal((2, 1), (jobs.Status.Tried, jobs.Status.Pictured));
    }

    [Fact]
    public void Status_shows_what_is_being_fetched_and_what_waits()
    {
        _fetcher.Gate = new SemaphoreSlim(0);
        using var jobs = Jobs();
        jobs.Enqueue("puffin", RealCategory.Animal);
        jobs.Enqueue("owl", RealCategory.Animal);
        for (var i = 0; i < 200 && jobs.Status.Fetching is null; i++) Thread.Sleep(10);

        Assert.Equal(("puffin", 1), (jobs.Status.Fetching, jobs.Status.Queued));
        Assert.True(jobs.Busy);
        _fetcher.Gate.Release(2);
        Assert.True(jobs.WaitIdle(Patience));
        Assert.False(jobs.Busy);
    }

    [Fact]
    public void Finished_is_raised_for_every_fetch_whatever_it_came_to()
    {
        _fetcher.Outcomes["owl"] = FetchOutcome.NothingSuitable;
        _fetcher.OnFetch = name => { if (name == "puffin") throw new InvalidOperationException("boom"); };
        using var jobs = Jobs();
        var finished = new List<string>();
        jobs.Finished += name => { lock (finished) finished.Add(name); };

        jobs.Enqueue("puffin", RealCategory.Animal);
        jobs.Enqueue("owl", RealCategory.Animal);
        jobs.Enqueue("seal", RealCategory.Animal);
        Assert.True(jobs.WaitIdle(Patience));
        for (var i = 0; i < 200 && finished.Count < 3; i++) Thread.Sleep(10);

        lock (finished) Assert.Equal(["puffin", "owl", "seal"], finished);
    }

    [Fact]
    public void Is_pending_while_a_thing_is_fetched_or_waits_and_not_after()
    {
        _fetcher.Gate = new SemaphoreSlim(0);
        using var jobs = Jobs();
        jobs.Enqueue("puffin", RealCategory.Animal);
        jobs.Enqueue("owl", RealCategory.Animal);
        for (var i = 0; i < 200 && jobs.Status.Fetching is null; i++) Thread.Sleep(10);

        Assert.True(jobs.IsPending("puffin"));
        Assert.True(jobs.IsPending("Owl!"));
        Assert.False(jobs.IsPending("seal"));
        _fetcher.Gate.Release(2);
        Assert.True(jobs.WaitIdle(Patience));
        Assert.False(jobs.IsPending("puffin"));
        Assert.False(jobs.IsPending("owl"));
    }

    [Fact]
    public void Disposing_stops_the_worker_and_drops_the_queue()
    {
        _fetcher.Gate = new SemaphoreSlim(0);
        var jobs = Jobs();
        jobs.Enqueue("puffin", RealCategory.Animal);
        jobs.Enqueue("owl", RealCategory.Animal);
        for (var i = 0; i < 200 && jobs.Status.Fetching is null; i++) Thread.Sleep(10);

        var started = DateTime.UtcNow;
        Assert.True(jobs.Dispose());
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Equal(0, _fetcher.CallsFor("owl"));
    }

    [Fact]
    public void Disposing_says_when_a_fetch_would_not_stop_and_leaves_it_running()
    {
        // A fetch that takes no notice of being cancelled (a model run cannot be interrupted): Dispose gives up on
        // it after its two seconds and says so, rather than holding the caller until the fetch is done.
        using var hold = new ManualResetEventSlim();
        _fetcher.OnFetch = _ => hold.Wait();
        var jobs = Jobs();
        jobs.Enqueue("puffin", RealCategory.Animal);
        for (var i = 0; i < 200 && jobs.Status.Fetching is null; i++) Thread.Sleep(10);

        var started = DateTime.UtcNow;
        Assert.False(jobs.Dispose());
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(5));
        hold.Set();
    }
}
