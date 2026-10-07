using System.ComponentModel.DataAnnotations;
namespace Stockroom
{
    public sealed class StockEdit
    {
        [Range(0, 1000, ErrorMessage = "Quantity must be between 0 and 1000.")]
        public int Quantity { get; set; }
        [Range(1, int.MaxValue)] public int Version { get; set; }
    }
    public interface IStockEditor { StockEdit Read(); bool TryStage(int quantity, int version); }
}
