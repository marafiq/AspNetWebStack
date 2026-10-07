using AspNetWebStack.Native;
using Microsoft.Data.Sqlite;
namespace Stockroom.Hosting;
public interface IStockStore
{
    StockEdit Read();
    void Commit(int quantity, int version, Action validate);
}
public sealed class MemoryStockStore : IStockStore
{
    private readonly object _gate = new(); private int _quantity = 12, _version = 1;
    public StockEdit Read() { lock (_gate) return new StockEdit { Quantity = _quantity, Version = _version }; }
    public void Commit(int quantity, int version, Action validate) { lock (_gate) {
        if (quantity < 0 || quantity > 1000 || version != _version || version == Int32.MaxValue) throw new System.Web.HttpException(409, "Stock changed; reload and retry.");
        validate(); _quantity = quantity; _version++;
    } }
}
public sealed class SqliteStockStore : IStockStore
{
    private readonly string _connectionString;
    public SqliteStockStore(string databasePath)
    {
        databasePath = DeploymentSettings.AbsolutePath(databasePath, "DatabasePath");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5 }.ToString();
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Stock (Id INTEGER PRIMARY KEY CHECK(Id=1), Quantity INTEGER NOT NULL CHECK(Quantity BETWEEN 0 AND 1000), Version INTEGER NOT NULL CHECK(Version BETWEEN 1 AND 2147483647)); INSERT OR IGNORE INTO Stock VALUES (1,12,1);";
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var c = new SqliteConnection(_connectionString); try { c.Open(); return c; } catch { c.Dispose(); throw; } }
    public StockEdit Read() {
        using var c = Open(); using var command = c.CreateCommand(); command.CommandText = "SELECT Quantity, Version FROM Stock WHERE Id=1";
        using var reader = command.ExecuteReader(); if (!reader.Read()) throw new InvalidOperationException("Stock record is missing.");
        return new StockEdit { Quantity = reader.GetInt32(0), Version = reader.GetInt32(1) };
    }
    public void Commit(int quantity, int version, Action validate) {
        if (quantity < 0 || quantity > 1000 || version < 1 || version == Int32.MaxValue) throw new System.Web.HttpException(409, "Invalid stock version or quantity.");
        using var c = Open(); using var transaction = c.BeginTransaction(deferred: false); using var command = c.CreateCommand();
        command.Transaction = transaction; command.CommandText = "UPDATE Stock SET Quantity=$quantity, Version=Version+1 WHERE Id=1 AND Version=$version";
        command.Parameters.AddWithValue("$quantity", quantity); command.Parameters.AddWithValue("$version", version);
        if (command.ExecuteNonQuery() != 1) throw new System.Web.HttpException(409, "Stock changed; reload and retry.");
        // The write is uncommitted and locked. Validate native request ownership immediately before durable commit.
        validate(); transaction.Commit();
    }
}
internal sealed class StockEditor : IStockEditor, IDisposable
{
    private readonly IStockStore _store; private int _quantity, _version; private bool _pending, _disposed;
    public StockEditor(IStockStore store) { _store = store; }
    public StockEdit Read() { ObjectDisposedException.ThrowIf(_disposed, this); return _store.Read(); }
    public bool TryStage(int quantity, int version) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pending || quantity < 0 || quantity > 1000) throw new InvalidOperationException("Invalid staged update.");
        if (version != _store.Read().Version || version == Int32.MaxValue) return false;
        _quantity = quantity; _version = version; _pending = true; return true;
    }
    internal bool HasChanges => _pending;
    internal void Commit(Action validate) {
        ObjectDisposedException.ThrowIf(_disposed, this); if (!_pending) throw new InvalidOperationException("No staged update.");
        _store.Commit(_quantity, _version, validate); _pending = false;
    }
    public void Dispose() { _pending = false; _disposed = true; }
}
internal sealed class StockTransaction : INativeMvcTransaction
{
    private readonly StockEditor _editor;
    public StockTransaction(IStockEditor editor) { _editor = (StockEditor)editor; }
    public bool HasChanges => _editor.HasChanges;
    public void Commit(Action validateRequest) => _editor.Commit(validateRequest);
}
