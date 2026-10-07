using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.SessionState;
namespace Stockroom
{
    [SessionState(SessionStateBehavior.Disabled)]
    public sealed class StockController : Controller
    {
        private readonly IStockEditor _stock;
        public StockController(IStockEditor stock) { _stock = stock; }
        [HttpGet] public async Task<ActionResult> Index() { await Task.Yield(); ViewBag.Agent = Request.UserAgent; return View(_stock.Read()); }
        [HttpGet, Authorize(Roles = "Editor")]
        public async Task<ActionResult> Edit() { await Task.Yield(); return View(_stock.Read()); }
        [HttpPost, Authorize(Roles = "Editor"), ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(StockEdit model) {
            await Task.Yield();
            if (!ModelState.IsValid) return View(model);
            if (!_stock.TryStage(model.Quantity, model.Version)) { Response.StatusCode = 409; ModelState.AddModelError("", "Stock changed. Return to the list and reload before saving."); return View(model); }
            TempData["Notice"] = "Quantity saved.";
            return RedirectToAction("Index");
        }
        [HttpPost, Authorize(Roles = "Editor"), ValidateAntiForgeryToken]
        public async Task<ActionResult> SaveJson(StockEdit model) {
            await Task.Yield();
            if (!ModelState.IsValid) { Response.StatusCode = 400; return Json(new { error = "Quantity must be between 0 and 1000." }); }
            if (!_stock.TryStage(model.Quantity, model.Version)) { Response.StatusCode = 409; return Json(new { error = "Stock changed. Reload before saving." }); }
            return Json(new { quantity = model.Quantity, version = model.Version + 1 });
        }
    }
}
