using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using DKMovies.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System.Security.Cryptography;
using System.Text;

namespace DKMovies.Controllers
{
    [Authorize(Policy = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AdminController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // ===== DASHBOARD ACTIONS =====

        // GET: Admin Dashboard
        public async Task<IActionResult> Index()
        {
            try
            {
                var totalUsers = await _context.Users.CountAsync();
                var totalEmployees = await _context.Employees.CountAsync();
                var totalMovies = await _context.Movies.CountAsync();
                var totalShowTimes = await _context.ShowTimes.CountAsync();
                var totalConcessions = await _context.Concessions.CountAsync();

                // Calculate total revenue from tickets only (no Orders table)
                var totalRevenue = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .SumAsync(t => t.ShowTime.Price);

                var model = new DashboardViewModel
                {
                    TotalUsers = totalUsers,
                    TotalEmployees = totalEmployees,
                    TotalMovies = totalMovies,
                    TotalShowTimes = totalShowTimes,
                    TotalConcessions = totalConcessions,
                    TotalRevenue = totalRevenue
                };

                // Pass data through ViewBag for the view
                ViewBag.TotalUsers = totalUsers;
                ViewBag.TotalEmployees = totalEmployees;
                ViewBag.TotalMovies = totalMovies;
                ViewBag.TotalShowTimes = totalShowTimes;
                ViewBag.TotalConcessions = totalConcessions;
                ViewBag.TotalRevenue = totalRevenue;

                return View(model);
            }
            catch (Exception ex)
            {
                // Log the error and return a safe fallback
                var emptyModel = new DashboardViewModel
                {
                    TotalUsers = 0,
                    TotalEmployees = 0,
                    TotalMovies = 0,
                    TotalShowTimes = 0,
                    TotalConcessions = 0,
                    TotalRevenue = 0
                };

                ViewBag.TotalUsers = 0;
                ViewBag.TotalEmployees = 0;
                ViewBag.TotalMovies = 0;
                ViewBag.TotalShowTimes = 0;
                ViewBag.TotalConcessions = 0;
                ViewBag.TotalRevenue = 0;
                ViewBag.ErrorMessage = "Có lỗi xảy ra khi tải dữ liệu dashboard.";

                return View(emptyModel);
            }
        }

        // Add a simple Home action to handle navigation to main dashboard
        public IActionResult Home()
        {
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> MovieDashboard()
        {
            try
            {
                // ✅ Create DashboardViewModel with basic stats
                var model = new DashboardViewModel
                {
                    TotalUsers = await _context.Users.CountAsync(),
                    TotalEmployees = await _context.Employees.CountAsync(),
                    TotalMovies = await _context.Movies.CountAsync(),
                    TotalShowTimes = await _context.ShowTimes.CountAsync(),
                    TotalConcessions = await _context.Concessions.CountAsync(),
                    TotalRevenue = await _context.Tickets
                        .Include(t => t.ShowTime)
                        .SumAsync(t => t.ShowTime.Price),

                    // ✅ Additional metrics
                    TodayTickets = await _context.Tickets
                        .Where(t => t.PurchaseTime.Date == DateTime.Today)
                        .CountAsync(),
                    ThisMonthRevenue = await _context.Tickets
                        .Include(t => t.ShowTime)
                        .Where(t => t.PurchaseTime.Month == DateTime.Now.Month &&
                                   t.PurchaseTime.Year == DateTime.Now.Year)
                        .SumAsync(t => t.ShowTime.Price),
                    ActiveShowtimes = await _context.ShowTimes
                        .Where(st => st.StartTime > DateTime.Now)
                        .CountAsync()
                };

                // Pass data through ViewBag for the view
                ViewBag.TotalUsers = model.TotalUsers;
                ViewBag.TotalEmployees = model.TotalEmployees;
                ViewBag.TotalMovies = model.TotalMovies;
                ViewBag.TotalShowTimes = model.TotalShowTimes;
                ViewBag.TotalConcessions = model.TotalConcessions;
                ViewBag.TotalRevenue = model.TotalRevenue;

                // ✅ Check if we have any tickets first
                var hasTickets = await _context.Tickets.AnyAsync();
                if (!hasTickets)
                {
                    model.TopMovies = new List<MovieScoreViewModel>();
                    ViewBag.ErrorMessage = "Chưa có dữ liệu bán vé để phân tích";
                    return View(model);
                }

                var movieStats = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Movie)
                    .ThenInclude(m => m.Reviews)
                    .Where(t => t.ShowTime != null && t.ShowTime.Movie != null)
                    .GroupBy(t => t.ShowTime.MovieID)
                    .Select(g => new
                    {
                        MovieID = g.Key,
                        Title = g.First().ShowTime.Movie.Title,
                        TicketsSold = g.Count(),
                        TotalRevenue = g.Sum(t => t.ShowTime.Price),
                        AvgRating = g.First().ShowTime.Movie.Reviews.Any()
                            ? g.First().ShowTime.Movie.Reviews.Average(r => r.Rating)
                            : 0
                    })
                    .ToListAsync();

                if (!movieStats.Any())
                {
                    model.TopMovies = new List<MovieScoreViewModel>();
                    ViewBag.ErrorMessage = "Không có dữ liệu phim để hiển thị";
                    return View(model);
                }

                double maxRevenue = movieStats.Max(s => (double)s.TotalRevenue);
                double maxTickets = movieStats.Max(s => (double)s.TicketsSold);
                double maxRating = 5.0;

                var scored = movieStats.Select(s => new MovieScoreViewModel
                {
                    MovieID = s.MovieID,
                    Title = s.Title ?? "Unknown Movie",
                    TicketsSold = s.TicketsSold,
                    TotalRevenue = s.TotalRevenue,
                    AvgRating = s.AvgRating,
                    PriorityScore = maxRevenue > 0 && maxTickets > 0
                        ? ((double)s.TotalRevenue / maxRevenue) * 50
                          + ((double)s.TicketsSold / maxTickets) * 40
                          + (s.AvgRating / maxRating) * 10
                        : 0
                })
                .OrderByDescending(s => s.PriorityScore)
                .Take(5)
                .ToList();

                // ✅ Set TopMovies in the model
                model.TopMovies = scored;

                // ✅ Calculate average rating
                model.AverageRating = scored.Any() ? scored.Average(m => m.AvgRating) : 0;

                return View(model); // Return DashboardViewModel
            }
            catch (Exception ex)
            {
                var errorModel = new DashboardViewModel
                {
                    TotalUsers = 0,
                    TotalEmployees = 0,
                    TotalMovies = 0,
                    TotalShowTimes = 0,
                    TotalConcessions = 0,
                    TotalRevenue = 0,
                    TopMovies = new List<MovieScoreViewModel>()
                };

                ViewBag.TotalUsers = 0;
                ViewBag.TotalEmployees = 0;
                ViewBag.TotalMovies = 0;
                ViewBag.TotalShowTimes = 0;
                ViewBag.TotalConcessions = 0;
                ViewBag.TotalRevenue = 0;
                ViewBag.ErrorMessage = "Có lỗi xảy ra khi tải dữ liệu thống kê phim.";
                return View(errorModel);
            }
        }

        // ===== PROFILE MANAGEMENT ACTIONS =====

        // GET: Admin Profile
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            try
            {
                var adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

                var admin = await _context.Admins
                    .Include(a => a.Employee)
                    .ThenInclude(e => e.Role)
                    .Include(a => a.Employee.Theater)
                    .FirstOrDefaultAsync(a => a.ID == adminId);

                if (admin == null)
                {
                    TempData["ToastError"] = "❌ Không tìm thấy thông tin admin.";
                    return RedirectToAction("Index");
                }

                var profileViewModel = new AdminProfileViewModel
                {
                    AdminId = admin.ID,
                    Username = admin.Username,
                    EmployeeId = admin.EmployeeID,
                    FullName = admin.Employee?.FullName ?? "",
                    Email = admin.Employee?.Email ?? "",
                    Phone = admin.Employee?.Phone ?? "",
                    Gender = admin.Employee?.Gender ?? "",
                    DateOfBirth = admin.Employee?.DateOfBirth,
                    CitizenID = admin.Employee?.CitizenID ?? "",
                    Address = admin.Employee?.Address ?? "",
                    HireDate = admin.Employee?.HireDate ?? DateTime.Now,
                    Salary = admin.Employee?.Salary ?? 0,
                    RoleName = admin.Employee?.Role?.Name ?? "",
                    TheaterName = admin.Employee?.Theater?.Name ?? "",
                    TheaterLocation = admin.Employee?.Theater?.Location ?? "",
                    ProfileImagePath = admin.Employee?.ProfileImagePath,
                    CreatedAt = admin.CreatedAt
                };

                return View(profileViewModel);
            }
            catch (Exception ex)
            {
                TempData["ToastError"] = "❌ Có lỗi xảy ra khi tải thông tin profile.";
                return RedirectToAction("Index");
            }
        }

        // GET: Edit Admin Profile
        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            try
            {
                var adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

                var admin = await _context.Admins
                    .Include(a => a.Employee)
                    .ThenInclude(e => e.Role)
                    .Include(a => a.Employee.Theater)
                    .FirstOrDefaultAsync(a => a.ID == adminId);

                if (admin == null)
                {
                    TempData["ToastError"] = "❌ Không tìm thấy thông tin admin.";
                    return RedirectToAction("Index");
                }

                var editViewModel = new EditAdminProfileViewModel
                {
                    AdminId = admin.ID,
                    Username = admin.Username,
                    EmployeeId = admin.EmployeeID,
                    FullName = admin.Employee?.FullName ?? "",
                    Email = admin.Employee?.Email ?? "",
                    Phone = admin.Employee?.Phone ?? "",
                    Gender = admin.Employee?.Gender ?? "",
                    DateOfBirth = admin.Employee?.DateOfBirth,
                    CitizenID = admin.Employee?.CitizenID ?? "",
                    Address = admin.Employee?.Address ?? "",
                    CurrentProfileImagePath = admin.Employee?.ProfileImagePath
                };

                return View(editViewModel);
            }
            catch (Exception ex)
            {
                TempData["ToastError"] = "❌ Có lỗi xảy ra khi tải form chỉnh sửa.";
                return RedirectToAction("Profile");
            }
        }

