using System.Text;
using Keypuncher.Core;
using Keypuncher.Rendering;
using SkiaSharp;

namespace Keypuncher.Toy.Tests;

public sealed class SessionRecorderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-recorder-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_test_session_is_recorded_as_a_playable_video_in_real_time()
    {
        var world = new World { Style = ArtStyle.Toddler };
        world.Load(0, [], [], scene: new Scene(Landscape.Forest, TimeOfDay.Dawn));
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        using var surface = SKSurface.Create(new SKImageInfo(640, 360));
        string file;
        using (var recorder = new SessionRecorder(_dir, "test", () => new DateTime(2026, 10, 25, 18, 30, 0)))
        {
            // Three seconds at 50 frames a second, then a stall of one second.
            for (var t = 0.0; t < 3; t += 0.02)
            {
                renderer.Draw(surface.Canvas, 640, 360, world);
                recorder.Offer(surface, 640, 360, t);
                // Drawing here is far quicker than real time; a pause per kept frame lets the encoder keep up, as it
                // does at the toy's real pace, rather than its queue filling and frames being dropped.
                if (Math.Abs((t * SessionRecorder.Fps) - Math.Round(t * SessionRecorder.Fps)) < 1e-6) Thread.Sleep(50);
            }
            Thread.Sleep(50);
            recorder.Offer(surface, 640, 360, 4.0);
        }
        file = Directory.GetFiles(_dir).Single();

        Assert.Equal(Path.Combine(_dir, "test-2026-10-25-183000.avi"), file);
        var bytes = File.ReadAllBytes(file);
        Assert.Equal("RIFF", Ascii(bytes, 0));
        Assert.Equal("AVI ", Ascii(bytes, 8));
        Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));
        var frames = BitConverter.ToInt32(bytes, 48);
        Assert.Equal(17, frames); // 12 frames in three seconds, then the stall filled with copies, so 4 s is 17 frames
        Assert.Equal(SessionRecorder.Width, BitConverter.ToInt32(bytes, 64));
        Assert.Equal(540, BitConverter.ToInt32(bytes, 68));

        var movi = IndexOf(bytes, "movi");
        var idx1 = IndexOf(bytes, "idx1");
        Assert.True(movi > 0 && idx1 > movi);
        Assert.Equal(frames * 16, BitConverter.ToInt32(bytes, idx1 + 4));
        for (var i = 0; i < frames; i++)
        {
            var entry = idx1 + 8 + (i * 16);
            Assert.Equal("00dc", Ascii(bytes, entry));
            var chunk = movi + BitConverter.ToInt32(bytes, entry + 8);
            Assert.Equal("00dc", Ascii(bytes, chunk));
            Assert.Equal(0xFF, bytes[chunk + 8]); // a JPEG's start-of-image marker
            Assert.Equal(0xD8, bytes[chunk + 9]);
        }
    }

    private static string Ascii(byte[] bytes, int at) => Encoding.ASCII.GetString(bytes, at, 4);

    private static int IndexOf(byte[] bytes, string fourcc) => bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(fourcc));
}
