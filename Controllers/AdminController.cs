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
            return View(items);
        }

        [HttpGet]
        public IActionResult Upload() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Upload(string title, string description, IFormFile beforeImage, IFormFile afterImage)
        {
            if (beforeImage == null || afterImage == null || string.IsNullOrWhiteSpace(title))
            {
                ModelState.AddModelError("", "Заглавие и двете изображения са задължителни.");
                return View();
            }

            string uploads = Path.Combine(_env.WebRootPath, "uploads");
            var beforeName = Guid.NewGuid().ToString() + Path.GetExtension(beforeImage.FileName);
            var afterName = Guid.NewGuid().ToString() + Path.GetExtension(afterImage.FileName);

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
                Title = title,
                Description = description,
                BeforeImagePath = "/uploads/" + beforeName,
                AfterImagePath = "/uploads/" + afterName,
                CreatedAt = DateTime.UtcNow
            };
            _db.PhotoPairs.Add(pair);
            _db.SaveChanges();

            return RedirectToAction("Index");
        }

        [HttpPost]
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