        // POST: Update Admin Profile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(EditAdminProfileViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var admin = await _context.Admins
                    .Include(a => a.Employee)
                    .FirstOrDefaultAsync(a => a.ID == model.AdminId);

                if (admin == null)
                {
                    TempData["ToastError"] = "❌ Không tìm thấy thông tin admin.";
                    return RedirectToAction("Index");
                }

                // Check if username is already taken by another admin
                if (admin.Username != model.Username)
                {
                    var usernameExists = await _context.Admins
                        .AnyAsync(a => a.Username == model.Username && a.ID != model.AdminId);

                    var userUsernameExists = await _context.Users
                        .AnyAsync(u => u.Username == model.Username);

                    if (usernameExists || userUsernameExists)
                    {
                        ModelState.AddModelError("Username", "Tên đăng nhập đã tồn tại trong hệ thống.");
                        return View(model);
                    }

                    admin.Username = model.Username;
                }

                // Check if email is already taken by another employee
                if (admin.Employee?.Email != model.Email)
                {
                    var emailExists = await _context.Employees
                        .AnyAsync(e => e.Email == model.Email && e.ID != admin.EmployeeID);

                    if (emailExists)
                    {
                        ModelState.AddModelError("Email", "Email đã được sử dụng bởi nhân viên khác.");
                        return View(model);
                    }
                }

