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

namespace DKMovies.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(bytes);
            }
        }

        private async Task SendConfirmationEmail(string toEmail, string code)
        {
            var fromAddress = new MailAddress("ducn3683@gmail.com", "DKMovies");
            var toAddress = new MailAddress(toEmail);
            const string fromPassword = "ubuj nryh dbrf mrcd"; // Không có dấu cách nếu bạn 
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

        [HttpGet]
        public IActionResult Login()
        {
            // Nếu đã đăng nhập, chuyển hướng theo role
            if (User.Identity.IsAuthenticated)
            {
                return RedirectBasedOnRole();
            }
            return View();
        }

        private IActionResult RedirectBasedOnRole()
        {
            var userType = User.FindFirst("UserType")?.Value;

            if (userType == "Admin")
            {
                return RedirectToAction("Dashboard", "Admin");
            }
            else
            {
                return RedirectToAction("Index", "Home");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, bool rememberMe)
        {
            var hashedPassword = HashPassword(password);

            // Kiểm tra Admin trước - ưu tiên admin login
            var admin = await _context.Admins
                .Include(a => a.Employee)
                .ThenInclude(e => e.Role)
                .Include(a => a.Employee.Theater)
                .FirstOrDefaultAsync(a => a.Username == username);

            if (admin != null && admin.PasswordHash == hashedPassword)
            {
                await SignInAdmin(admin, rememberMe);
                TempData["ToastSuccess"] = "🎉 Đăng nhập thành công!";
                // Chuyển hướng đến trang admin
                return RedirectToAction("Dashboard", "Admin");
            }

            // Kiểm tra User thường
            var user = await _context.Users.FirstOrDefaultAsync(u =>
                (u.Username == username || u.Email == username) && u.PasswordHash == hashedPassword);

            if (user != null)
            {
                if (!user.EmailConfirmed)
                {
                    ViewData["ToastError"] = "⚠️ Vui lòng xác minh email trước khi đăng nhập.";
                    ViewBag.ActiveTab = "login";
                    return View();
                }

                if (user.TwoFactorEnabled)
                {
                    var code = new Random().Next(100000, 999999).ToString();
                    user.TwoFactorCode = code;
                    user.TwoFactorExpiry = DateTime.UtcNow.AddMinutes(5);
                    await _context.SaveChangesAsync();

                    await Send2FACodeEmail(user.Email, code);

                    TempData["Email2FA"] = user.Email;
                    TempData["RememberMe"] = rememberMe.ToString();
                    return RedirectToAction("Verify2FA");
                }

                await SignInUser(user, rememberMe);
                TempData["ToastSuccess"] = "🎉 Đăng nhập thành công!";
                // User thường vào trang chủ
                return RedirectToAction("Index", "Home");
            }

            ViewData["ToastError"] = "❌ Tên đăng nhập hoặc mật khẩu không đúng.";
            ViewBag.ActiveTab = "login";
            return View();
        }

        private async Task SignInUser(User user, bool rememberMe)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
                new Claim(ClaimTypes.Role, "User"),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("UserType", "User")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync("MyCookieAuth", principal, new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddHours(1)
            });
        }

        private async Task SignInAdmin(Admin admin, bool rememberMe)
        {
            // Xác định role dựa trên EmployeeRole
            string role = DetermineRole(admin.Employee?.Role?.Name);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, admin.Username),
                new Claim(ClaimTypes.NameIdentifier, admin.ID.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.Email, admin.Employee?.Email ?? ""),
                new Claim("UserType", "Admin"),
                new Claim("EmployeeId", admin.EmployeeID.ToString()),
                new Claim("TheaterId", admin.Employee?.TheaterID.ToString() ?? ""),
                new Claim("TheaterName", admin.Employee?.Theater?.Name ?? "")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync("MyCookieAuth", principal, new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddHours(1)
            });
        }

        private string DetermineRole(string employeeRoleName)
        {
            if (string.IsNullOrEmpty(employeeRoleName))
                return "Employee";

            return employeeRoleName.ToLower() switch
            {
                "superadmin" or "super admin" => "SuperAdmin",
                "admin" or "administrator" => "Admin",
                "manager" => "Admin",
                _ => "Employee"
            };
        }

        [HttpGet]
        public IActionResult Verify2FA()
        {
            ViewBag.Email = TempData["Email2FA"] ?? TempData.Peek("Email2FA");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Verify2FA(string email, string code)
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

                var rememberMe = TempData["RememberMe"] != null && bool.TryParse(TempData["RememberMe"].ToString(), out var r) && r;
                await SignInUser(user, rememberMe);

                TempData["ToastSuccess"] = "🎉 Đăng nhập thành công!";
                return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError("", "Mã xác nhận không hợp lệ hoặc đã hết hạn.");
            ViewBag.Email = TempData["Email2FA"] ?? TempData.Peek("Email2FA");
            return View();
        }

        private async Task Send2FACodeEmail(string toEmail, string code)
        {
            var fromAddress = new MailAddress("ducn3683@gmail.com", "DKMovies");
            var toAddress = new MailAddress(toEmail);
            const string fromPassword = "ubuj nryh dbrf mrcd"; // Không có dấu cách nếu bạn 
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

        [HttpGet]
        public IActionResult SignUp() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SignUp(string username, string email, string password,
                            string fullName, string phone,
                            DateTime? birthDate, string gender)
        {
            if (await _context.Users.AnyAsync(u => u.Username == username))
            {
                ViewData["ToastError"] = "Tên đăng nhập đã tồn tại.";
                ViewBag.ActiveTab = "register";
                return View("Login");
            }

            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                ViewData["ToastError"] = "Email đã được sử dụng.";
                ViewBag.ActiveTab = "register";
                return View("Login");
            }

            // Tạo mã xác nhận
            string confirmationCode = new Random().Next(100000, 999999).ToString();

            // Gửi email xác nhận
            try
            {
                await SendConfirmationEmail(email, confirmationCode);
            }
            catch (Exception)
            {
                ViewData["ToastError"] = "Không thể gửi email xác nhận. Vui lòng thử lại sau.";
                ViewBag.ActiveTab = "register";
                return View("Login");
            }

            // Tạo user nếu gửi email thành công
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
                ConfirmationCode = confirmationCode
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["EmailToVerify"] = email;
            return RedirectToAction("VerifyEmail");
        }

        [HttpGet]
        public IActionResult VerifyEmail()
        {
            ViewBag.Email = TempData["EmailToVerify"] ?? TempData.Peek("EmailToVerify");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyEmail(string email, string code)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                ViewData["ToastError"] = "Vui lòng điền đầy đủ thông tin.";
                ViewBag.Email = email;
                return View();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                ViewData["ToastError"] = "Tài khoản không tồn tại.";
                ViewBag.Email = email;
                return View();
            }

            if (user.EmailConfirmed)
            {
                TempData["ToastSuccess"] = "Email đã được xác minh trước đó.";
                return RedirectToAction("Login");
            }

            if (user.ConfirmationCode == code)
            {
                try
                {
                    user.EmailConfirmed = true;
                    user.ConfirmationCode = null;
                    _context.Entry(user).State = EntityState.Modified;
                    var result = await _context.SaveChangesAsync();

                    if (result > 0)
                    {
                        TempData["ToastSuccess"] = "✅ Xác minh thành công! Bạn có thể đăng nhập.";
                        return RedirectToAction("Login");
                    }
                    else
                    {
                        ViewData["ToastError"] = "❌ Có lỗi xảy ra khi lưu thông tin. Vui lòng thử lại.";
                        ViewBag.Email = email;
                        return View();
                    }
                }
                catch (Exception ex)
                {
                    ViewData["ToastError"] = "❌ Có lỗi xảy ra khi cập nhật thông tin. Vui lòng thử lại.";
                    ViewBag.Email = email;
                    return View();
                }
            }

            ViewData["ToastError"] = "❌ Mã xác nhận không chính xác hoặc đã hết hạn.";
            ViewBag.Email = email;
            return View();
        }

        [HttpGet]
        public IActionResult AdminLogin() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminLogin(string username, string password)
        {
            // CẬP NHẬT: Sử dụng bảng Admins mới với relationship
            var admin = await _context.Admins
                .Include(a => a.Employee)
                .ThenInclude(e => e.Role)
                .FirstOrDefaultAsync(a => a.Username == username);

            if (admin != null && admin.PasswordHash == HashPassword(password))
            {
                // Sử dụng session như cũ để không phá vỡ code hiện tại
                HttpContext.Session.SetString("Username", admin.Username);
                HttpContext.Session.SetString("UserID", admin.ID.ToString());
                HttpContext.Session.SetString("Role", DetermineRole(admin.Employee?.Role?.Name));

                return RedirectToAction("AdminDashboard", "Admin");
            }

            ModelState.AddModelError("", "Tên đăng nhập hoặc mật khẩu không hợp lệ.");
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("MyCookieAuth");
            return RedirectToAction("Login", "Account");
        }
    }
}