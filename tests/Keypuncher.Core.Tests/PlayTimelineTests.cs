using System.Text.Json;
using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public sealed class PlayTimelineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-timeline-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Each_event_is_one_json_line_with_the_clock_time_and_its_fields()
    {
        var path = Path.Combine(_dir, "play.timeline.jsonl");
        using (var timeline = new PlayTimeline(path, () => new DateTime(2026, 11, 15, 9, 30, 5, 250)))
        {
            timeline.Add("start", ("calm", "Baby"), ("camera", true));
            timeline.Add("shown", ("name", "puffin"), ("x", 0.123456), ("y", 0.5), ("text", null), ("code", 30));
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal("2026-11-15T09:30:05.250", first.RootElement.GetProperty("t").GetString());
        Assert.Equal("start", first.RootElement.GetProperty("ev").GetString());
        Assert.True(first.RootElement.GetProperty("camera").GetBoolean());
        using var second = JsonDocument.Parse(lines[1]);
        var root = second.RootElement;
        Assert.Equal("puffin", root.GetProperty("name").GetString());
        Assert.Equal(0.123, root.GetProperty("x").GetDouble());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("text").ValueKind);
        Assert.Equal(30, root.GetProperty("code").GetInt32());
        Assert.True(root.GetProperty("s").GetDouble() >= first.RootElement.GetProperty("s").GetDouble());
    }

    [Fact]
    public void Lines_can_be_read_while_play_goes_on_and_nothing_is_written_after_it_ends()
    {
        var path = Path.Combine(_dir, "live.timeline.jsonl");
        var timeline = new PlayTimeline(path);
        timeline.Add("key", ("code", 16));
        // Written as it happens, so a toy ended from Task Manager keeps everything up to then.
        using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            Assert.Contains("\"key\"", reader.ReadToEnd());
        timeline.Dispose();
        timeline.Add("late");
        Assert.Single(File.ReadAllLines(path));
    }

    [Fact]
    public void Things_put_on_screen_and_scene_changes_are_announced()
    {
        var world = new World();
        var added = new List<Entity>();
        var scenes = new List<Scene>();
        world.Added += added.Add;
        world.SceneChanged += scenes.Add;

        world.Add(new Entity { Id = world.NextId(), Role = EntityRole.Word, Text = "lundi", X = 0.2, Y = 0.3, Born = 0 });
        world.SetScene(world.Scene with { Landscape = Landscape.Beach });
        world.SetScene(world.Scene);

        Assert.Equal("lundi", Assert.Single(added).Text);
        Assert.Equal(Landscape.Beach, Assert.Single(scenes).Landscape);
    }
}