                // Handle profile image upload
                string profileImagePath = admin.Employee?.ProfileImagePath;
                if (model.ProfileImage != null && model.ProfileImage.Length > 0)
                {
                    profileImagePath = await SaveProfileImage(model.ProfileImage);

                    // Delete old image if exists
                    if (!string.IsNullOrEmpty(admin.Employee?.ProfileImagePath))
                    {
                        DeleteOldImage(admin.Employee.ProfileImagePath);
                    }
                }

                // Update employee information
                if (admin.Employee != null)
                {
                    admin.Employee.FullName = model.FullName;
                    admin.Employee.Email = model.Email;
                    admin.Employee.Phone = model.Phone;
                    admin.Employee.Gender = model.Gender;
                    admin.Employee.DateOfBirth = model.DateOfBirth;
                    admin.Employee.CitizenID = model.CitizenID;
                    admin.Employee.Address = model.Address;
                    admin.Employee.ProfileImagePath = profileImagePath;
                }

                await _context.SaveChangesAsync();
                TempData["ToastSuccess"] = "✅ Cập nhật thông tin thành công!";
                return RedirectToAction("Profile");
            }
            catch (Exception ex)
            {
                TempData["ToastError"] = "❌ Có lỗi xảy ra khi cập nhật thông tin.";
                return View(model);
            }
        }

        // GET: Change Password
        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View(new ChangePasswordViewModel());
        }

        // POST: Change Password
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var admin = await _context.Admins.FindAsync(adminId);

                if (admin == null)
                {
                    TempData["ToastError"] = "❌ Không tìm thấy thông tin admin.";
                    return RedirectToAction("Index");
                }

                // Verify current password
                var currentPasswordHash = HashPassword(model.CurrentPassword);
                if (admin.PasswordHash != currentPasswordHash)
                {
                    ModelState.AddModelError("CurrentPassword", "Mật khẩu hiện tại không đúng.");
                    return View(model);
                }

                // Update password
                admin.PasswordHash = HashPassword(model.NewPassword);
                await _context.SaveChangesAsync();

                TempData["ToastSuccess"] = "✅ Đổi mật khẩu thành công!";
                return RedirectToAction("Profile");
            }
            catch (Exception ex)
            {
                TempData["ToastError"] = "❌ Có lỗi xảy ra khi đổi mật khẩu.";
                return View(model);
            }
        }

        // ===== API ENDPOINTS FOR DASHBOARD =====

        // ===== SỬA: REAL-TIME TOP 5 MOVIES API - Đã tối ưu hóa =====
        [HttpGet]
        public async Task<JsonResult> GetTop5MoviesRealTime()
        {
            try
            {
                var endDate = DateTime.Now;
                var startDate = endDate.AddDays(-7); // Last 7 days

                // SỬA: Kiểm tra có tickets trước
                var hasTickets = await _context.Tickets.AnyAsync();
                if (!hasTickets)
                {
                    return Json(new
                    {
                        success = true,
                        data = new object[0],
                        lastUpdated = DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy"),
                        message = "Chưa có dữ liệu vé"
                    });
                }

                var movieStats = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Movie)
                    .Where(t => t.PurchaseTime >= startDate &&
                               t.ShowTime != null &&
                               t.ShowTime.Movie != null)
                    .GroupBy(t => t.ShowTime.MovieID)
                    .Select(g => new
                    {
                        MovieID = g.Key,
                        Title = g.First().ShowTime.Movie.Title,
                        TicketsSold = g.Count(),
                        TotalRevenue = g.Sum(t => t.ShowTime.Price)
                    })
                    .OrderByDescending(x => x.TotalRevenue)
                    .ThenByDescending(x => x.TicketsSold)
                    .Take(5)
                    .ToListAsync();

                return Json(new
                {
                    success = true,
                    data = movieStats.Select((movie, index) => new
                    {
                        rank = index + 1,
                        movieId = movie.MovieID,
                        title = movie.Title ?? "Không có tên",
                        ticketsSold = movie.TicketsSold,
                        revenue = movie.TotalRevenue,
                        revenueFormatted = string.Format("{0:N0} ₫", movie.TotalRevenue)
                    }),
                    lastUpdated = DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy")
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Lỗi khi tải dữ liệu top phim: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetRevenueChartData(string period = "7days")
        {
            try
            {
                var endDate = DateTime.Now;
                var startDate = period switch
                {
                    "7days" => endDate.AddDays(-6),
                    "30days" => endDate.AddDays(-29),
                    "12months" => endDate.AddMonths(-11),
                    _ => endDate.AddDays(-6)
                };

                // ✅ SỬA: Tách riêng query để tránh lỗi GroupBy phức tạp
                var tickets = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .Where(t => t.PurchaseTime >= startDate && t.PurchaseTime <= endDate)
                    .Select(t => new
                    {
                        PurchaseTime = t.PurchaseTime,
                        Price = t.ShowTime.Price
                    })
                    .ToListAsync();

                // ✅ Group data in memory instead of database
                IEnumerable<object> groupedData;

                if (period == "12months")
                {
                    groupedData = tickets
                        .GroupBy(t => new { Year = t.PurchaseTime.Year, Month = t.PurchaseTime.Month })
                        .Select(g => new
                        {
                            Date = new DateTime(g.Key.Year, g.Key.Month, 1),
                            Revenue = g.Sum(t => t.Price),
                            TicketCount = g.Count()
                        })
                        .OrderBy(x => x.Date);
                }
                else
                {
                    groupedData = tickets
                        .GroupBy(t => new { Year = t.PurchaseTime.Year, Month = t.PurchaseTime.Month, Day = t.PurchaseTime.Day })
                        .Select(g => new
                        {
                            Date = new DateTime(g.Key.Year, g.Key.Month, g.Key.Day),
                            Revenue = g.Sum(t => t.Price),
                            TicketCount = g.Count()
                        })
                        .OrderBy(x => x.Date);
                }

                var revenueData = groupedData.ToList();

                var labels = revenueData.Select(r => period == "12months"
                    ? ((dynamic)r).Date.ToString("MM/yyyy")
                    : ((dynamic)r).Date.ToString("dd/MM")).ToArray();

                var revenues = revenueData.Select(r => ((dynamic)r).Revenue).ToArray();
                var tickets_count = revenueData.Select(r => ((dynamic)r).TicketCount).ToArray();

                return Json(new
                {
                    labels = labels,
                    revenues = revenues,
                    tickets = tickets_count
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = "Lỗi khi tải dữ liệu doanh thu: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetMoviePerformanceData()
        {
            try
            {
                var movieData = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Movie)
                    .Where(t => t.ShowTime != null && t.ShowTime.Movie != null)
                    .GroupBy(t => t.ShowTime.Movie.Title)
                    .Select(g => new
                    {
                        MovieTitle = g.Key,
                        TicketsSold = g.Count(),
                        Revenue = g.Sum(t => t.ShowTime.Price)
                    })
                    .OrderByDescending(x => x.TicketsSold)
                    .Take(5)
                    .ToListAsync();

                var labels = movieData.Select(m => m.MovieTitle).ToArray();
                var ticketCounts = movieData.Select(m => m.TicketsSold).ToArray();
                var colors = new[] { "#0d6efd", "#198754", "#ffc107", "#dc3545", "#6c757d" };

                return Json(new
                {
                    labels = labels,
                    data = ticketCounts,
                    colors = colors
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = "Lỗi khi tải dữ liệu phim: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetTheaterPerformanceData()
        {
            try
            {
                var theaterData = await _context.ShowTimes
                    .Include(st => st.Auditorium)
                    .ThenInclude(a => a.Theater)
                    .Include(st => st.Tickets)
                    .Where(st => st.Auditorium != null && st.Auditorium.Theater != null)
                    .GroupBy(st => st.Auditorium.Theater.Name)
                    .Select(g => new
                    {
                        TheaterName = g.Key,
                        TotalShows = g.Count(),
                        TicketsSold = g.SelectMany(st => st.Tickets).Count(),
                        TotalCapacity = g.Count() * 100 // Assume 100 seats per show
                    })
                    .ToListAsync();

                var labels = theaterData.Select(t => t.TheaterName).ToArray();
                var performancePercentages = theaterData.Select(t =>
                    t.TotalCapacity > 0 ? Math.Round((double)t.TicketsSold / t.TotalCapacity * 100, 1) : 0
                ).ToArray();

                return Json(new
                {
                    labels = labels,
                    data = performancePercentages
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = "Lỗi khi tải dữ liệu rạp chiếu: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetRecentActivity()
        {
            try
            {
                var activities = new List<object>();

                // Recent movies
                var recentMovies = await _context.Movies
                    .OrderByDescending(m => m.ID)
                    .Take(2)
                    .Select(m => new
                    {
                        Type = "movie",
                        Icon = "fas fa-film",
                        Color = "primary",
                        Title = "Phim mới được thêm",
                        Description = $"{m.Title} - {GetTimeAgo(DateTime.Now.AddHours(-new Random().Next(1, 24)))}"
                    })
                    .ToListAsync();

                // Recent tickets
                var recentTickets = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Movie)
                    .OrderByDescending(t => t.PurchaseTime)
                    .Take(2)
                    .Select(t => new
                    {
                        Type = "ticket",
                        Icon = "fas fa-ticket-alt",
                        Color = "success",
                        Title = "Vé mới được đặt",
                        Description = $"1 vé cho {t.ShowTime.Movie.Title} - {GetTimeAgo(t.PurchaseTime)}"
                    })
                    .ToListAsync();

                activities.AddRange(recentMovies.Cast<object>());
                activities.AddRange(recentTickets.Cast<object>());

                // Shuffle and take 5
                var random = new Random();
                var shuffledActivities = activities.OrderBy(x => random.Next()).Take(5);

                return Json(shuffledActivities);
            }
            catch (Exception ex)
            {
                return Json(new { error = "Lỗi khi tải hoạt động gần đây: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetDashboardStats()
        {
            try
            {
                var stats = new
                {
                    TotalUsers = await _context.Users.CountAsync(),
                    TotalMovies = await _context.Movies.CountAsync(),
                    TotalShowTimes = await _context.ShowTimes.CountAsync(),
                    TotalRevenue = await _context.Tickets
                        .Include(t => t.ShowTime)
                        .SumAsync(t => t.ShowTime.Price),
                    TodayTickets = await _context.Tickets
                        .Where(t => t.PurchaseTime.Date == DateTime.Today)
                        .CountAsync(),
                    ThisMonthRevenue = await _context.Tickets
                        .Include(t => t.ShowTime)
                        .Where(t => t.PurchaseTime.Month == DateTime.Now.Month &&
                                   t.PurchaseTime.Year == DateTime.Now.Year)
                        .SumAsync(t => t.ShowTime.Price)
                };

                return Json(stats);
            }
            catch (Exception ex)
            {
                return Json(new { error = "Lỗi khi tải thống kê: " + ex.Message });
            }
        }

        // ===== SỬA: AUTO SHOWTIME MANAGEMENT - Đã tối ưu hóa =====
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> AutoManageShowtimes()
        {
            try
            {
                var result = await PerformSimpleAutoManagement();
                return Json(new
                {
                    success = true,
                    message = "Quản lý suất chiếu tự động hoàn thành",
                    addedCount = result.AddedCount,
                    removedCount = result.RemovedCount,
                    details = result.Details
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Lỗi khi quản lý suất chiếu tự động: " + ex.Message });
            }
        }

        // ===== PRIVATE HELPER METHODS =====

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(bytes);
            }
        }

        private async Task<string> SaveProfileImage(IFormFile imageFile)
        {
            try
            {
                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "profiles");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var uniqueFileName = Guid.NewGuid().ToString() + "_" + imageFile.FileName;
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(fileStream);
                }

                return "/uploads/profiles/" + uniqueFileName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void DeleteOldImage(string imagePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(imagePath))
                {
                    var fullPath = Path.Combine(_webHostEnvironment.WebRootPath, imagePath.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                }
            }
            catch (Exception)
            {
                // Log error but don't throw
            }
        }

        // SỬA: Simplified auto management logic - Tối ưu hóa cho chu kỳ 5 phút
        private async Task<SimpleAutoResult> PerformSimpleAutoManagement()
        {
            var result = new SimpleAutoResult();
            var now = DateTime.Now;

            // SỬA: Thay đổi từ 7 ngày thành 24 giờ để phù hợp với chu kỳ 5 phút
            var oneDayAgo = now.AddHours(-24);

            try
            {
                // SỬA: Kiểm tra có tickets trước
                var hasTickets = await _context.Tickets.AnyAsync();
                if (!hasTickets)
                {
                    result.Details.Add("Chưa có dữ liệu vé để phân tích");
                    return result;
                }

                var moviePerformance = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Movie)
                    .Where(t => t.PurchaseTime >= oneDayAgo &&
                               t.ShowTime != null &&
                               t.ShowTime.Movie != null)
                    .GroupBy(t => t.ShowTime.MovieID)
                    .Select(g => new
                    {
                        MovieID = g.Key,
                        MovieTitle = g.First().ShowTime.Movie.Title,
                        TicketsSold = g.Count(),
                        TotalRevenue = g.Sum(t => t.ShowTime.Price)
                    })
                    .ToListAsync();

                if (!moviePerformance.Any())
                {
                    result.Details.Add("Không có dữ liệu performance trong 24h qua để phân tích");
                    return result;
                }

                var avgRevenue = moviePerformance.Average(x => (double)x.TotalRevenue);
                var avgTickets = moviePerformance.Average(x => x.TicketsSold);

                // SỬA: Điều chỉnh threshold cho chu kỳ ngắn
                var topPerformers = moviePerformance
                    .Where(x => x.TotalRevenue >= (decimal)(avgRevenue * 1.5) ||
                               x.TicketsSold >= avgTickets * 1.5)
                    .OrderByDescending(x => x.TotalRevenue)
                    .Take(2)
                    .ToList();

                var poorPerformers = moviePerformance
                    .Where(x => x.TotalRevenue <= (decimal)(avgRevenue * 0.3) &&
                               x.TicketsSold <= avgTickets * 0.3)
                    .ToList();

                // SỬA: Giới hạn số lượng thay đổi cho chu kỳ 5 phút
                foreach (var poor in poorPerformers.Take(1)) // Chỉ xử lý 1 phim kém nhất
                {
                    var removed = await RemovePoorPerformingShowtimes(poor.MovieID, poor.MovieTitle);
                    result.RemovedCount += removed;
                    if (removed > 0)
                    {
                        result.Details.Add($"Xóa {removed} suất chiếu của '{poor.MovieTitle}' (doanh thu thấp trong 24h)");
                    }
                }

                foreach (var top in topPerformers.Take(1)) // Chỉ xử lý 1 phim tốt nhất
                {
                    var added = await AddShowtimesForTopMovie(top.MovieID, top.MovieTitle);
                    result.AddedCount += added;
                    if (added > 0)
                    {
                        result.Details.Add($"Thêm {added} suất chiếu cho '{top.MovieTitle}' (doanh thu cao trong 24h)");
                    }
                }

                await _context.SaveChangesAsync();

                // SỬA: Thêm thông tin chu kỳ
                if (result.AddedCount == 0 && result.RemovedCount == 0)
                {
                    result.Details.Add("Không cần điều chỉnh suất chiếu trong chu kỳ này");
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Details.Add($"Lỗi trong quá trình tự động quản lý: {ex.Message}");
                return result;
            }
        }

        private async Task<int> RemovePoorPerformingShowtimes(int movieId, string movieTitle)
        {
            var now = DateTime.Now;

            // SỬA: Chỉ xóa những suất chiếu ít nhất 6 giờ nữa và chưa có vé
            var showtimesToRemove = await _context.ShowTimes
                .Where(st => st.MovieID == movieId &&
                            st.StartTime > now.AddHours(6)) // Tăng từ 3 giờ lên 6 giờ
                .Include(st => st.Tickets)
                .Where(st => !st.Tickets.Any())
                .OrderBy(st => st.StartTime)
                .Take(1) // SỬA: Giảm từ 2 xuống 1 để ít aggressive hơn
                .ToListAsync();

            if (showtimesToRemove.Any())
            {
                _context.ShowTimes.RemoveRange(showtimesToRemove);
            }

            return showtimesToRemove.Count;
        }

        private async Task<int> AddShowtimesForTopMovie(int movieId, string movieTitle)
        {
            var movie = await _context.Movies.FindAsync(movieId);
            if (movie == null) return 0;

            var now = DateTime.Now;
            var addedCount = 0;

            // SỬA: Điều chỉnh thời gian thêm suất chiếu cho chu kỳ 5 phút
            var targetTimes = new[]
            {
                now.AddHours(8).Date.AddHours(19),  // Ngày mai 7pm
                now.AddHours(32).Date.AddHours(21)  // Ngày kia 9pm
            };

            foreach (var targetTime in targetTimes.Take(1)) // SỬA: Chỉ thêm 1 suất thay vì 2
            {
                var bestAuditorium = await FindAvailableAuditorium(targetTime, movie.DurationMinutes);
                if (bestAuditorium != null)
                {
                    var newShowtime = new ShowTime
                    {
                        MovieID = movieId,
                        AuditoriumID = bestAuditorium.ID,
                        StartTime = targetTime,
                        DurationMinutes = movie.DurationMinutes,
                        SubtitleLanguageID = 1,
                        Is3D = false,
                        Price = GetOptimalPrice(movieId)
                    };

                    _context.ShowTimes.Add(newShowtime);
                    addedCount++;
                }
            }

            return addedCount;
        }

        private async Task<Auditorium> FindAvailableAuditorium(DateTime targetTime, int duration)
        {
            var auditoriums = await _context.Auditoriums.ToListAsync();

            foreach (var auditorium in auditoriums)
            {
                var hasConflict = await _context.ShowTimes
                    .AnyAsync(st => st.AuditoriumID == auditorium.ID &&
                                   targetTime < st.StartTime.AddMinutes(st.DurationMinutes + 30) &&
                                   targetTime.AddMinutes(duration + 30) > st.StartTime);

                if (!hasConflict)
                {
                    return auditorium;
                }
            }

            return null;
        }

        private decimal GetOptimalPrice(int movieId)
        {
            var avgPrice = _context.ShowTimes
                .Where(st => st.MovieID == movieId)
                .Select(st => st.Price)
                .DefaultIfEmpty(10.0m)
                .Average();

            // SỬA: Giữ giá ổn định hơn cho chu kỳ ngắn
            return Math.Max(5.0m, Math.Min(20.0m, avgPrice * 1.02m)); // Tăng chỉ 2% thay vì 5%
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} phút trước";
            else if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} giờ trước";
            else if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} ngày trước";
            else
                return dateTime.ToString("dd/MM/yyyy");
        }
    }

    public class SimpleAutoResult
    {
        public int AddedCount { get; set; } = 0;
        public int RemovedCount { get; set; } = 0;
        public List<string> Details { get; set; } = new List<string>();
    }
}