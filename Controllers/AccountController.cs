using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using HopMop.Data;
using HopMop.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace HopMop.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IPasswordHasher<User> _hasher;

        public AccountController(AppDbContext db, IPasswordHasher<User> hasher)
        {
            _db = db;
            _hasher = hasher;
        }

        [HttpGet]
        public IActionResult Login() => View("~/Views/Admin/Login.cshtml");

        [HttpGet]
        public IActionResult AccessDenied()
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("~/Views/Admin/AccessDenied.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", "Невалиден email или парола.");
                return View("~/Views/Admin/Login.cshtml");
            }

            var user = _db.Users.FirstOrDefault(u => u.Email == email);
            if (user == null)
            {
                ModelState.AddModelError("", "Невалиден email или парола.");
                return View("~/Views/Admin/Login.cshtml");
            }

            var res = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (res == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError("", "Невалиден email или парола.");
                return View("~/Views/Admin/Login.cshtml");
            }

            var claims = new List<Claim> { new Claim(ClaimTypes.Name, user.Email), new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("IsAdmin", user.IsAdmin.ToString()) };
            var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id));
            return RedirectToAction("Index", "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }
    }
}
