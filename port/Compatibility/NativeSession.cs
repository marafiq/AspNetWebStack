using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Web.SessionState;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AspNetWebStack.Native;

// Register the data schema at application startup; stored bytes never select CLR types.
// Native session middleware owns the cookie, cache, expiration and concurrency model.
public sealed class NativeSessionOptions
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase);
    public string KeyPrefix { get; }
    public int MaximumBytes { get; }
    public NativeSessionOptions(string keyPrefix, int maximumBytes = 65536)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyPrefix);
        if (keyPrefix.Length > 128 || keyPrefix.Any(char.IsControl)) throw new ArgumentException("Use a bounded session namespace.", nameof(keyPrefix));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        KeyPrefix = keyPrefix + ":"; MaximumBytes = maximumBytes;
    }
    public NativeSessionOptions Register<T>(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (String.Equals(name, "$initialized", StringComparison.OrdinalIgnoreCase) || name.Length > 128 || name.Any(char.IsControl)) throw new ArgumentException("Use a bounded session key.", nameof(name));
        var type = typeof(T);
        if (type == typeof(object) || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
            throw new ArgumentException("Register a concrete session value type.");
        if (_types.Count == 32) throw new InvalidOperationException("Register at most 32 session keys.");
        _types.Add(name, type); return this;
    }
    internal NativeSessionOptions Snapshot()
    {
        var copy = new NativeSessionOptions(KeyPrefix[..^1], MaximumBytes);
        foreach (var pair in _types) copy._types.Add(pair.Key, pair.Value);
        return copy;
    }
    internal IEnumerable<KeyValuePair<string, Type>> Types => _types;
}

internal sealed class NativeSessionState : HttpSessionStateBase
{
    private readonly Microsoft.AspNetCore.Http.HttpContext _core;
    private readonly ISessionFeature _feature;
    private readonly ISession _session;
    private readonly NativeRequestLifetime _lifetime;
    private readonly NativeSessionOptions _options;
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _original = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _readOnly, _isNew;
    private readonly int _timeout;
    private readonly string _marker;
    private Dictionary<string, byte[]> _prepared;
    private bool _sealed;
    private int _version;

    private NativeSessionState(Microsoft.AspNetCore.Http.HttpContext core, NativeRequestLifetime lifetime,
        NativeSessionOptions options, SessionStateBehavior behavior, int timeout)
    {
        _core = core; _lifetime = lifetime; _options = options; _timeout = timeout;
        _feature = core.Features.Get<ISessionFeature>() ?? throw new InvalidOperationException("Configure UseSession before MVC endpoints.");
        _session = _feature.Session ?? throw new InvalidOperationException("Native session is unavailable.");
        _readOnly = behavior == SessionStateBehavior.ReadOnly;
        _marker = options.KeyPrefix + "$initialized";
        foreach (var pair in options.Types)
        {
            if (pair.Key == "$initialized") throw new ArgumentException("The session marker key is reserved.");
            _names.Add(pair.Key, pair.Key); _types.Add(pair.Key, pair.Value);
        }
        _isNew = !_session.TryGetValue(_marker, out _);
        long size = 0;
        foreach (string key in _session.Keys.Where(key => key.StartsWith(options.KeyPrefix, StringComparison.Ordinal)))
        {
            if (key == _marker) continue;
            string name = key.Substring(options.KeyPrefix.Length);
            if (!_names.TryGetValue(name, out var canonical) || name != canonical)
                throw new InvalidOperationException("Stored session keys do not match the registered schema.");
            if (_session.TryGetValue(key, out byte[] bytes))
            {
                size += bytes.Length;
                if (size > options.MaximumBytes) throw new HttpException(413, "Session size limit exceeded.");
                _original.Add(name, bytes.ToArray());
                _values.Add(name, JsonSerializer.Deserialize(bytes, _types[name]) ?? throw new InvalidOperationException("Stored session values must be non-null; remove absent keys."));
            }
        }
    }
    internal static async Task<NativeSessionState> LoadAsync(Microsoft.AspNetCore.Http.HttpContext core,
        NativeRequestLifetime lifetime, NativeSessionOptions options, SessionStateBehavior behavior, int timeout)
    {
        if (behavior != SessionStateBehavior.Required && behavior != SessionStateBehavior.ReadOnly)
            throw new PlatformNotSupportedException("Use Required or ReadOnly session behavior.");
        var feature = core.Features.Get<ISessionFeature>() ?? throw new InvalidOperationException("Configure native session middleware before MVC.");
        var session = feature.Session ?? throw new InvalidOperationException("Native session is unavailable.");
        await session.LoadAsync(lifetime.RequestAborted);
        lifetime.CheckPublication();
        if (!ReferenceEquals(feature, core.Features.Get<ISessionFeature>()) || !ReferenceEquals(session, feature.Session))
            throw new InvalidOperationException("The native session changed while loading.");
        if (!session.IsAvailable) throw new InvalidOperationException("The native session store is unavailable.");
        return new NativeSessionState(core, lifetime, options, behavior, timeout);
    }
    internal void ValidateOwner()
    {
        if (!ReferenceEquals(_feature, _core.Features.Get<ISessionFeature>()) || !ReferenceEquals(_session, _feature.Session))
            throw new InvalidOperationException("The owned native session changed.");
    }
    private void Check() { _lifetime.CheckPublication(); ValidateOwner(); }
    private string Name(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _names.TryGetValue(name, out var canonical) ? canonical : throw new InvalidOperationException("Register the session key at startup: " + name);
    }
    private void Write()
    {
        Check();
        if (_readOnly) throw new InvalidOperationException("This controller has read-only session state.");
        if (_sealed) throw new InvalidOperationException("Session state is sealed for commit.");
        _version++;
    }
    public override object this[string name]
    {
        get { Check(); return _values.TryGetValue(Name(name), out var value) ? value : null; }
        set
        {
            Write(); name = Name(name);
            if (value == null) _values.Remove(name);
            else
            {
                if (value.GetType() != _types[name]) throw new ArgumentException("The session value must have its registered exact type.", nameof(value));
                _values[name] = value;
            }
        }
    }
    public override string SessionID { get { Check(); return _session.Id; } }
    public override int Count { get { Check(); return _values.Count; } }
    public override bool IsReadOnly { get { Check(); return _readOnly; } }
    public override bool IsNewSession { get { Check(); return _isNew; } }
    public override int Timeout { get { Check(); return _timeout; } set { Check(); throw new PlatformNotSupportedException("Configure the native SessionOptions.IdleTimeout at startup."); } }
    public override void Add(string name, object value) => this[name] = value;
    public override void Remove(string name) { Write(); _values.Remove(Name(name)); }
    public override void RemoveAll() => Clear();
    public override void Clear() { Write(); _values.Clear(); }
    public override void Abandon() { Check(); throw new PlatformNotSupportedException("Session abandonment and identifier rotation require a separate native host contract."); }

