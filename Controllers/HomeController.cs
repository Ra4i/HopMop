using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using HopMop.Data;
using HopMop.Models;

namespace HopMop.Controllers
{
    [AllowAnonymous]
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;

        public HomeController(AppDbContext db)
        {
            _db = db;
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

        public IActionResult Prices()
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
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ContactSubmit([FromForm] Inquiry model)
        {
            if (!ModelState.IsValid) return View("Contact");

            _db.Inquiries.Add(model);
            _db.SaveChanges();

            TempData["Success"] = "Вашето запитване е изпратено. Ще се свържем с вас скоро!";
            return RedirectToAction("Contact");
        }
    }
}
