using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HopMop.Data;
using HopMop.Models;

namespace HopMop.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class UsersController : Controller
    {
        private const int MinPasswordLength = 10;

        private readonly AppDbContext _db;
        private readonly IPasswordHasher<User> _hasher;
        private readonly ILogger<UsersController> _log;

        public UsersController(AppDbContext db, IPasswordHasher<User> hasher, ILogger<UsersController> log)
        {
            _db = db;
            _hasher = hasher;
            _log = log;
        }

        public IActionResult Index()
        {
            var items = _db.Users.OrderBy(u => u.Email).ToList();
            return View(items);
        }

        [HttpGet]
        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(string email, string password, bool isAdmin)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", "Email и парола са задължителни.");
                return View();
            }

            // Stored lower-cased so the same address cannot be registered twice
            // under different capitalisation.
            var normalized = email.Trim().ToLowerInvariant();

            if (!new EmailAddressAttribute().IsValid(normalized) || normalized.Length > 254)
            {
                ModelState.AddModelError("", "Въведеният email адрес не е валиден.");
                return View();
            }

            // These accounts administer the public site, so a short password is
            // refused outright rather than left to the person creating it.
            if (password.Length < MinPasswordLength)
            {
                ModelState.AddModelError("", $"Паролата трябва да е поне {MinPasswordLength} символа.");
                return View();
            }

            if (_db.Users.Any(u => u.Email.ToLower() == normalized))
            {
                ModelState.AddModelError("", "Потребител с този email вече съществува.");
                return View();
            }

            var user = new User { Email = normalized, IsAdmin = isAdmin };
            user.PasswordHash = _hasher.HashPassword(user, password);

            _db.Users.Add(user);
            _db.SaveChanges();

            _log.LogInformation("User {Email} created by {Actor} (admin: {IsAdmin}).",
                normalized, User.Identity?.Name, isAdmin);

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            // Prevent an admin from deleting their own account (lockout guard).
            var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentId == id.ToString())
            {
                TempData["Error"] = "Не можете да изтриете собствения си акаунт.";
                return RedirectToAction("Index");
            }

            var item = _db.Users.Find(id);
            if (item != null)
            {
                // Second lockout guard: deleting the last admin would leave the
                // site with no way to reach the admin pages at all.
                if (item.IsAdmin && _db.Users.Count(u => u.IsAdmin) <= 1)
                {
                    TempData["Error"] = "Не можете да изтриете последния администратор.";
                    return RedirectToAction("Index");
                }

                _db.Users.Remove(item);
                _db.SaveChanges();

                _log.LogInformation("User {Email} deleted by {Actor}.", item.Email, User.Identity?.Name);
            }
            return RedirectToAction("Index");
        }
    }
}
