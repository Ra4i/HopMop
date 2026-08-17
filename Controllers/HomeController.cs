using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using HopMop.Data;
using HopMop.Models;

namespace HopMop.Controllers
{
    [AllowAnonymous]
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<HomeController> _log;

        public HomeController(AppDbContext db, ILogger<HomeController> log)
        {
            _db = db;
            _log = log;
        }

        public IActionResult Index()
        {
            ViewBag.Title = "HopMop Ltd. - Cleanliness you can count on!";
            return View();
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Services()
        {
            return View();
        }

        public IActionResult Gallery()
        {
            var items = _db.PhotoPairs.OrderByDescending(p => p.CreatedAt).ToList();
            return View(items);
        }

        public IActionResult Contact()
        {
            return View(new Inquiry());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimitPolicies.ContactForm)]
        public IActionResult ContactSubmit([FromForm] Inquiry model)
        {
            // Returning the model keeps what the visitor typed on screen instead
            // of handing back an empty form after a validation error.
            if (!ModelState.IsValid) return View("Contact", model);

            // Set server-side rather than trusted from the form — the field is
            // [BindNever], so a posted CreatedAt is ignored.
            model.CreatedAt = DateTime.UtcNow;

            // An omitted phone binds to null, but databases created before Phone
            // became optional declare the column NOT NULL. Store "" so the app
            // works against both the old and the new schema.
            model.Phone ??= string.Empty;

            _db.Inquiries.Add(model);
            _db.SaveChanges();

            TempData["Success"] = "Вашето запитване е изпратено. Ще се свържем с вас скоро!";
            return RedirectToAction("Contact");
        }

        // Target of both UseExceptionHandler and UseStatusCodePagesWithReExecute.
        // Never shows the exception itself — that detail goes to the log only.
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult Error(int? code)
        {
            var feature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
            if (feature is not null)
            {
                _log.LogError(feature.Error, "Unhandled exception at {Path}", feature.Path);
            }

            var status = code ?? StatusCodes.Status500InternalServerError;
            Response.StatusCode = status;

            ViewBag.StatusCode = status;
            return View();
        }
    }
}
