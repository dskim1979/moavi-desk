/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

#pragma warning disable IDE0048   // pixel and protocol arithmetic reads plainer without the extra brackets

using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace MoaviDesk.Services;

/// <summary>
/// A VNC (RFB 3.8) client for the app's own viewer (Android, where there is no remote-viewer): the engine's
/// console websocket, VNC password authentication, 32-bit BGRX pixels, Raw / CopyRect / ZRLE and desktop resize.
/// The frame buffer is <see cref="Pixels"/> (Width × Height × 4, B G R X); <see cref="Updated"/> comes after each
/// update, on the reading thread.
/// </summary>
internal sealed class RfbClient : IAsyncDisposable
{
    private readonly Stream _s;
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly ZlibFeed _zfeed = new();
    private ZLibStream? _zlib;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public byte[] Pixels { get; private set; } = [];
    public string Name { get; private set; } = "";
    public readonly object Sync = new();

    public event Action? Updated;
    public event Action<string?>? Closed;
    public event Action<string>? CutText;

    private RfbClient(Stream s) => _s = s;

    /// <summary>Connects through the engine's console websocket (the same one the VNC bridge uses).</summary>
    public static async Task<RfbClient> ConnectAsync(DeskApi api, string wsPath, string password, CancellationToken ct = default)
    {
        var ws = await EngineSocket.ConnectAsync(api, wsPath, ct);
        var c = new RfbClient(new WebSocketByteStream(ws));
        try { await c.HandshakeAsync(password, ct); }
        catch { await c.DisposeAsync(); throw; }
        return c;
    }

    /// <summary>Over any byte stream (tests: a TCP connection to a VNC server).</summary>
    public static async Task<RfbClient> ConnectAsync(Stream stream, string password, CancellationToken ct = default)
    {
        var c = new RfbClient(stream);
        try { await c.HandshakeAsync(password, ct); }
        catch { await c.DisposeAsync(); throw; }
        return c;
    }

    // --- handshake ---------------------------------------------------------------------------------
    private async Task HandshakeAsync(string password, CancellationToken ct)
    {
        var version = Encoding.ASCII.GetString(await ReadAsync(12, ct));
        if (!version.StartsWith("RFB ", StringComparison.Ordinal)) { throw new IOException("not a VNC server"); }
        var minor = int.TryParse(version.AsSpan(8, 3), out var m) ? m : 3;
        var v33 = minor < 7;
        await _s.WriteAsync(Encoding.ASCII.GetBytes(v33 ? "RFB 003.003\n" : "RFB 003.008\n"), ct);

        int type;
        if (v33)
        {
            type = (int)await U32Async(ct);
            if (type == 0) { throw new IOException(await ReasonAsync(ct)); }
        }
        else
        {
            var n = (await ReadAsync(1, ct))[0];
            if (n == 0) { throw new IOException(await ReasonAsync(ct)); }
            var types = await ReadAsync(n, ct);
            type = types.Contains((byte)2) && password.Length > 0 ? 2 : types.Contains((byte)1) ? 1 : types.Contains((byte)2) ? 2 : 0;
            if (type == 0) { throw new IOException("no supported VNC security type"); }
            await _s.WriteAsync(new[] { (byte)type }, ct);
        }

        if (type == 2)
        {
            var challenge = await ReadAsync(16, ct);
            await _s.WriteAsync(VncAuth(challenge, password), ct);
        }
        // SecurityResult: always in 3.8 (with a reason when refused), after VNC authentication before that
        if (type == 2 || minor >= 8)
        {
            var result = await U32Async(ct);
            if (result != 0) { throw new IOException(minor >= 8 ? await ReasonAsync(ct) : "VNC authentication failed"); }
        }

        await _s.WriteAsync(new byte[] { 1 }, ct);   // shared
        var init = await ReadAsync(24, ct);
        Resize(BinaryPrimitives.ReadUInt16BigEndian(init), BinaryPrimitives.ReadUInt16BigEndian(init.AsSpan(2)));
        Name = Encoding.UTF8.GetString(await ReadAsync((int)BinaryPrimitives.ReadUInt32BigEndian(init.AsSpan(20)), ct));

        // 32-bit true colour, little endian, R<<16 G<<8 B: bytes B G R X in memory
        var pf = new byte[20];
        pf[0] = 0;
        pf[4] = 32; pf[5] = 24; pf[6] = 0; pf[7] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(pf.AsSpan(8), 255);
        BinaryPrimitives.WriteUInt16BigEndian(pf.AsSpan(10), 255);
        BinaryPrimitives.WriteUInt16BigEndian(pf.AsSpan(12), 255);
        pf[14] = 16; pf[15] = 8; pf[16] = 0;
        await _s.WriteAsync(pf, ct);

        int[] encodings = [16 /* ZRLE */, 1 /* CopyRect */, 0 /* Raw */, -223 /* DesktopSize */];
        var se = new byte[4 + encodings.Length * 4];
        se[0] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(se.AsSpan(2), (ushort)encodings.Length);
        for (var i = 0; i < encodings.Length; i++) { BinaryPrimitives.WriteInt32BigEndian(se.AsSpan(4 + i * 4), encodings[i]); }
        await _s.WriteAsync(se, ct);
    }