    // No native writes until MVC has completed and validated its response. Mutable
    // objects are serialized here, so in-place updates work across awaited actions.
    internal bool Prepare()
    {
        Check(); _sealed = true;
        if (_readOnly) return false;
        int version = _version; long size = 0;
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _values.ToArray())
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(pair.Value, _types[pair.Key]);
            size += bytes.Length;
            if (size > _options.MaximumBytes) throw new HttpException(413, "Session size limit exceeded.");
            result.Add(pair.Key, bytes);
        }
        Check();
        if (_version != version) throw new InvalidOperationException("Session changed during serialization.");
        _prepared = result;
        return result.Count != _original.Count || result.Any(pair => !_original.TryGetValue(pair.Key, out var original) || !pair.Value.SequenceEqual(original));
    }
    internal async Task CommitAsync()
    {
        Check();
        if (_readOnly || _prepared == null) throw new InvalidOperationException("Prepare writable session before commit.");
        foreach (var name in _original.Keys.Where(name => !_prepared.ContainsKey(name))) _session.Remove(_options.KeyPrefix + name);
        foreach (var pair in _prepared) _session.Set(_options.KeyPrefix + pair.Key, pair.Value);
        _session.Set(_marker, new byte[] { 1 });
        await _session.CommitAsync(_lifetime.RequestAborted);
        Check();
    }
}

// Shared admission/ownership for input preparation and the awaited MVC lifetime.
internal sealed class NativeSessionOwnership
{
    private readonly Microsoft.AspNetCore.Http.HttpContext _core;
    private readonly ISessionFeature _feature;
    private readonly ISession _session;
    internal NativeSessionOwnership(Microsoft.AspNetCore.Http.HttpContext core, bool enabled)
    {
        _core = core; _feature = core.Features.Get<ISessionFeature>(); _session = _feature?.Session;
        if (!enabled && _feature != null) throw new PlatformNotSupportedException("This public request boundary requires disabled session.");
    }
    internal void Check()
    {
        if (!ReferenceEquals(_feature, _core.Features.Get<ISessionFeature>()) || !ReferenceEquals(_session, _feature?.Session))
            throw new InvalidOperationException("The native session feature changed during owned execution.");
    }
}
