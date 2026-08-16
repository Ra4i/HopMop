using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using HopMop.Data;
using HopMop.Models;

namespace HopMop.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AdminController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public IActionResult Index()
        {
            var items = _db.PhotoPairs.OrderByDescending(p => p.CreatedAt).ToList();
            // The dashboard badge counts what still needs attention, not the archive.
            ViewBag.ActiveInquiryCount = _db.Inquiries.Count(i => !i.IsResolved);
            return View(items);
        }

        // Active inquiries — the ones not yet marked as done.
        [Authorize(Policy = "AdminOnly")]
        public IActionResult Inquiries()
        {
            var items = _db.Inquiries
                .Where(i => !i.IsResolved)
                .OrderByDescending(i => i.CreatedAt)
                .ToList();
            return View(items);
        }

        // The archive: resolved inquiries are kept, only moved out of the way.
        [Authorize(Policy = "AdminOnly")]
        public IActionResult ResolvedInquiries()
        {
            var items = _db.Inquiries
                .Where(i => i.IsResolved)
                .OrderByDescending(i => i.ResolvedAt)
                .ToList();
            return View(items);
        }

        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        [ValidateAntiForgeryToken]
        public IActionResult ResolveInquiry(int id)
        {
            var item = _db.Inquiries.Find(id);
            if (item != null && !item.IsResolved)
            {
                item.IsResolved = true;
                item.ResolvedAt = DateTime.UtcNow;
                _db.SaveChanges();
            }
            return RedirectToAction("Inquiries");
        }

        [HttpGet]
        public IActionResult Upload() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Upload(string? title, string? description, IFormFile? beforeImage, IFormFile? afterImage)
        {
            // Drop the framework's automatic (English) messages — every problem
            // on this page is reported below in plain Bulgarian instead.
            ModelState.Clear();

            if (string.IsNullOrWhiteSpace(title))
            {
                ModelState.AddModelError("", "Напишете заглавие — то се вижда в галерията.");
            }
            if (beforeImage == null || beforeImage.Length == 0)
            {
                ModelState.AddModelError("", "Изберете снимка ПРЕДИ почистването.");
            }
            if (afterImage == null || afterImage.Length == 0)
            {
                ModelState.AddModelError("", "Изберете снимка СЛЕД почистването.");
            }
            if (!ModelState.IsValid) return View();

            // Validate both files before writing anything to disk, so a bad
            // second file cannot leave a stray first file behind.
            var beforeExt = ValidateImage(beforeImage!, "ПРЕДИ");
            var afterExt = ValidateImage(afterImage!, "СЛЕД");
            if (!ModelState.IsValid) return View();

            string uploads = Path.Combine(_env.WebRootPath, "uploads");
            Directory.CreateDirectory(uploads);

            var beforeName = Guid.NewGuid().ToString() + beforeExt;
            var afterName = Guid.NewGuid().ToString() + afterExt;

            using (var fs = System.IO.File.Create(Path.Combine(uploads, beforeName)))
            {
                beforeImage.CopyTo(fs);
            }
            using (var fs = System.IO.File.Create(Path.Combine(uploads, afterName)))
            {
                afterImage.CopyTo(fs);
            }

            var pair = new PhotoPair
            {
                Title = title!,
                Description = description ?? string.Empty,
                BeforeImagePath = "/uploads/" + beforeName,
                AfterImagePath = "/uploads/" + afterName,
                CreatedAt = DateTime.UtcNow
            };
            _db.PhotoPairs.Add(pair);
            _db.SaveChanges();

            return RedirectToAction("Index");
        }

        // Checks one uploaded image and returns the extension to save it under.
        // Adds a plain-language message to ModelState when something is wrong.
        private string ValidateImage(IFormFile file, string label)
        {
            const long maxBytes = 10 * 1024 * 1024;
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
            {
                ModelState.AddModelError("", $"Файлът за „{label}“ не е снимка. Изберете файл във формат JPG, PNG или WEBP.");
                return ext;
            }

            if (file.Length > maxBytes)
            {
                var mb = (file.Length / 1024d / 1024d).ToString("0.0");
                ModelState.AddModelError("", $"Снимката „{label}“ е твърде голяма ({mb} MB). Максимумът е 10 MB.");
            }

            return ext == ".jpeg" ? ".jpg" : ext;
        }

        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var item = _db.PhotoPairs.Find(id);
            if (item != null)
            {
                // delete files
                try
                {
                    var before = Path.Combine(_env.WebRootPath, item.BeforeImagePath.TrimStart('/','\\').Replace('/','\\'));
                    var after = Path.Combine(_env.WebRootPath, item.AfterImagePath.TrimStart('/','\\').Replace('/','\\'));
                    if (System.IO.File.Exists(before)) System.IO.File.Delete(before);
                    if (System.IO.File.Exists(after)) System.IO.File.Delete(after);
                }
                catch { }

                _db.PhotoPairs.Remove(item);
                _db.SaveChanges();
            }
            return RedirectToAction("Index");
        }
    }
}
