using DKMovies.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DKMovies.Controllers
{
    public class ConcessionsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ConcessionsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Concessions/Menu?category=...
        public async Task<IActionResult> Menu(string? category)
        {
            try
            {
                // Lấy danh sách tất cả category khác nhau (chỉ từ items có sẵn)
                var allCategories = await _context.Concessions
                    .Where(c => c.IsActive &&
                           c.TheaterConcessions.Any(tc => tc.IsAvailable && tc.StockLeft > 0))
                    .Select(c => c.Category)
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Distinct()
                    .OrderBy(c => c)
                    .ToListAsync();

                ViewBag.Categories = allCategories ?? new List<string>();
                ViewBag.SelectedCategory = category?.Trim();

                // Base query cho concessions có sẵn
                var query = _context.Concessions
                    .Include(c => c.TheaterConcessions.Where(tc => tc.IsAvailable && tc.StockLeft > 0))
                        .ThenInclude(tc => tc.Theater)
                    .Where(c => c.IsActive &&
                           c.TheaterConcessions.Any(tc => tc.IsAvailable && tc.StockLeft > 0));

                // Apply category filter if provided
                if (!string.IsNullOrWhiteSpace(category))
                {
                    var trimmedCategory = category.Trim();
                    query = query.Where(c => !string.IsNullOrEmpty(c.Category) &&
                                           c.Category.Equals(trimmedCategory, StringComparison.OrdinalIgnoreCase));
                }

                var items = await query
                    .OrderBy(c => c.Category)
                    .ThenBy(c => c.Name)
                    .ToListAsync();

                return View(items ?? new List<Concession>());
            }
            catch (Exception ex)
            {
                // Log error (add your logging mechanism here)
                // _logger.LogError(ex, "Error occurred while loading concessions menu");

                // Return empty view with error message
                ViewBag.Categories = new List<string>();
                ViewBag.SelectedCategory = null;
                ViewBag.ErrorMessage = "Có lỗi xảy ra khi tải thực đơn. Vui lòng thử lại sau.";

                return View(new List<Concession>());
            }
        }

        // GET: /Concessions/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || id <= 0)
            {
                return NotFound("ID không hợp lệ.");
            }

            try
            {
                var item = await _context.Concessions
                    .Include(c => c.TheaterConcessions.Where(tc => tc.IsAvailable && tc.StockLeft > 0))
                        .ThenInclude(tc => tc.Theater)
                    .FirstOrDefaultAsync(c => c.ID == id && c.IsActive);

                if (item == null)
                {
                    return NotFound("Không tìm thấy món ăn này hoặc món ăn đã ngừng kinh doanh.");
                }

                // Check if user can purchase (has valid tickets)
                bool canPurchase = false;
                List<Ticket> userTickets = new List<Ticket>();

                if (User.Identity.IsAuthenticated)
                {
                    var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (!string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out int userId))
                    {
                        // Check if user exists
                        var userExists = await _context.Users.AnyAsync(u => u.ID == userId);
                        if (userExists)
                        {
                            // Get user's valid tickets (future showtimes with confirmed status)
                            userTickets = await _context.Tickets
                                .Include(t => t.ShowTime)
                                    .ThenInclude(st => st.Movie)
                                .Include(t => t.ShowTime)
                                    .ThenInclude(st => st.Auditorium)
                                        .ThenInclude(a => a.Theater)
                                .Where(t => t.UserID == userId &&
                                           t.ShowTime.StartTime > DateTime.Now &&
                                           t.Status == TicketStatus.CONFIRMED) // Use enum instead of string
                                .OrderBy(t => t.ShowTime.StartTime)
                                .ToListAsync();

                            // User can purchase if they have valid tickets at same theaters where concession is available
                            if (userTickets.Any() && item.TheaterConcessions.Any())
                            {
                                var userTheaterIds = userTickets
                                    .Select(t => t.ShowTime.Auditorium.TheaterID)
                                    .Distinct();
                                var concessionTheaterIds = item.TheaterConcessions
                                    .Select(tc => tc.TheaterID)
                                    .Distinct();
                                canPurchase = userTheaterIds.Intersect(concessionTheaterIds).Any();
                            }
                        }
                    }
                }

                ViewBag.CanPurchase = canPurchase;
                ViewBag.UserTickets = userTickets;

                return View(item);
            }
            catch (Exception ex)
            {
                // Log error
                // _logger.LogError(ex, "Error occurred while loading concession details for ID: {Id}", id);

                return NotFound("Có lỗi xảy ra khi tải chi tiết món ăn.");
            }
        }

        // GET: /Concessions/Search?q=...
        public async Task<IActionResult> Search(string? q)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return RedirectToAction(nameof(Menu));
            }

            try
            {
                var searchTerm = q.Trim().ToLower();

                var items = await _context.Concessions
                    .Include(c => c.TheaterConcessions.Where(tc => tc.IsAvailable && tc.StockLeft > 0))
                        .ThenInclude(tc => tc.Theater)
                    .Where(c => c.IsActive &&
                           c.TheaterConcessions.Any(tc => tc.IsAvailable && tc.StockLeft > 0) &&
                           (EF.Functions.Like(c.Name.ToLower(), $"%{searchTerm}%") ||
                            (!string.IsNullOrEmpty(c.Description) && EF.Functions.Like(c.Description.ToLower(), $"%{searchTerm}%")) ||
                            (!string.IsNullOrEmpty(c.Category) && EF.Functions.Like(c.Category.ToLower(), $"%{searchTerm}%"))))
                    .OrderBy(c => c.Name)
                    .Take(50) // Limit search results
                    .ToListAsync();

                ViewBag.Categories = new List<string>();
                ViewBag.SelectedCategory = null;
                ViewBag.SearchQuery = q;
                ViewData["Title"] = $"Kết quả tìm kiếm cho '{q}' ({items.Count} kết quả)";

                return View("Menu", items);
            }
            catch (Exception ex)
            {
                // Log error
                // _logger.LogError(ex, "Error occurred while searching concessions for query: {Query}", q);

                ViewBag.Categories = new List<string>();
                ViewBag.SelectedCategory = null;
                ViewBag.ErrorMessage = "Có lỗi xảy ra khi tìm kiếm. Vui lòng thử lại sau.";

                return View("Menu", new List<Concession>());
            }
        }

        // Helper method to check if user has valid tickets for specific theaters
        private async Task<bool> HasValidTicketsForTheaters(int userId, IEnumerable<int> theaterIds)
        {
            return await _context.Tickets
                .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Auditorium)
                .AnyAsync(t => t.UserID == userId &&
                              theaterIds.Contains(t.ShowTime.Auditorium.TheaterID) &&
                              t.ShowTime.StartTime > DateTime.Now &&
                              t.Status == TicketStatus.CONFIRMED);
        }

        // GET: /Concessions/Categories - API endpoint for categories
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            try
            {
                var categories = await _context.Concessions
                    .Where(c => c.IsActive &&
                           c.TheaterConcessions.Any(tc => tc.IsAvailable && tc.StockLeft > 0))
                    .Select(c => c.Category)
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Distinct()
                    .OrderBy(c => c)
                    .ToListAsync();

                return Json(categories ?? new List<string>());
            }
            catch (Exception ex)
            {
                // Log error
                return Json(new List<string>());
            }
        }

        // GET: /Concessions/GetAvailableTheaters/{concessionId}
        [HttpGet]
        public async Task<IActionResult> GetAvailableTheaters(int concessionId)
        {
            try
            {
                var theaters = await _context.TheaterConcession
                    .Include(tc => tc.Theater)
                    .Where(tc => tc.ConcessionID == concessionId &&
                               tc.IsAvailable &&
                               tc.StockLeft > 0)
                    .Select(tc => new {
                        tc.Theater.ID,
                        tc.Theater.Name,
                        tc.Theater.Location,
                        tc.Price,
                        tc.StockLeft
                    })
                    .ToListAsync();

                return Json(theaters);
            }
            catch (Exception ex)
            {
                // Log error
                return Json(new List<object>());
            }
        }

        // POST: /Concessions/CheckPurchaseEligibility
        [HttpPost]
        public async Task<IActionResult> CheckPurchaseEligibility(int concessionId)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Json(new { canPurchase = false, message = "Vui lòng đăng nhập để mua concession." });
            }

            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdStr, out int userId))
                {
                    return Json(new { canPurchase = false, message = "Không thể xác định thông tin người dùng." });
                }

                // Get concession theater IDs
                var concessionTheaterIds = await _context.TheaterConcession
                    .Where(tc => tc.ConcessionID == concessionId && tc.IsAvailable && tc.StockLeft > 0)
                    .Select(tc => tc.TheaterID)
                    .ToListAsync();

                if (!concessionTheaterIds.Any())
                {
                    return Json(new { canPurchase = false, message = "Concession này hiện không có sẵn." });
                }

                // Check if user has valid tickets at those theaters
                var hasValidTickets = await _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Auditorium)
                    .AnyAsync(t => t.UserID == userId &&
                                  concessionTheaterIds.Contains(t.ShowTime.Auditorium.TheaterID) &&
                                  t.ShowTime.StartTime > DateTime.Now &&
                                  t.Status == TicketStatus.CONFIRMED);

                if (!hasValidTickets)
                {
                    return Json(new
                    {
                        canPurchase = false,
                        message = "Bạn cần có vé xem phim tại rạp có bán concession này để có thể mua."
                    });
                }

                return Json(new { canPurchase = true, message = "Bạn có thể mua concession này." });
            }
            catch (Exception ex)
            {
                // Log error
                return Json(new { canPurchase = false, message = "Có lỗi xảy ra khi kiểm tra điều kiện mua hàng." });
            }
        }
    }
}