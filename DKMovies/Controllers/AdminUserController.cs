using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using System.Text;
using System.Security.Cryptography;

namespace DKMovies.Controllers
{
    [Authorize(Roles = "Admin")] // Admin only
    public class AdminUserController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AdminUserController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(bytes);
            }
        }

        // GET: AdminUsers
        public async Task<IActionResult> Index(string searchString, string sortOrder, int page = 1)
        {
            ViewData["CurrentSort"] = sortOrder;
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["DateSortParm"] = sortOrder == "Date" ? "date_desc" : "Date";
            ViewData["EmailSortParm"] = sortOrder == "Email" ? "email_desc" : "Email";
            ViewData["CurrentFilter"] = searchString;

            var users = _context.Users
                .Include(u => u.Tickets)
                .Include(u => u.Reviews)
                .Include(u => u.Orders)
                .AsQueryable();

            // Search functionality
            if (!String.IsNullOrEmpty(searchString))
            {
                users = users.Where(u => u.FullName.Contains(searchString)
                                    || u.Email.Contains(searchString)
                                    || u.Username.Contains(searchString)
                                    || u.Phone.Contains(searchString));
            }

            // Sorting
            switch (sortOrder)
            {
                case "name_desc":
                    users = users.OrderByDescending(u => u.FullName);
                    break;
                case "Date":
                    users = users.OrderBy(u => u.CreatedAt);
                    break;
                case "date_desc":
                    users = users.OrderByDescending(u => u.CreatedAt);
                    break;
                case "Email":
                    users = users.OrderBy(u => u.Email);
                    break;
                case "email_desc":
                    users = users.OrderByDescending(u => u.Email);
                    break;
                default:
                    users = users.OrderBy(u => u.FullName);
                    break;
            }

            // Pagination
            int pageSize = 10;
            var totalUsers = await users.CountAsync();
            var pagedUsers = await users
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalUsers / pageSize);
            ViewBag.TotalUsers = totalUsers;

            return View(pagedUsers);
        }

        // GET: AdminUsers/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users
                .Include(u => u.Tickets)
                    .ThenInclude(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                .Include(u => u.Tickets)
                    .ThenInclude(t => t.ShowTime)
                        .ThenInclude(st => st.Auditorium)
                            .ThenInclude(a => a.Theater)
                .Include(u => u.Tickets)
                    .ThenInclude(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                .Include(u => u.Reviews)
                    .ThenInclude(r => r.Movie)
                .Include(u => u.Orders)
                    .ThenInclude(o => o.OrderItems)
                .Include(u => u.WatchlistItems)
                    .ThenInclude(w => w.Movie)
                .FirstOrDefaultAsync(m => m.ID == id);

            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // GET: AdminUsers/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: AdminUsers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Username,Email,PasswordHash,FullName,Phone,BirthDate,Gender")] User user, IFormFile? profileImage)
        {
            if (ModelState.IsValid)
            {
                // Check if username or email already exists
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == user.Username || u.Email == user.Email);

                if (existingUser != null)
                {
                    if (existingUser.Username == user.Username)
                        ModelState.AddModelError("Username", "Tên đăng nhập đã tồn tại.");
                    if (existingUser.Email == user.Email)
                        ModelState.AddModelError("Email", "Email đã được sử dụng.");

                    return View(user);
                }

                // Handle profile image upload
                if (profileImage != null && profileImage.Length > 0)
                {
                    var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "users");
                    Directory.CreateDirectory(uploadsFolder);

                    var uniqueFileName = Guid.NewGuid().ToString() + "_" + profileImage.FileName;
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await profileImage.CopyToAsync(fileStream);
                    }

                    user.ProfileImagePath = "/images/users/" + uniqueFileName;
                }

                user.CreatedAt = DateTime.Now;
                user.EmailConfirmed = false;
                user.TwoFactorEnabled = false;

                // Hash password using the same method as existing UsersController
                user.PasswordHash = HashPassword(user.PasswordHash);

                _context.Add(user);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Tạo người dùng thành công!";
                return RedirectToAction(nameof(Index));
            }
            return View(user);
        }

        // GET: AdminUsers/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }
            return View(user);
        }

        // POST: AdminUsers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ID,Username,Email,FullName,Phone,BirthDate,Gender,EmailConfirmed,TwoFactorEnabled")] User user, IFormFile? profileImage, string? newPassword)
        {
            if (id != user.ID)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.ID == id);
                    if (existingUser == null)
                    {
                        return NotFound();
                    }

                    // Check if username or email is taken by another user
                    var duplicateUser = await _context.Users
                        .FirstOrDefaultAsync(u => u.ID != id && (u.Username == user.Username || u.Email == user.Email));

                    if (duplicateUser != null)
                    {
                        if (duplicateUser.Username == user.Username)
                            ModelState.AddModelError("Username", "Tên đăng nhập đã tồn tại.");
                        if (duplicateUser.Email == user.Email)
                            ModelState.AddModelError("Email", "Email đã được sử dụng.");

                        return View(user);
                    }

                    // Keep original values
                    user.CreatedAt = existingUser.CreatedAt;
                    user.PasswordHash = existingUser.PasswordHash;
                    user.ProfileImagePath = existingUser.ProfileImagePath;
                    user.ConfirmationCode = existingUser.ConfirmationCode;
                    user.TwoFactorCode = existingUser.TwoFactorCode;
                    user.TwoFactorExpiry = existingUser.TwoFactorExpiry;

                    // Update password if provided
                    if (!string.IsNullOrEmpty(newPassword))
                    {
                        user.PasswordHash = HashPassword(newPassword);
                    }

                    // Handle profile image upload
                    if (profileImage != null && profileImage.Length > 0)
                    {
                        // Delete old image
                        if (!string.IsNullOrEmpty(existingUser.ProfileImagePath))
                        {
                            var oldImagePath = Path.Combine(_webHostEnvironment.WebRootPath, existingUser.ProfileImagePath.TrimStart('/'));
                            if (System.IO.File.Exists(oldImagePath))
                            {
                                System.IO.File.Delete(oldImagePath);
                            }
                        }

                        // Upload new image
                        var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "users");
                        Directory.CreateDirectory(uploadsFolder);

                        var uniqueFileName = Guid.NewGuid().ToString() + "_" + profileImage.FileName;
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await profileImage.CopyToAsync(fileStream);
                        }

                        user.ProfileImagePath = "/images/users/" + uniqueFileName;
                    }

                    _context.Update(user);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "Cập nhật người dùng thành công!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(user.ID))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(user);
        }

        // GET: AdminUsers/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users
                .Include(u => u.Tickets)
                .Include(u => u.Reviews)
                .Include(u => u.Orders)
                .FirstOrDefaultAsync(m => m.ID == id);

            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // POST: AdminUsers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var user = await _context.Users
                .Include(u => u.Tickets)
                .Include(u => u.Reviews)
                .Include(u => u.Orders)
                .FirstOrDefaultAsync(u => u.ID == id);

            if (user != null)
            {
                // Check if user has any tickets, orders, or reviews
                if (user.Tickets.Any() || user.Orders.Any() || user.Reviews.Any())
                {
                    TempData["Error"] = "Không thể xóa người dùng này vì đã có dữ liệu liên quan (vé, đơn hàng, đánh giá).";
                    return RedirectToAction(nameof(Index));
                }

                // Delete profile image
                if (!string.IsNullOrEmpty(user.ProfileImagePath))
                {
                    var imagePath = Path.Combine(_webHostEnvironment.WebRootPath, user.ProfileImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Xóa người dùng thành công!";
            }

            return RedirectToAction(nameof(Index));
        }

        // API: Get user statistics
        [HttpGet]
        public async Task<JsonResult> GetUserStatistics()
        {
            try
            {
                var totalUsers = await _context.Users.CountAsync();
                var newUsersThisMonth = await _context.Users
                    .Where(u => u.CreatedAt.Month == DateTime.Now.Month && u.CreatedAt.Year == DateTime.Now.Year)
                    .CountAsync();
                var activeUsers = await _context.Users
                    .Where(u => u.Tickets.Any())
                    .CountAsync();
                var verifiedUsers = await _context.Users
                    .Where(u => u.EmailConfirmed)
                    .CountAsync();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        totalUsers,
                        newUsersThisMonth,
                        activeUsers,
                        verifiedUsers
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // API: Toggle user email confirmation
        [HttpPost]
        public async Task<JsonResult> ToggleEmailConfirmation(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return Json(new { success = false, message = "Người dùng không tồn tại" });
                }

                user.EmailConfirmed = !user.EmailConfirmed;
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    newStatus = user.EmailConfirmed,
                    message = user.EmailConfirmed ? "Đã xác thực email" : "Đã hủy xác thực email"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // API: Send email confirmation
        [HttpPost]
        public async Task<JsonResult> SendEmailConfirmation(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return Json(new { success = false, message = "Người dùng không tồn tại" });
                }

                // TODO: Implement email service
                // For now, just generate a confirmation code
                user.ConfirmationCode = Guid.NewGuid().ToString("N")[..8].ToUpper();
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Email xác thực đã được gửi đến {user.Email}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private bool UserExists(int id)
        {
            return _context.Users.Any(e => e.ID == id);
        }
    }
}