using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using DKMovies.Models;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Net.Mail;
using System.Net;
using System.Collections.Generic;

namespace DKMovies.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        #region Helper Methods
        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(bytes);
            }
        }

        private bool IsAdmin(int roleId)
        {
            return roleId == 2; // Admin RoleID
        }

        private string GetUserRole(int roleId)
        {
            return roleId switch
            {
                1 => "User",
                2 => "Admin",
                _ => "User"
            };
        }

        private IActionResult RedirectBasedOnRole()
        {
            return RedirectToAction("Index", "Home");
        }

        private string GenerateSecureRandomCode()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] bytes = new byte[4];
                rng.GetBytes(bytes);
                int randomNumber = Math.Abs(BitConverter.ToInt32(bytes, 0));
                return (randomNumber % 900000 + 100000).ToString();
            }
        }
        #endregion

        #region Email Methods
        private async Task SendConfirmationEmail(string toEmail, string code)
        {
            var fromAddress = new MailAddress("ducn3683@gmail.com", "DKMovies");
            var toAddress = new MailAddress(toEmail);
            const string fromPassword = "ubuj nryh dbrf mrcd";
            string subject = "🎬 DKMovies - Xác nhận tài khoản";
            string body = $@"
                <html>
                <body style='font-family: Arial, sans-serif;'>
                    <h2 style='color:#2c3e50;'>Xác nhận đăng ký DKMovies</h2>
                    <p>Xin chào,</p>
                    <p>Cảm ơn bạn đã đăng ký tài khoản tại <strong>DKMovies</strong>.</p>
                    <p style='font-size:18px;'>
                        🔐 Mã xác nhận của bạn là:<br />
                        <span style='font-size:24px; font-weight:bold; color:#2ecc71;'>{code}</span>
                    </p>
                    <p>Mã này có hiệu lực trong vài phút. Vui lòng không chia sẻ mã này với bất kỳ ai.</p>
                    <p>Trân trọng,<br />Đội ngũ DKMovies</p>
                </body>
                </html>";

            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, fromPassword)
            };

            using (var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            })
            {
                await smtp.SendMailAsync(message);
            }
        }

        private async Task Send2FACodeEmail(string toEmail, string code)
        {
            var fromAddress = new MailAddress("ducn3683@gmail.com", "DKMovies");
            var toAddress = new MailAddress(toEmail);
            const string fromPassword = "ubuj nryh dbrf mrcd";
            string subject = "🎬 DKMovies - Mã xác thực 2FA";
            string body = $@"
                <html>
                <body style='font-family: Arial, sans-serif;'>
                    <h2 style='color:#2c3e50;'>Mã xác thực đăng nhập DKMovies</h2>
                    <p>Xin chào,</p>
                    <p>Mã xác thực 2FA của bạn là:</p>
                    <p style='font-size:18px;'>
                        🔐 <span style='font-size:24px; font-weight:bold; color:#2ecc71;'>{code}</span>
                    </p>
                    <p>Mã này có hiệu lực trong 5 phút. Vui lòng không chia sẻ mã này với bất kỳ ai.</p>
                    <p>Trân trọng,<br />Đội ngũ DKMovies</p>
                </body>
                </html>";

            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, fromPassword)
            };

            using (var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            })
            {
                await smtp.SendMailAsync(message);
            }
        }
        #endregion

        #region Login Methods
        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectBasedOnRole();
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, bool rememberMe)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewData["ToastError"] = "❌ Vui lòng điền đầy đủ thông tin đăng nhập.";
                ViewBag.ActiveTab = "login";
                return View();
            }

            try
            {
                var hashedPassword = HashPassword(password);
                Console.WriteLine("👉 Username nhập: " + username);
                Console.WriteLine("👉 Password nhập: " + password);
                Console.WriteLine("👉 Hash kết quả: " + hashedPassword);


                // Tìm user trong bảng Users với RoleID để xác định quyền
                var user = await _context.Users
                    .FirstOrDefaultAsync(u =>
                        (u.Username == username || u.Email == username) &&
                        u.PasswordHash == hashedPassword);

                if (user == null)
                {
                    // Nếu không tìm thấy trong Users, thử tìm trong Admins (backward compatibility)
                    var admin = await _context.Admins
                        .Include(a => a.Employee)
                        .ThenInclude(e => e.Role)
                        .Include(a => a.Employee.Theater)
                        .FirstOrDefaultAsync(a => a.Username == username && a.PasswordHash == hashedPassword);

                    if (admin != null)
                    {
                        return await ProcessLegacyAdminLogin(admin, rememberMe);
                    }

                    ViewData["ToastError"] = "❌ Tên đăng nhập hoặc mật khẩu không đúng.";
                    ViewBag.ActiveTab = "login";
                    return View();
                }

                return await ProcessLogin(user, rememberMe);
            }
            catch (Exception ex)
            {
                ViewData["ToastError"] = "❌ Có lỗi xảy ra trong quá trình đăng nhập. Vui lòng thử lại.";
                ViewBag.ActiveTab = "login";
                return View();
            }
        }

        private async Task<IActionResult> ProcessLogin(User user, bool rememberMe)
        {
            // Check if email is confirmed
            if (!user.EmailConfirmed)
            {
                ViewData["ToastError"] = "⚠️ Vui lòng xác minh email trước khi đăng nhập.";
                ViewBag.ActiveTab = "login";
                return View();
            }

            // Handle 2FA if enabled
            if (user.TwoFactorEnabled)
            {
                return await Handle2FA(user, rememberMe);
            }

            // Sign in based on role
            await SignInUser(user, rememberMe);

            // Redirect based on role
            if (IsAdmin(user.RoleID))
            {
                TempData["ToastSuccess"] = "🎉 Đăng nhập thành công với tư cách Quản trị viên!";
                return RedirectToAction("Dashboard", "Admin");
            }
            else
            {
                TempData["ToastSuccess"] = "🎉 Đăng nhập thành công!";
                return RedirectToAction("Index", "Home");
            }
        }

        // Backward compatibility cho Admin login từ bảng Admins cũ
        private async Task<IActionResult> ProcessLegacyAdminLogin(Admin admin, bool rememberMe)
        {
            await SignInLegacyAdmin(admin, rememberMe);
            TempData["ToastSuccess"] = "🎉 Đăng nhập thành công với tư cách Quản trị viên!";
            return RedirectToAction("Dashboard", "Admin");
        }

        private async Task<IActionResult> Handle2FA(User user, bool rememberMe)
        {
            try
            {
                var code = GenerateSecureRandomCode();
                user.TwoFactorCode = code;
                user.TwoFactorExpiry = DateTime.UtcNow.AddMinutes(5);

                await _context.SaveChangesAsync();
                await Send2FACodeEmail(user.Email, code);

                TempData["Email2FA"] = user.Email;
                TempData["RememberMe"] = rememberMe.ToString();
                return RedirectToAction("Verify2FA");
            }
            catch (Exception)
            {
                ViewData["ToastError"] = "❌ Không thể gửi mã xác thực. Vui lòng thử lại.";
                ViewBag.ActiveTab = "login";
                return View();
            }
        }

        private async Task SignInUser(User user, bool rememberMe)
        {
            var userRole = GetUserRole(user.RoleID);
            var userType = IsAdmin(user.RoleID) ? "Admin" : "User";

            var claims = new List<System.Security.Claims.Claim>
{
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user.Username),
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.ID.ToString()),
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, userRole),
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, user.Email),
    new System.Security.Claims.Claim("UserType", userType),
    new System.Security.Claims.Claim("FullName", user.FullName ?? ""),
    new System.Security.Claims.Claim("RoleID", user.RoleID.ToString())
};


            // Nếu là Admin, thêm thông tin Employee (nếu có)
            if (IsAdmin(user.RoleID))
            {
                var employee = await _context.Employees
                    .Include(e => e.Theater)
                    .Include(e => e.Role)
                    .FirstOrDefaultAsync(e => e.Email == user.Email);

                if (employee != null)
                {
                    claims.Add(new Claim("EmployeeId", employee.ID.ToString()));
                    claims.Add(new System.Security.Claims.Claim("EmployeeId", employee.ID.ToString()));
                    claims.Add(new Claim("TheaterName", employee.Theater?.Name ?? ""));

                    // FIX: Đảm bảo giá trị không null và convert thành string
                    var employeeRoleName = employee.Role?.Name ?? "";
                    claims.Add(new Claim("EmployeeRole", employeeRoleName));
                }
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync("MyCookieAuth", principal, new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddHours(1)
            });
        }

        // Backward compatibility - sẽ được remove sau khi migrate
        private async Task SignInLegacyAdmin(Admin admin, bool rememberMe)
        {
            string role = DetermineLegacyRole(admin.Employee?.Role?.Name);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, admin.Username),
                new Claim(ClaimTypes.NameIdentifier, admin.ID.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.Email, admin.Employee?.Email ?? ""),
                new Claim("UserType", "Admin"),
                new Claim("EmployeeId", admin.EmployeeID.ToString()),
                new Claim("TheaterId", admin.Employee?.TheaterID.ToString() ?? ""),
                new Claim("TheaterName", admin.Employee?.Theater?.Name ?? ""),
                new Claim("EmployeeRole", admin.Employee?.Role?.Name ?? "")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync("MyCookieAuth", principal, new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddHours(1)
            });
        }

        private string DetermineLegacyRole(string employeeRoleName)
        {
            if (string.IsNullOrEmpty(employeeRoleName))
                return "Admin";

            return employeeRoleName.ToLower() switch
            {
                "superadmin" or "super admin" => "SuperAdmin",
                "admin" or "administrator" => "Admin",
                "manager" => "Admin",
                _ => "Admin"
            };
        }
        #endregion

        #region 2FA Methods
        [HttpGet]
        public IActionResult Verify2FA()
        {
            ViewBag.Email = TempData["Email2FA"] ?? TempData.Peek("Email2FA");
            if (string.IsNullOrEmpty(ViewBag.Email?.ToString()))
            {
                return RedirectToAction("Login");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Verify2FA(string email, string code)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                ModelState.AddModelError("", "Vui lòng điền đầy đủ thông tin.");
                ViewBag.Email = email;
                return View();
            }

            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
                if (user == null || !user.TwoFactorEnabled)
                {
                    ModelState.AddModelError("", "Tài khoản không hợp lệ hoặc chưa bật 2FA.");
                    return View();
                }

                if (user.TwoFactorCode == code && user.TwoFactorExpiry > DateTime.UtcNow)
                {
                    user.TwoFactorCode = null;
                    user.TwoFactorExpiry = null;
                    await _context.SaveChangesAsync();

                    var rememberMe = TempData["RememberMe"] != null &&
                                   bool.TryParse(TempData["RememberMe"].ToString(), out var r) && r;

                    await SignInUser(user, rememberMe);

                    if (IsAdmin(user.RoleID))
                    {
                        TempData["ToastSuccess"] = "🎉 Đăng nhập thành công với tư cách Quản trị viên!";
                        return RedirectToAction("Dashboard", "Admin");
                    }
                    else
                    {
                        TempData["ToastSuccess"] = "🎉 Đăng nhập thành công!";
                        return RedirectToAction("Index", "Home");
                    }
                }

                ModelState.AddModelError("", "Mã xác nhận không hợp lệ hoặc đã hết hạn.");
                ViewBag.Email = TempData["Email2FA"] ?? TempData.Peek("Email2FA");
                return View();
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "Có lỗi xảy ra khi xác thực. Vui lòng thử lại.");
                ViewBag.Email = email;
                return View();
            }
        }
        #endregion

        #region Registration Methods
        [HttpGet]
        public IActionResult SignUp() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SignUp(string username, string email, string password,
                            string fullName, string phone,
                            DateTime? birthDate, string gender)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fullName))
            {
                ViewData["ToastError"] = "❌ Vui lòng điền đầy đủ thông tin bắt buộc.";
                ViewBag.ActiveTab = "register";
                return View("Login");
            }

            try
            {
                // Check for duplicate username in both Users and Admins tables (backward compatibility)
                if (await _context.Users.AnyAsync(u => u.Username == username) ||
                    await _context.Admins.AnyAsync(a => a.Username == username))
                {
                    ViewData["ToastError"] = "❌ Tên đăng nhập đã tồn tại trong hệ thống.";
                    ViewBag.ActiveTab = "register";
                    return View("Login");
                }

                if (await _context.Users.AnyAsync(u => u.Email == email))
                {
                    ViewData["ToastError"] = "❌ Email đã được sử dụng.";
                    ViewBag.ActiveTab = "register";
                    return View("Login");
                }

                string confirmationCode = GenerateSecureRandomCode();
                await SendConfirmationEmail(email, confirmationCode);

                var user = new User
                {
                    Username = username,
                    Email = email,
                    PasswordHash = HashPassword(password),
                    FullName = fullName,
                    Phone = phone,
                    BirthDate = birthDate,
                    Gender = gender,
                    CreatedAt = DateTime.Now,
                    EmailConfirmed = false,
                    ConfirmationCode = confirmationCode,
                    RoleID = 1 // Default to User role
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                TempData["EmailToVerify"] = email;
                return RedirectToAction("VerifyEmail");
            }
            catch (Exception)
            {
                ViewData["ToastError"] = "❌ Không thể tạo tài khoản. Vui lòng thử lại sau.";
                ViewBag.ActiveTab = "register";
                return View("Login");
            }
        }

        [HttpGet]
        public IActionResult VerifyEmail()
        {
            ViewBag.Email = TempData["EmailToVerify"] ?? TempData.Peek("EmailToVerify");
            if (string.IsNullOrEmpty(ViewBag.Email?.ToString()))
            {
                return RedirectToAction("SignUp");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyEmail(string email, string code)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                ViewData["ToastError"] = "❌ Vui lòng điền đầy đủ thông tin.";
                ViewBag.Email = email;
                return View();
            }

            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

                if (user == null)
                {
                    ViewData["ToastError"] = "❌ Tài khoản không tồn tại.";
                    ViewBag.Email = email;
                    return View();
                }

                if (user.EmailConfirmed)
                {
                    TempData["ToastSuccess"] = "✅ Email đã được xác minh trước đó.";
                    return RedirectToAction("Login");
                }

                if (user.ConfirmationCode == code)
                {
                    user.EmailConfirmed = true;
                    user.ConfirmationCode = null;

                    await _context.SaveChangesAsync();
                    TempData["ToastSuccess"] = "✅ Xác minh thành công! Bạn có thể đăng nhập.";
                    return RedirectToAction("Login");
                }

                ViewData["ToastError"] = "❌ Mã xác nhận không chính xác.";
                ViewBag.Email = email;
                return View();
            }
            catch (Exception)
            {
                ViewData["ToastError"] = "❌ Có lỗi xảy ra khi xác minh. Vui lòng thử lại.";
                ViewBag.Email = email;
                return View();
            }
        }
        #endregion

        #region Logout
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("MyCookieAuth");
            TempData["ToastSuccess"] = "👋 Đăng xuất thành công!";
            return RedirectToAction("Login", "Account");
        }
        #endregion
    }

    #region Authorization Helper Class
    public static class AuthHelper
    {
        public static bool IsAdmin(ClaimsPrincipal user)
        {
            return user.IsInRole("Admin");
        }

        public static bool IsUser(ClaimsPrincipal user)
        {
            return user.IsInRole("User");
        }

        public static int GetUserId(ClaimsPrincipal user)
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        public static string GetUserType(ClaimsPrincipal user)
        {
            return user.FindFirst("UserType")?.Value ?? "User";
        }

        public static int GetRoleId(ClaimsPrincipal user)
        {
            var roleIdClaim = user.FindFirst("RoleID")?.Value;
            return int.TryParse(roleIdClaim, out int roleId) ? roleId : 1;
        }

        public static string GetFullName(ClaimsPrincipal user)
        {
            return user.FindFirst("FullName")?.Value ?? "";
        }

        public static int GetEmployeeId(ClaimsPrincipal user)
        {
            var employeeIdClaim = user.FindFirst("EmployeeId")?.Value;
            return int.TryParse(employeeIdClaim, out int employeeId) ? employeeId : 0;
        }

        public static int GetTheaterId(ClaimsPrincipal user)
        {
            var theaterIdClaim = user.FindFirst("TheaterId")?.Value;
            return int.TryParse(theaterIdClaim, out int theaterId) ? theaterId : 0;
        }
    }
    #endregion
}