    /// <summary>VNC authentication: the challenge DES-encrypted with the password (8 bytes, each byte's bits reversed).</summary>
    private static byte[] VncAuth(byte[] challenge, string password)
    {
        var key = new byte[8];
        var pw = Encoding.Latin1.GetBytes(password);
        for (var i = 0; i < 8 && i < pw.Length; i++)
        {
            var b = pw[i];
            byte r = 0;
            for (var bit = 0; bit < 8; bit++) { if ((b & (1 << bit)) != 0) { r |= (byte)(0x80 >> bit); } }
            key[i] = r;
        }
#pragma warning disable CA5351   // the RFB protocol's own authentication
        using var des = DES.Create();
        des.Key = key;
        return des.EncryptEcb(challenge, PaddingMode.None);
#pragma warning restore CA5351
    }

    // --- the update loop ----------------------------------------------------------------------------
    public void Start() => _ = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        string? error = null;
        try
        {
            var ct = _cts.Token;
            await RequestAsync(false);
            while (!ct.IsCancellationRequested)
            {
                var type = (await ReadAsync(1, ct))[0];
                switch (type)
                {
                    case 0:
                        await UpdateAsync(ct);
                        Updated?.Invoke();
                        await RequestAsync(true);
                        break;
                    case 1:
                        var h = await ReadAsync(5, ct);
                        await ReadAsync(BinaryPrimitives.ReadUInt16BigEndian(h.AsSpan(3)) * 6, ct);
                        break;
                    case 2:
                        break;   // bell
                    case 3:
                        await ReadAsync(3, ct);
                        var text = await ReadAsync((int)await U32Async(ct), ct);
                        CutText?.Invoke(Encoding.Latin1.GetString(text));
                        break;
                    default:
                        throw new IOException($"VNC message {type}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { error = ex.Message; }
        Closed?.Invoke(error);
    }

    private async Task UpdateAsync(CancellationToken ct)
    {
        var h = await ReadAsync(3, ct);
        int rects = BinaryPrimitives.ReadUInt16BigEndian(h.AsSpan(1));
        for (var r = 0; r < rects; r++)
        {
            var rh = await ReadAsync(12, ct);
            int x = BinaryPrimitives.ReadUInt16BigEndian(rh), y = BinaryPrimitives.ReadUInt16BigEndian(rh.AsSpan(2));
            int w = BinaryPrimitives.ReadUInt16BigEndian(rh.AsSpan(4)), hh = BinaryPrimitives.ReadUInt16BigEndian(rh.AsSpan(6));
            var enc = BinaryPrimitives.ReadInt32BigEndian(rh.AsSpan(8));
            switch (enc)
            {
                case 0:
                    var raw = await ReadAsync(w * hh * 4, ct);
                    lock (Sync) { for (var row = 0; row < hh; row++) { Buffer.BlockCopy(raw, row * w * 4, Pixels, ((y + row) * Width + x) * 4, w * 4); } }
                    break;
                case 1:
                    var src = await ReadAsync(4, ct);
                    CopyRect(BinaryPrimitives.ReadUInt16BigEndian(src), BinaryPrimitives.ReadUInt16BigEndian(src.AsSpan(2)), x, y, w, hh);
                    break;
                case 16:
                    var data = await ReadAsync((int)await U32Async(ct), ct);
                    Zrle(data, x, y, w, hh);
                    break;
                case -223:
                    lock (Sync) { Resize(w, hh); }
                    break;
                default:
                    throw new IOException($"VNC encoding {enc}");
            }
        }
    }

    private void Resize(int w, int h)
    {
        Width = w;
        Height = h;
        Pixels = new byte[w * h * 4];
    }

    private void CopyRect(int sx, int sy, int x, int y, int w, int h)
    {
        lock (Sync)
        {
            var tmp = new byte[w * h * 4];
            for (var row = 0; row < h; row++) { Buffer.BlockCopy(Pixels, ((sy + row) * Width + sx) * 4, tmp, row * w * 4, w * 4); }
            for (var row = 0; row < h; row++) { Buffer.BlockCopy(tmp, row * w * 4, Pixels, ((y + row) * Width + x) * 4, w * 4); }
        }
    }

    // ZRLE: one zlib stream for the whole connection; 64×64 tiles of 3-byte pixels (B G R)
    private void Zrle(byte[] data, int rx, int ry, int rw, int rh)
    {
        _zfeed.Add(data);
        _zlib ??= new ZLibStream(_zfeed, CompressionMode.Decompress);
        byte[] Take(int n) { var b = new byte[n]; _zlib.ReadExactly(b); return b; }
        int Byte() => Take(1)[0];
        uint Pixel() { var p = Take(3); return (uint)(p[0] | p[1] << 8 | p[2] << 16) | 0xFF000000; }

        lock (Sync)
        {
            for (var ty = ry; ty < ry + rh; ty += 64)
            {
                for (var tx = rx; tx < rx + rw; tx += 64)
                {
                    int tw = Math.Min(64, rx + rw - tx), th = Math.Min(64, ry + rh - ty);
                    var tile = new uint[tw * th];
                    var sub = Byte();
                    if (sub == 0)
                    {
                        for (var i = 0; i < tile.Length; i++) { tile[i] = Pixel(); }
                    }
                    else if (sub == 1)
                    {
                        Array.Fill(tile, Pixel());
                    }
                    else if (sub <= 16)
                    {
                        var palette = new uint[sub];
                        for (var i = 0; i < sub; i++) { palette[i] = Pixel(); }
                        var bits = sub == 2 ? 1 : sub <= 4 ? 2 : 4;
                        var rowBytes = (tw * bits + 7) / 8;
                        for (var row = 0; row < th; row++)
                        {
                            var line = Take(rowBytes);
                            for (var col = 0; col < tw; col++)
                            {
                                var bitPos = col * bits;
                                var idx = (line[bitPos / 8] >> (8 - bits - bitPos % 8)) & ((1 << bits) - 1);
                                tile[row * tw + col] = palette[idx];
                            }
                        }
                    }
                    else if (sub == 128)
                    {
                        for (var i = 0; i < tile.Length;)
                        {
                            var p = Pixel();
                            var run = RunLength(Byte);
                            for (var k = 0; k < run && i < tile.Length; k++) { tile[i++] = p; }
                        }
                    }
                    else if (sub >= 130)
                    {
                        var palette = new uint[sub - 128];
                        for (var i = 0; i < palette.Length; i++) { palette[i] = Pixel(); }
                        for (var i = 0; i < tile.Length;)
                        {
                            var idx = Byte();
                            var run = (idx & 128) != 0 ? RunLength(Byte) : 1;
                            var p = palette[idx & 127];
                            for (var k = 0; k < run && i < tile.Length; k++) { tile[i++] = p; }
                        }
                    }
                    else
                    {
                        throw new IOException($"ZRLE subencoding {sub}");
                    }
                    for (var row = 0; row < th; row++)
                    {
                        Buffer.BlockCopy(tile, row * tw * 4, Pixels, ((ty + row) * Width + tx) * 4, tw * 4);
                    }
                }
            }
        }
    }

    private static int RunLength(Func<int> next)
    {
        var len = 1;
        int b;
        do { b = next(); len += b; } while (b == 255);
        return len;
    }

    // --- input --------------------------------------------------------------------------------------
    public Task RequestAsync(bool incremental)
    {
        var m = new byte[10];
        m[0] = 3;
        m[1] = (byte)(incremental ? 1 : 0);
        BinaryPrimitives.WriteUInt16BigEndian(m.AsSpan(6), (ushort)Width);
        BinaryPrimitives.WriteUInt16BigEndian(m.AsSpan(8), (ushort)Height);
        return SendAsync(m);
    }

    /// <summary>buttons: 1 left, 2 middle, 4 right, 8 / 16 wheel up / down.</summary>
    public Task PointerAsync(int x, int y, int buttons)
    {
        var m = new byte[6];
        m[0] = 5;
        m[1] = (byte)buttons;
        BinaryPrimitives.WriteUInt16BigEndian(m.AsSpan(2), (ushort)Math.Clamp(x, 0, Math.Max(0, Width - 1)));
        BinaryPrimitives.WriteUInt16BigEndian(m.AsSpan(4), (ushort)Math.Clamp(y, 0, Math.Max(0, Height - 1)));
        return SendAsync(m);
    }

    public Task KeyAsync(uint keysym, bool down)
    {
        var m = new byte[8];
        m[0] = 4;
        m[1] = (byte)(down ? 1 : 0);
        BinaryPrimitives.WriteUInt32BigEndian(m.AsSpan(4), keysym);
        return SendAsync(m);
    }

    public async Task TypeAsync(string text)
    {
        foreach (var r in text.EnumerateRunes())
        {
            var sym = r.Value == '\n' ? 0xff0dU : r.Value < 0x100 ? (uint)r.Value : 0x01000000U | (uint)r.Value;
            await KeyAsync(sym, true);
            await KeyAsync(sym, false);
        }
    }

    public async Task CtrlAltDelAsync()
    {
        foreach (var k in new uint[] { 0xffe3, 0xffe9, 0xffff }) { await KeyAsync(k, true); }
        foreach (var k in new uint[] { 0xffff, 0xffe9, 0xffe3 }) { await KeyAsync(k, false); }
    }

    private async Task SendAsync(byte[] m)
    {
        await _send.WaitAsync();
        try { await _s.WriteAsync(m, _cts.Token); }
        catch { /* the loop reports the closed connection */ }
        finally { _send.Release(); }
    }

    // --- reading ------------------------------------------------------------------------------------
    private async Task<byte[]> ReadAsync(int n, CancellationToken ct)
    {
        var b = new byte[n];
        await _s.ReadExactlyAsync(b, ct);
        return b;
    }

    private async Task<uint> U32Async(CancellationToken ct) => BinaryPrimitives.ReadUInt32BigEndian(await ReadAsync(4, ct));

    private async Task<string> ReasonAsync(CancellationToken ct) => Encoding.UTF8.GetString(await ReadAsync((int)await U32Async(ct), ct));

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        await _s.DisposeAsync();
        _zlib?.Dispose();
    }

    /// <summary>The compressed bytes so far, read by the one zlib stream ZRLE keeps for the connection.</summary>
    private sealed class ZlibFeed : Stream
    {
        private readonly Queue<byte[]> _chunks = new();
        private byte[]? _cur;
        private int _pos;
        public void Add(byte[] data) => _chunks.Enqueue(data);
        public override int Read(byte[] buffer, int offset, int count)
        {
            while (_cur is null || _pos >= _cur.Length)
            {
                if (!_chunks.TryDequeue(out _cur)) { _cur = null; return 0; }
                _pos = 0;
            }
            var n = Math.Min(count, _cur.Length - _pos);
            Buffer.BlockCopy(_cur, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>The engine's console websocket (VNC), with the session cookie and the client's certificate choice.</summary>
internal static class EngineSocket
{
    public static async Task<ClientWebSocket> ConnectAsync(DeskApi api, string wsPath, CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", api.BaseUri.GetLeftPart(UriPartial.Authority));
        var cookie = api.CookieHeader;
        if (cookie.Length > 0) { ws.Options.SetRequestHeader("Cookie", cookie); }
        if (api.IgnoreCertificateErrors) { ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true; }
        var uri = new UriBuilder(new Uri(api.BaseUri, wsPath.TrimStart('/'))) { Scheme = api.BaseUri.Scheme == "http" ? "ws" : "wss" }.Uri;
        await ws.ConnectAsync(uri, ct);
        return ws;
    }
}

/// <summary>A websocket as a byte stream (binary frames; a text frame is the engine refusing).</summary>
internal sealed class WebSocketByteStream(ClientWebSocket ws) : Stream
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (ws.State != WebSocketState.Open) { return 0; }
        var r = await ws.ReceiveAsync(buffer, ct);
        if (r.MessageType == WebSocketMessageType.Close) { return 0; }
        if (r.MessageType == WebSocketMessageType.Text) { throw new IOException(Encoding.UTF8.GetString(buffer.Span[..r.Count])); }
        return r.Count;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        => ws.SendAsync(buffer, WebSocketMessageType.Binary, true, ct);

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _ = ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch { }
            ws.Dispose();
        }
        base.Dispose(disposing);
    }
}
