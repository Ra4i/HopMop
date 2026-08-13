using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using HopMop.Data;
using HopMop.Models;

namespace HopMop.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class UsersController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IPasswordHasher<User> _hasher;

        public UsersController(AppDbContext db, IPasswordHasher<User> hasher)
        {
            _db = db;
            _hasher = hasher;
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

            if (_db.Users.Any(u => u.Email == email))
            {
                ModelState.AddModelError("", "Потребител с този email вече съществува.");
                return View();
            }

            var user = new User { Email = email, IsAdmin = isAdmin };
            user.PasswordHash = _hasher.HashPassword(user, password);

            _db.Users.Add(user);
            _db.SaveChanges();

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
                _db.Users.Remove(item);
                _db.SaveChanges();
            }
            return RedirectToAction("Index");
        }
    }
}
