using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;

namespace AspNetWebStack.Native;

// Stable legacy file/stream identity over native parsed files. A fresh native
// slice per read lets sequential interleaved files retain independent positions.
internal sealed class NativePostedFiles : HttpFileCollectionBase
{
    private readonly Action _check;
    private int _reading;
    internal NativePostedFiles(IFormFileCollection files, Action check)
    {
        _check = check;
        foreach (var file in files) BaseAdd(file.Name, new PostedFile(file, this));
        IsReadOnly = true;
    }
    public override int Count { get { _check(); return base.Count; } }
    public override string[] AllKeys { get { _check(); return BaseGetAllKeys(); } }
    public override KeysCollection Keys { get { _check(); return base.Keys; } }
    public override IEnumerator GetEnumerator()
    {
        _check();
        for (int index = 0; index < Count; index++) { _check(); yield return BaseGetKey(index); }
    }
    public override string GetKey(int index) { _check(); return BaseGetKey(index); }
    public override HttpPostedFileBase Get(int index) { _check(); return (HttpPostedFileBase)BaseGet(index); }
    public override HttpPostedFileBase Get(string name) { _check(); return (HttpPostedFileBase)BaseGet(name); }
    public override HttpPostedFileBase this[int index] => Get(index);
    public override HttpPostedFileBase this[string name] => Get(name);
    public override IList<HttpPostedFileBase> GetMultiple(string name)
    {
        _check();
        var result = new List<HttpPostedFileBase>();
        for (int index = 0; index < Count; index++)
            if (String.Equals(name, BaseGetKey(index), StringComparison.OrdinalIgnoreCase)) result.Add(Get(index));
        return result.AsReadOnly();
    }
    private void Enter()
    {
        _check();
        if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
            throw new InvalidOperationException("Uploaded files require sequential request-owned access.");
    }
    private void Exit() => Volatile.Write(ref _reading, 0);

    private sealed class PostedFile : HttpPostedFileBase
    {
        private readonly IFormFile _file;
        private readonly NativePostedFiles _owner;
        private readonly PostedStream _stream;
        internal PostedFile(IFormFile file, NativePostedFiles owner) { _file = file; _owner = owner; _stream = new PostedStream(file, owner); }
        public override string FileName { get { _owner._check(); return _file.FileName; } }
        public override string ContentType { get { _owner._check(); return _file.ContentType; } }
        public override int ContentLength { get { _owner._check(); return checked((int)_file.Length); } }
        public override Stream InputStream { get { _owner._check(); return _stream; } }
        public override void SaveAs(string filename)
        {
            _owner._check();
            if (!Path.IsPathFullyQualified(filename)) throw new HttpException("SaveAs requires an absolute destination path.");
            _owner.Enter();
            try
            {
                // Reference Source SaveAs writes the complete file and preserves
                // InputStream.Position. The application owns this destination.
                using var source = _file.OpenReadStream();
                using var destination = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None);
                byte[] block = new byte[8192]; int count;
                while ((count = source.Read(block, 0, block.Length)) != 0) { _owner._check(); destination.Write(block, 0, count); }
                _owner._check();
            }
            finally { _owner.Exit(); }
        }
    }

    private sealed class PostedStream : Stream
    {
        private readonly IFormFile _file;
        private readonly NativePostedFiles _owner;
        private long _position;
        private bool _disposed;
        internal PostedStream(IFormFile file, NativePostedFiles owner) { _file = file; _owner = owner; }
        private void Check() { _owner._check(); ObjectDisposedException.ThrowIf(_disposed, this); }
        public override bool CanRead { get { Check(); return true; } }
        public override bool CanSeek { get { Check(); return true; } }
        public override bool CanWrite => false;
        public override long Length { get { Check(); return _file.Length; } }
        public override long Position
        {
            get { Check(); return _position; }
            set { Check(); if (value < 0 || value > _file.Length) throw new ArgumentOutOfRangeException(nameof(value)); _position = value; }
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            Check();
            Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(_position + offset), SeekOrigin.End => checked(_file.Length + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            return _position;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            Check(); _owner.Enter();
            try
            {
                using var source = _file.OpenReadStream(); source.Position = _position;
                int count = source.Read(buffer); _position += count; Check(); return count;
            }
            finally { _owner.Exit(); }
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Check(); cancellationToken.ThrowIfCancellationRequested(); _owner.Enter();
            try
            {
                using var source = _file.OpenReadStream(); source.Position = _position;
                int count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                _position += count; Check(); return count;
            }
            finally { _owner.Exit(); }
        }
        public override void Flush() => Check();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _disposed = true; base.Dispose(disposing); }
    }
}
