using System.Text;

namespace Keypuncher.Rendering;

/// <summary>
/// Writes JPEG frames into a Motion-JPEG AVI, which Windows' Media Player, VLC and ffmpeg all play without any
/// codec installed. The headers are brought up to date every second of video, so a file cut short by the app
/// being ended from Task Manager still plays up to then (without its index, which only <see cref="Dispose"/> writes).
/// </summary>
public sealed class MjpegAviWriter : IDisposable
{
    private readonly FileStream _file;
    private readonly BinaryWriter _out;
    private readonly int _fps;
    private readonly List<(long Offset, int Size)> _index = [];
    private readonly long _riffSize, _totalFrames, _streamLength, _moviSize, _moviStart;
    private int _largest;

    public MjpegAviWriter(string path, int width, int height, int fps)
    {
        _fps = fps;
        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _out = new BinaryWriter(_file, Encoding.ASCII);

        Fourcc("RIFF");
        _riffSize = Placeholder();
        Fourcc("AVI ");

        Fourcc("LIST");
        _out.Write(4 + (8 + 56) + (8 + 4 + (8 + 56) + (8 + 40)));
        Fourcc("hdrl");

        Fourcc("avih");
        _out.Write(56);
        _out.Write(1_000_000 / fps);  // microseconds a frame
        _out.Write(0);                // max bytes a second
        _out.Write(0);                // padding
        _out.Write(0x10);             // has an index
        _totalFrames = Placeholder();
        _out.Write(0);                // initial frames
        _out.Write(1);                // streams
        _out.Write(0);                // suggested buffer size
        _out.Write(width);
        _out.Write(height);
        _out.Write(new byte[16]);

        Fourcc("LIST");
        _out.Write(4 + (8 + 56) + (8 + 40));
        Fourcc("strl");

        Fourcc("strh");
        _out.Write(56);
        Fourcc("vids");
        Fourcc("MJPG");
        _out.Write(0);                // flags
        _out.Write(0);                // priority and language
        _out.Write(0);                // initial frames
        _out.Write(1);                // scale
        _out.Write(fps);              // rate: frames a second is rate / scale
        _out.Write(0);                // start
        _streamLength = Placeholder();
        _out.Write(0);                // suggested buffer size
        _out.Write(-1);               // quality
        _out.Write(0);                // sample size
        _out.Write((short)0);
        _out.Write((short)0);
        _out.Write((short)width);
        _out.Write((short)height);

        Fourcc("strf");
        _out.Write(40);
        _out.Write(40);
        _out.Write(width);
        _out.Write(height);
        _out.Write((short)1);
        _out.Write((short)24);
        Fourcc("MJPG");
        _out.Write(width * height * 3);
        _out.Write(new byte[16]);

        Fourcc("LIST");
        _moviSize = Placeholder();
        _moviStart = _file.Position;
        Fourcc("movi");
    }

    public int Frames => _index.Count;

    public long Length => _file.Length;

    public void AddFrame(ReadOnlySpan<byte> jpeg)
    {
        _index.Add((_file.Position - _moviStart, jpeg.Length));
        Fourcc("00dc");
        _out.Write(jpeg.Length);
        _out.Write(jpeg);
        if (jpeg.Length % 2 == 1) _out.Write((byte)0);
        _largest = Math.Max(_largest, jpeg.Length);
        if (_index.Count % _fps == 0) Patch();
    }

    public void Dispose()
    {
        var moviEnd = _file.Position;
        Fourcc("idx1");
        _out.Write(_index.Count * 16);
        foreach (var (offset, size) in _index)
        {
            Fourcc("00dc");
            _out.Write(0x10);         // a key frame: every JPEG is
            _out.Write((int)offset);
            _out.Write(size);
        }
        Patch(moviEnd);
        _out.Dispose();
    }

    /// <summary>Brings the sizes and frame counts in the headers up to date, then returns to the end.</summary>
    private void Patch(long? moviEnd = null)
    {
        _out.Flush();
        var end = _file.Position;
        Write(_riffSize, (int)(end - 8));
        Write(_totalFrames, _index.Count);
        Write(_streamLength, _index.Count);
        Write(_moviSize, (int)((moviEnd ?? end) - _moviStart));
        _file.Position = end;
        _out.Flush();
    }

    private void Write(long at, int value)
    {
        _file.Position = at;
        _out.Write(value);
    }

    private long Placeholder()
    {
        var at = _file.Position;
        _out.Write(0);
        return at;
    }

    private void Fourcc(string code) => _out.Write(Encoding.ASCII.GetBytes(code));
}
