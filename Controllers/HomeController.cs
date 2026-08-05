using Microsoft.AspNetCore.Mvc;
using HopMop.Data;
using HopMop.Models;
using System.Net.Mail;

namespace HopMop.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _cfg;

        public HomeController(AppDbContext db, IConfiguration cfg)
        {
            _db = db;
            _cfg = cfg;
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

            // Try send email if configured
            try
            {
                var smtp = _cfg.GetSection("Smtp");
                var host = smtp["Host"];
                var from = smtp["From"];
                var to = _cfg["Admin:DefaultEmail"];
                if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                {
                    var port = 25;
                    if (!int.TryParse(smtp["Port"], out port))
                    {
                        port = 25;
                    }

                    using var client = new SmtpClient(host, port)
                    {
                        EnableSsl = true,
                        Credentials = new System.Net.NetworkCredential(smtp["Username"], smtp["Password"])
                    };
                    var msg = new MailMessage(from, to)
                    {
                        Subject = "New inquiry from website",
                        Body = $"Name: {model.Name}\nEmail: {model.Email}\nPhone: {model.Phone}\nMessage:\n{model.Message}"
                    };
                    client.Send(msg);
                }
            }
            catch { /* swallow; still saved to DB */ }

            TempData["Success"] = "Вашето запитване е изпратено. Ще се свържем с вас скоро!";
            return RedirectToAction("Contact");
        }
    }
}
