using System.IO;
using LibVLCSharp.Shared;

namespace NightOwls.Services;

public class LibVlcHost : IDisposable
{
    private LibVLC? _libVlc;
    private readonly object _gate = new();

    public LibVLC Instance
    {
        get
        {
            if (_libVlc is null)
            {
                lock (_gate)
                {
                    if (_libVlc is null)
                    {
                        Core.Initialize();
                        _libVlc = new LibVLC("--no-video-title-show", "--quiet");
                    }
                }
            }
            return _libVlc;
        }
    }

    public Media CreateMedia(string path) => new(Instance, new Uri(path));

    public void Dispose()
    {
        lock (_gate)
        {
            _libVlc?.Dispose();
            _libVlc = null;
        }
    }
}
