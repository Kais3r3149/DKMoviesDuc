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

                // Calculate total revenue from tickets only
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
                // Return safe fallback on error
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

        public IActionResult Home()
        {
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> MovieDashboard()
        {
            try
            {
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

                // Check if we have any tickets
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

                model.TopMovies = scored;
                model.AverageRating = scored.Any() ? scored.Average(m => m.AvgRating) : 0;

                return View(model);
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

                // Check if username is already taken
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

                // Check if email is already taken
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

        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View(new ChangePasswordViewModel());
        }

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

        [HttpGet]
        public async Task<JsonResult> GetTop5MoviesRealTime()
        {
            try
            {
                var endDate = DateTime.Now;
                var startDate = endDate.AddDays(-7); // Last 7 days

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

        // ===== SIMPLE AUTO SHOWTIME MANAGEMENT =====

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> AutoManageShowtimes()
        {
            try
            {
                var result = await PerformSimpleAutoManagement();

                // Actually save the changes to database
                if (result.AddedCount > 0 || result.RemovedCount > 0)
                {
                    await _context.SaveChangesAsync();
                    result.Details.Add($"💾 Đã lưu {result.AddedCount + result.RemovedCount} thay đổi vào database");
                }

                return Json(new
                {
                    success = true,
                    message = "Quản lý suất chiếu tự động hoàn thành",
                    addedCount = result.AddedCount,
                    removedCount = result.RemovedCount,
                    details = result.Details,
                    totalChanges = result.AddedCount + result.RemovedCount
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    error = "Lỗi khi quản lý suất chiếu tự động",
                    details = ex.Message
                });
            }
        }

        private async Task<SimpleAutoResult> PerformSimpleAutoManagement()
        {
            var result = new SimpleAutoResult();
            var now = DateTime.Now;

            try
            {
                result.Details.Add($"Bắt đầu quản lý tự động lúc {now:HH:mm:ss dd/MM/yyyy}");

                // Check basic requirements
                var hasMovies = await _context.Movies.AnyAsync();
                if (!hasMovies)
                {
                    result.Details.Add("❌ Không có phim nào trong hệ thống");
                    return result;
                }

                var hasAuditoriums = await _context.Auditoriums.AnyAsync();
                if (!hasAuditoriums)
                {
                    result.Details.Add("❌ Không có phòng chiếu nào trong hệ thống");
                    return result;
                }

                // Get performance data if available
                var performanceData = await GetSimplePerformanceData();
                result.Details.Add($"📊 Phân tích dữ liệu: {performanceData.Count} phim có dữ liệu");

                // Identify top and poor performers
                var topMovies = performanceData
                    .OrderByDescending(p => p.TotalRevenue)
                    .ThenByDescending(p => p.TicketsSold)
                    .Take(3)
                    .ToList();

                var poorMovies = performanceData
                    .Where(p => p.TotalRevenue > 0) // Only movies with some sales
                    .OrderBy(p => p.TotalRevenue)
                    .ThenBy(p => p.TicketsSold)
                    .Take(2)
                    .ToList();

                // If no performance data, use all active movies
                if (!performanceData.Any())
                {
                    var activeMovies = await _context.Movies
                        .Where(m => m.ReleaseDate <= now)
                        .Take(5)
                        .ToListAsync();

                    foreach (var movie in activeMovies)
                    {
                        topMovies.Add(new SimpleMoviePerformance
                        {
                            MovieID = movie.ID,
                            MovieTitle = movie.Title,
                            TicketsSold = 0,
                            TotalRevenue = 0
                        });
                    }
                    result.Details.Add("ℹ️ Sử dụng tất cả phim đang chiếu do chưa có dữ liệu bán vé");
                }

                result.Details.Add($"🏆 Top phim: {topMovies.Count} phim");
                result.Details.Add($"📉 Phim kém: {poorMovies.Count} phim");

                // Add showtimes for top movies
                foreach (var movie in topMovies)
                {
                    var added = await AddSimpleShowtimes(movie.MovieID, movie.MovieTitle);
                    result.AddedCount += added;
                    if (added > 0)
                    {
                        result.Details.Add($"➕ Thêm {added} suất chiếu cho '{movie.MovieTitle}'");
                    }
                }

                // Remove poor performing showtimes
                foreach (var movie in poorMovies)
                {
                    var removed = await RemoveSimpleShowtimes(movie.MovieID, movie.MovieTitle);
                    result.RemovedCount += removed;
                    if (removed > 0)
                    {
                        result.Details.Add($"➖ Xóa {removed} suất chiếu cho '{movie.MovieTitle}'");
                    }
                }

                result.Details.Add($"📊 Tổng kết: +{result.AddedCount} thêm, -{result.RemovedCount} xóa");

                // Note: SaveChanges will be called in the main method
                return result;
            }
            catch (Exception ex)
            {
                result.Details.Add($"❌ Lỗi: {ex.Message}");
                throw;
            }
        }

        private async Task<List<SimpleMoviePerformance>> GetSimplePerformanceData()
        {
            try
            {
                var oneWeekAgo = DateTime.Now.AddDays(-7);

                var ticketData = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Movie)
                    .Where(t => t.PurchaseTime >= oneWeekAgo &&
                               t.ShowTime != null &&
                               t.ShowTime.Movie != null)
                    .GroupBy(t => t.ShowTime.MovieID)
                    .Select(g => new SimpleMoviePerformance
                    {
                        MovieID = g.Key,
                        MovieTitle = g.First().ShowTime.Movie.Title ?? "Unknown",
                        TicketsSold = g.Count(),
                        TotalRevenue = g.Sum(t => t.ShowTime.Price)
                    })
                    .ToListAsync();

                return ticketData;
            }
            catch (Exception ex)
            {
                return new List<SimpleMoviePerformance>();
            }
        }

        private async Task<int> AddSimpleShowtimes(int movieId, string movieTitle)
        {
            try
            {
                var movie = await _context.Movies.FindAsync(movieId);
                if (movie == null) return 0;

                var now = DateTime.Now;
                var addedCount = 0;

                // Popular time slots
                var popularHours = new[] { 14, 16, 18, 20, 22 };

                // Add showtimes for next 3 days
                for (int day = 1; day <= 3; day++)
                {
                    var targetDate = now.AddDays(day).Date;

                    // Try to add 2 showtimes per day
                    foreach (var hour in popularHours.Take(2))
                    {
                        var targetTime = targetDate.AddHours(hour);

                        // Skip if time has passed
                        if (targetTime <= now.AddHours(2)) continue;

                        // Find available auditorium
                        var auditorium = await FindAvailableAuditorium(targetTime, movie.DurationMinutes);
                        if (auditorium != null)
                        {
                            // Check if showtime already exists
                            var exists = await _context.ShowTimes
                                .AnyAsync(st => st.MovieID == movieId &&
                                               st.AuditoriumID == auditorium.ID &&
                                               st.StartTime == targetTime);

                            if (!exists)
                            {
                                var newShowtime = new ShowTime
                                {
                                    MovieID = movieId,
                                    AuditoriumID = auditorium.ID,
                                    StartTime = targetTime,
                                    DurationMinutes = movie.DurationMinutes,
                                    SubtitleLanguageID = 1,
                                    Is3D = false,
                                    Price = GetSimplePrice(hour)
                                };

                                _context.ShowTimes.Add(newShowtime);
                                addedCount++;

                                // Limit to prevent too many showtimes
                                if (addedCount >= 4) break;
                            }
                        }
                    }
                    if (addedCount >= 4) break;
                }

                return addedCount;
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        private async Task<int> RemoveSimpleShowtimes(int movieId, string movieTitle)
        {
            try
            {
                var now = DateTime.Now;
                var cutoffTime = now.AddHours(6); // Don't remove showtimes starting within 6 hours

                // Find showtimes to remove (future showtimes with no tickets sold)
                var showtimesToRemove = await _context.ShowTimes
                    .Include(st => st.Tickets)
                    .Where(st => st.MovieID == movieId &&
                                st.StartTime > cutoffTime &&
                                st.StartTime <= now.AddDays(2) &&
                                !st.Tickets.Any()) // No tickets sold
                    .OrderBy(st => st.StartTime)
                    .Take(2) // Limit removal
                    .ToListAsync();

                if (showtimesToRemove.Any())
                {
                    _context.ShowTimes.RemoveRange(showtimesToRemove);
                    return showtimesToRemove.Count;
                }

                return 0;
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        private async Task<Auditorium> FindAvailableAuditorium(DateTime targetTime, int duration)
        {
            try
            {
                var auditoriums = await _context.Auditoriums
                    .OrderByDescending(a => a.Capacity) // Prefer larger auditoriums
                    .ToListAsync();

                foreach (var auditorium in auditoriums)
                {
                    // Check for conflicts (including buffer time)
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
            catch (Exception ex)
            {
                return null;
            }
        }

        private decimal GetSimplePrice(int hour)
        {
            // Simple time-based pricing
            return hour switch
            {
                >= 19 and <= 21 => 15.0m, // Prime time
                >= 22 or <= 6 => 10.0m,   // Late/early
                >= 14 and <= 18 => 12.0m, // Afternoon
                _ => 11.0m                 // Default
            };
        }

        private async Task<string> SaveProfileImage(IFormFile profileImage)
        {
            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "profiles");
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = Guid.NewGuid().ToString() + "_" + profileImage.FileName;
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await profileImage.CopyToAsync(fileStream);
            }

            return "/images/profiles/" + uniqueFileName;
        }

        private void DeleteOldImage(string imagePath)
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

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }
    }

    // ===== SUPPORTING CLASSES FOR SIMPLE AUTO MANAGEMENT =====

    public class SimpleAutoResult
    {
        public int AddedCount { get; set; }
        public int RemovedCount { get; set; }
        public List<string> Details { get; set; } = new List<string>();
    }

    public class SimpleMoviePerformance
    {
        public int MovieID { get; set; }
        public string MovieTitle { get; set; }
        public int TicketsSold { get; set; }
        public decimal TotalRevenue { get; set; }
    }
}