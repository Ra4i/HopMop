using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using HopMop.Data;
using HopMop.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace HopMop.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IPasswordHasher<User> _hasher;
        private readonly ILogger<AccountController> _log;

        // Verified against when the email is unknown, so a failed login costs the
        // same time whether or not the account exists. Without it the response
        // time alone tells an attacker which emails are registered.
        private static readonly string DummyHash =
            new PasswordHasher<User>().HashPassword(new User { Email = "n/a" }, "not-a-real-password");

        public AccountController(AppDbContext db, IPasswordHasher<User> hasher, ILogger<AccountController> log)
        {
            _db = db;
            _hasher = hasher;
            _log = log;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View("~/Views/Admin/Login.cshtml");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("~/Views/Admin/AccessDenied.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimitPolicies.Login)]
        public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            // One message for every failure mode: saying "no such user" or "wrong
            // password" would confirm which emails have accounts.
            const string failure = "Невалиден email или парола.";

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", failure);
                return View("~/Views/Admin/Login.cshtml");
            }

            var normalized = email.Trim().ToLowerInvariant();
            var user = _db.Users.FirstOrDefault(u => u.Email.ToLower() == normalized);

            var res = user is null
                ? _hasher.VerifyHashedPassword(new User { Email = normalized }, DummyHash, password)
                : _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

            if (user is null || res == PasswordVerificationResult.Failed)
            {
                _log.LogWarning("Failed login attempt for {Email} from {Ip}.",
                    normalized, HttpContext.Connection.RemoteIpAddress);
                ModelState.AddModelError("", failure);
                return View("~/Views/Admin/Login.cshtml");
            }

            // The stored hash used older parameters — rewrite it with the current
            // ones now that the plaintext is known to be correct.
            if (res == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _hasher.HashPassword(user, password);
                _db.SaveChanges();
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Email),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("IsAdmin", user.IsAdmin.ToString())
            };
            var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id));

            _log.LogInformation("User {Email} signed in.", user.Email);

            // IsLocalUrl blocks an open redirect: a crafted ?returnUrl pointing at
            // another site would otherwise bounce the user off after login.
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }
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
