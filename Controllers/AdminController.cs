using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using HopMop.Data;
using HopMop.Models;

namespace HopMop.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private const long MaxImageBytes = 10 * 1024 * 1024;

        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AdminController> _log;

        public AdminController(AppDbContext db, IWebHostEnvironment env, ILogger<AdminController> log)
        {
            _db = db;
            _env = env;
            _log = log;
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
        [RequestSizeLimit(25 * 1024 * 1024)]
        public IActionResult Upload(string? title, string? description, IFormFile? beforeImage, IFormFile? afterImage)
        {
            // Drop the framework's automatic (English) messages — every problem
            // on this page is reported below in plain Bulgarian instead.
            ModelState.Clear();

            if (string.IsNullOrWhiteSpace(title))
            {
                ModelState.AddModelError("", "Напишете заглавие — то се вижда в галерията.");
            }
            else if (title.Length > 150)
            {
                ModelState.AddModelError("", "Заглавието може да е до 150 символа.");
            }
            if (!string.IsNullOrEmpty(description) && description.Length > 2000)
            {
                ModelState.AddModelError("", "Описанието може да е до 2000 символа.");
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

            // The stored name is a fresh GUID, never anything derived from the
            // uploaded file name — that keeps user input out of the file path
            // entirely, so there is nothing to traverse or overwrite with.
            var beforeName = Guid.NewGuid().ToString("N") + beforeExt;
            var afterName = Guid.NewGuid().ToString("N") + afterExt;

            using (var fs = System.IO.File.Create(Path.Combine(uploads, beforeName)))
            {
                beforeImage!.CopyTo(fs);
            }
            using (var fs = System.IO.File.Create(Path.Combine(uploads, afterName)))
            {
                afterImage!.CopyTo(fs);
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
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
            {
                ModelState.AddModelError("", $"Файлът за „{label}“ не е снимка. Изберете файл във формат JPG, PNG или WEBP.");
                return ".jpg";
            }

            if (file.Length > MaxImageBytes)
            {
                var mb = (file.Length / 1024d / 1024d).ToString("0.0");
                ModelState.AddModelError("", $"Снимката „{label}“ е твърде голяма ({mb} MB). Максимумът е 10 MB.");
                return ".jpg";
            }

            // An extension is just part of the file name — anyone can rename a
            // script to .jpg. These files are served back from wwwroot, so the
            // bytes themselves have to confirm the format before it is saved.
            if (!HasImageSignature(file, ext))
            {
                ModelState.AddModelError("", $"Файлът за „{label}“ не е валидна снимка. Изберете истински JPG, PNG или WEBP файл.");
                return ".jpg";
            }

            return ext == ".jpeg" ? ".jpg" : ext;
        }

        // Reads the leading magic bytes and confirms they match the claimed type.
        private static bool HasImageSignature(IFormFile file, string ext)
        {
            Span<byte> head = stackalloc byte[12];

            using var stream = file.OpenReadStream();
            var read = 0;
            while (read < head.Length)
            {
                var n = stream.Read(head[read..]);
                if (n == 0) break;
                read += n;
            }
            if (read < head.Length) return false;

            return ext switch
            {
                ".jpg" or ".jpeg" => head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,

                ".png" => head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
                       && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A,

                // "RIFF" .... "WEBP"
                ".webp" => head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46
                        && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50,

                _ => false
            };
        }

        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var item = _db.PhotoPairs.Find(id);
            if (item != null)
            {
                DeleteUploadedFile(item.BeforeImagePath);
                DeleteUploadedFile(item.AfterImagePath);

                _db.PhotoPairs.Remove(item);
                _db.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        // Removes one file from wwwroot/uploads. The stored path is always one
        // this app generated, but it is re-checked against the uploads folder
        // anyway so a tampered database row cannot reach outside it.
        private void DeleteUploadedFile(string storedPath)
        {
            try
            {
                var uploads = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads"));
                var relative = storedPath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
                var full = Path.GetFullPath(Path.Combine(_env.WebRootPath, relative));

                if (!full.StartsWith(uploads + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    _log.LogWarning("Refused to delete {Path}: outside the uploads folder.", storedPath);
                    return;
                }

                if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
            }
            catch (Exception ex)
            {
                // The database row still goes away; a leftover file on disk is
                // not worth failing the request over, but it should be visible.
                _log.LogWarning(ex, "Could not delete uploaded file {Path}.", storedPath);
            }
        }
    }
}
