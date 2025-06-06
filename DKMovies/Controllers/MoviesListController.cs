﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace DKMovies.Controllers
{
    public class MoviesListController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const int PageSize = 30;

        public MoviesListController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: MoviesList
        public async Task<IActionResult> Index(int page = 1)
        {
            // Validate page number
            if (page < 1) page = 1;

            var totalMovies = await _context.Movies.CountAsync();
            var totalPages = (int)Math.Ceiling(totalMovies / (double)PageSize);

            // Ensure page doesn't exceed total pages
            if (totalPages > 0 && page > totalPages) page = totalPages;

            var movies = await _context.Movies
                .Include(m => m.Country)
                .Include(m => m.Language)
                .Include(m => m.Rating)
                .Include(m => m.Director)
                .Include(m => m.MovieGenres)
                    .ThenInclude(mg => mg.Genre)
                .OrderBy(m => m.Title)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            var movieIds = movies.Select(m => m.ID).ToList();

            // Only get ratings if there are movies
            var avgRatings = new Dictionary<int, double>();
            if (movieIds.Any())
            {
                var ratingsQuery = await _context.Reviews
                    .Where(r => movieIds.Contains(r.MovieID) && r.IsApproved)
                    .GroupBy(r => r.MovieID)
                    .Select(g => new { MovieID = g.Key, AvgRating = g.Average(r => r.Rating) })
                    .ToListAsync();

                avgRatings = ratingsQuery.ToDictionary(x => x.MovieID, x => x.AvgRating);
            }

            ViewData["AverageRatings"] = avgRatings;
            ViewData["CurrentPage"] = page;
            ViewData["TotalPages"] = totalPages;
            ViewData["Title"] = "Tất cả phim";

            return View(movies);
        }

        // GET: MoviesList/NowShowing?date=yyyy-MM-dd
        public async Task<IActionResult> NowShowing(DateTime? date)
        {
            if (date == null)
            {
                // Không có ngày truyền vào → lấy ngày chiếu gần nhất
                date = await _context.ShowTimes
                    .Where(s => s.StartTime >= DateTime.Today)
                    .OrderBy(s => s.StartTime)
                    .Select(s => s.StartTime.Date)
                    .FirstOrDefaultAsync();

                if (date == default)
                {
                    ViewData["Title"] = "Hiện không có phim nào đang chiếu.";
                    ViewData["AverageRatings"] = new Dictionary<int, double>();
                    ViewData["CurrentPage"] = 1;
                    ViewData["TotalPages"] = 1;
                    return View("Index", new List<Movie>());
                }
            }

            var selectedDate = date.Value.Date;

            var movieIds = await _context.ShowTimes
                .Where(s => s.StartTime.Date == selectedDate)
                .Select(s => s.MovieID)
                .Distinct()
                .ToListAsync();

            var movies = await _context.Movies
                .Where(m => movieIds.Contains(m.ID))
                .Include(m => m.Country)
                .Include(m => m.Language)
                .Include(m => m.Rating)
                .Include(m => m.Director)
                .Include(m => m.MovieGenres)
                    .ThenInclude(mg => mg.Genre)
                .OrderBy(m => m.Title)
                .ToListAsync();

            // Get ratings for these movies
            var avgRatings = new Dictionary<int, double>();
            if (movieIds.Any())
            {
                var ratingsQuery = await _context.Reviews
                    .Where(r => movieIds.Contains(r.MovieID) && r.IsApproved)
                    .GroupBy(r => r.MovieID)
                    .Select(g => new { MovieID = g.Key, AvgRating = g.Average(r => r.Rating) })
                    .ToListAsync();

                avgRatings = ratingsQuery.ToDictionary(x => x.MovieID, x => x.AvgRating);
            }

            ViewData["AverageRatings"] = avgRatings;
            ViewData["Title"] = $"Phim chiếu ngày {selectedDate:dd/MM/yyyy}";
            ViewData["CurrentPage"] = 1;
            ViewData["TotalPages"] = 1;

            return View("Index", movies);
        }

        // GET: MoviesList/Search
        public async Task<IActionResult> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return RedirectToAction(nameof(Index));

            // Sanitize query
            query = query.Trim().ToLower();
            if (query.Length > 100) // Prevent overly long queries
            {
                query = query.Substring(0, 100);
            }

            var movies = await _context.Movies
                .Include(m => m.Country)
                .Include(m => m.Language)
                .Include(m => m.Rating)
                .Include(m => m.Director)
                .Include(m => m.MovieGenres)
                    .ThenInclude(mg => mg.Genre)
                .Where(m =>
                    EF.Functions.Like(m.Title.ToLower(), $"%{query}%") ||
                    EF.Functions.Like(m.Description.ToLower(), $"%{query}%") ||
                    (m.Director != null && EF.Functions.Like(m.Director.FullName.ToLower(), $"%{query}%")) ||
                    (m.Country != null && EF.Functions.Like(m.Country.Name.ToLower(), $"%{query}%")) ||
                    (m.Language != null && EF.Functions.Like(m.Language.Name.ToLower(), $"%{query}%")) ||
                    m.MovieGenres.Any(mg => EF.Functions.Like(mg.Genre.Name.ToLower(), $"%{query}%"))
                )
                .Take(100) // Limit search results to prevent performance issues
                .ToListAsync();

            // Get ratings for search results
            var avgRatings = new Dictionary<int, double>();
            if (movies.Any())
            {
                var movieIds = movies.Select(m => m.ID).ToList();
                var ratingsQuery = await _context.Reviews
                    .Where(r => movieIds.Contains(r.MovieID) && r.IsApproved)
                    .GroupBy(r => r.MovieID)
                    .Select(g => new { MovieID = g.Key, AvgRating = g.Average(r => r.Rating) })
                    .ToListAsync();

                avgRatings = ratingsQuery.ToDictionary(x => x.MovieID, x => x.AvgRating);
            }

            ViewData["AverageRatings"] = avgRatings;
            ViewData["Title"] = $"Kết quả tìm kiếm cho \"{query}\" ({movies.Count} kết quả)";
            ViewData["CurrentPage"] = 1;
            ViewData["TotalPages"] = 1;

            return View("Index", movies);
        }

        // GET: MoviesList/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || id <= 0)
                return NotFound();

            var movie = await _context.Movies
                .Include(m => m.Country)
                .Include(m => m.Language)
                .Include(m => m.Rating)
                .Include(m => m.Director)
                .Include(m => m.MovieGenres)
                    .ThenInclude(mg => mg.Genre)
                .Include(m => m.Reviews.Where(r => r.IsApproved)) // Only load approved reviews
                    .ThenInclude(r => r.User)
                .FirstOrDefaultAsync(m => m.ID == id);

            if (movie == null)
                return NotFound();

            // Calculate average rating from approved reviews only
            var approvedReviews = movie.Reviews.Where(r => r.IsApproved).ToList();
            var averageRating = approvedReviews.Any() ? approvedReviews.Average(r => r.Rating) : 0;
            ViewData["AverageRating"] = averageRating;

            // Check user-specific data only if authenticated
            ViewData["HasUserReviewed"] = false;
            ViewData["IsInWatchlist"] = false;

            if (User.Identity.IsAuthenticated)
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out int userId))
                {
                    // Verify user exists in database before checking reviews/watchlist
                    var userExists = await _context.Users.AnyAsync(u => u.ID == userId);
                    if (userExists)
                    {
                        var hasReviewed = await _context.Reviews
                            .AnyAsync(r => r.MovieID == movie.ID && r.UserID == userId);
                        ViewData["HasUserReviewed"] = hasReviewed;

                        var isInWatchlist = await _context.WatchlistItems
                            .AnyAsync(w => w.MovieID == movie.ID && w.UserID == userId);
                        ViewData["IsInWatchlist"] = isInWatchlist;
                    }
                }
            }

            return View(movie);
        }

        // ============================================
        // TICKET BOOKING METHODS
        // ============================================

        // GET: MoviesList/OrderTicket/5
        public async Task<IActionResult> OrderTicket(int? id, string? search, string? date, int? theaterId)
        {
            if (id == null || id <= 0)
            {
                TempData["ToastError"] = "ID phim không hợp lệ.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var movie = await _context.Movies
                    .Include(m => m.Rating)
                    .Include(m => m.Language)
                    .Include(m => m.Country)
                    .FirstOrDefaultAsync(m => m.ID == id.Value);

                if (movie == null)
                {
                    TempData["ToastError"] = "Không tìm thấy phim.";
                    return RedirectToAction(nameof(Index));
                }

                var now = DateTime.Now;

                // Get all showtimes for this movie in the future
                var query = _context.ShowTimes
                    .Include(s => s.Auditorium)
                        .ThenInclude(a => a.Theater)
                    .Include(s => s.SubtitleLanguage)
                    .Where(s => s.MovieID == id.Value && s.StartTime >= now);

                // Apply filters if provided
                if (!string.IsNullOrWhiteSpace(search))
                {
                    query = query.Where(s => s.Auditorium.Theater.Name.Contains(search) ||
                                            s.Auditorium.Theater.Location.Contains(search));
                }

                if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out DateTime selectedDate))
                {
                    var startOfDay = selectedDate.Date;
                    var endOfDay = startOfDay.AddDays(1);
                    query = query.Where(s => s.StartTime >= startOfDay && s.StartTime < endOfDay);
                }

                if (theaterId.HasValue && theaterId > 0)
                {
                    query = query.Where(s => s.Auditorium.TheaterID == theaterId.Value);
                }

                var showtimes = await query
                    .OrderBy(s => s.StartTime)
                    .ThenBy(s => s.Auditorium.Theater.Name)
                    .ToListAsync();

                ViewData["Movie"] = movie;
                ViewData["Search"] = search;
                ViewData["Date"] = date;
                ViewData["SelectedTheaterId"] = theaterId;

                return View(showtimes);
            }
            catch (Exception ex)
            {
                // Log error
                TempData["ToastError"] = "Có lỗi xảy ra khi tải thông tin đặt vé. Vui lòng thử lại.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        // GET: MoviesList/OrderTicketDetails/5 (id = ShowTimeID)
        public async Task<IActionResult> OrderTicketDetails(int? id)
        {
            if (id == null || id <= 0)
            {
                TempData["ToastError"] = "ID suất chiếu không hợp lệ.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var showtime = await _context.ShowTimes
                    .Include(s => s.Movie)
                    .Include(s => s.Auditorium)
                        .ThenInclude(a => a.Seats)
                    .Include(s => s.Auditorium)
                        .ThenInclude(a => a.Theater)
                    .Include(s => s.SubtitleLanguage)
                    .FirstOrDefaultAsync(s => s.ID == id.Value);

                if (showtime == null)
                {
                    TempData["ToastError"] = "Không tìm thấy suất chiếu.";
                    return RedirectToAction(nameof(Index));
                }

                // Check if showtime is still valid (not started yet)
                if (showtime.StartTime <= DateTime.Now)
                {
                    TempData["ToastError"] = "Suất chiếu này đã bắt đầu hoặc đã kết thúc.";
                    return RedirectToAction(nameof(OrderTicket), new { id = showtime.MovieID });
                }

                if (showtime.Auditorium?.Seats == null)
                {
                    TempData["ToastError"] = "Thông tin phòng chiếu không đầy đủ.";
                    return RedirectToAction(nameof(OrderTicket), new { id = showtime.MovieID });
                }

                var seats = showtime.Auditorium.Seats.OrderBy(s => s.RowLabel).ThenBy(s => s.SeatNumber).ToList();

                // Get taken seat IDs for this showtime (only confirmed and pending tickets)
                var takenSeats = await _context.TicketSeats
                    .Include(ts => ts.Ticket)
                    .Where(ts => ts.Ticket.ShowTimeID == id.Value &&
                               ts.Ticket.Status != TicketStatus.CANCELLED)
                    .Select(ts => ts.SeatID)
                    .ToListAsync();

                ViewData["TakenSeats"] = takenSeats;
                ViewData["ShowTime"] = showtime;

                return View(seats);
            }
            catch (Exception ex)
            {
                // Log error
                TempData["ToastError"] = "Có lỗi xảy ra khi tải thông tin ghế ngồi.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: MoviesList/ConfirmOrder
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User")]
        public async Task<IActionResult> ConfirmOrder(int ShowTimeID, List<int> SelectedSeats)
        {
            // Validate input
            if (ShowTimeID <= 0)
            {
                TempData["ToastError"] = "Thông tin suất chiếu không hợp lệ.";
                return RedirectToAction(nameof(Index));
            }

            if (SelectedSeats == null || !SelectedSeats.Any())
            {
                TempData["ToastError"] = "Vui lòng chọn ít nhất một ghế.";
                return RedirectToAction(nameof(OrderTicketDetails), new { id = ShowTimeID });
            }

            if (SelectedSeats.Count > 10) // Reasonable limit
            {
                TempData["ToastError"] = "Chỉ được đặt tối đa 10 ghế trong một lần.";
                return RedirectToAction(nameof(OrderTicketDetails), new { id = ShowTimeID });
            }

            try
            {
                // Get user ID
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdStr, out int userId))
                {
                    TempData["ToastError"] = "Phiên đăng nhập không hợp lệ.";
                    return RedirectToAction("Login", "Account");
                }

                // Verify user exists
                var userExists = await _context.Users.AnyAsync(u => u.ID == userId);
                if (!userExists)
                {
                    TempData["ToastError"] = "Tài khoản người dùng không tồn tại.";
                    return RedirectToAction("Login", "Account");
                }

                // Get and validate showtime
                var showTime = await _context.ShowTimes
                    .Include(st => st.Movie)
                    .Include(st => st.Auditorium)
                        .ThenInclude(a => a.Theater)
                    .FirstOrDefaultAsync(st => st.ID == ShowTimeID);

                if (showTime == null)
                {
                    TempData["ToastError"] = "Không tìm thấy suất chiếu.";
                    return RedirectToAction(nameof(Index));
                }

                // Check if showtime is still bookable
                if (showTime.StartTime <= DateTime.Now.AddMinutes(30)) // 30 minutes before showtime
                {
                    TempData["ToastError"] = "Không thể đặt vé trong vòng 30 phút trước giờ chiếu.";
                    return RedirectToAction(nameof(OrderTicket), new { id = showTime.MovieID });
                }

                // Validate selected seats exist in the auditorium
                var validSeats = await _context.Seats
                    .Where(s => s.AuditoriumID == showTime.AuditoriumID &&
                               SelectedSeats.Contains(s.ID))
                    .ToListAsync();

                if (validSeats.Count != SelectedSeats.Count)
                {
                    TempData["ToastError"] = "Một số ghế được chọn không hợp lệ.";
                    return RedirectToAction(nameof(OrderTicketDetails), new { id = ShowTimeID });
                }

                // Check for already taken seats (race condition protection)
                var takenSeatIds = await _context.TicketSeats
                    .Include(ts => ts.Ticket)
                    .Where(ts => ts.Ticket.ShowTimeID == ShowTimeID &&
                               ts.Ticket.Status != TicketStatus.CANCELLED &&
                               SelectedSeats.Contains(ts.SeatID))
                    .Select(ts => ts.SeatID)
                    .ToListAsync();

                if (takenSeatIds.Any())
                {
                    var takenSeatNames = await _context.Seats
                        .Where(s => takenSeatIds.Contains(s.ID))
                        .Select(s => $"{s.RowLabel}{s.SeatNumber}")
                        .ToListAsync();

                    TempData["ToastError"] = $"Các ghế sau đã được đặt: {string.Join(", ", takenSeatNames)}. Vui lòng chọn ghế khác.";
                    return RedirectToAction(nameof(OrderTicketDetails), new { id = ShowTimeID });
                }

                // Create transaction to ensure data consistency
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Create ticket
                    var ticket = new Ticket
                    {
                        UserID = userId,
                        ShowTimeID = ShowTimeID,
                        PurchaseTime = DateTime.Now,
                        Status = TicketStatus.PENDING // Set initial status
                    };

                    _context.Tickets.Add(ticket);
                    await _context.SaveChangesAsync(); // Get ticket ID

                    // Create ticket seats
                    var ticketSeats = validSeats.Select(seat => new TicketSeat
                    {
                        TicketID = ticket.ID,
                        SeatID = seat.ID
                    }).ToList();

                    _context.TicketSeats.AddRange(ticketSeats);
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    TempData["ToastSuccess"] = "Đặt vé thành công! Vé của bạn đang ở trạng thái chờ xác nhận.";
                    return RedirectToAction(nameof(OrderConfirmation), new { ticketId = ticket.ID });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                // Log error
                TempData["ToastError"] = "Có lỗi xảy ra khi đặt vé. Vui lòng thử lại.";
                return RedirectToAction(nameof(OrderTicketDetails), new { id = ShowTimeID });
            }
        }

        // GET: MoviesList/OrderConfirmation/5
        public async Task<IActionResult> OrderConfirmation(int? ticketId)
        {
            if (ticketId == null || ticketId <= 0)
            {
                TempData["ToastError"] = "ID vé không hợp lệ.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var ticket = await _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Auditorium)
                            .ThenInclude(a => a.Theater)
                    .Include(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.ID == ticketId.Value);

                if (ticket == null)
                {
                    TempData["ToastError"] = "Không tìm thấy vé.";
                    return RedirectToAction(nameof(Index));
                }

                // Verify ownership if user is authenticated
                if (User.Identity.IsAuthenticated)
                {
                    var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (int.TryParse(userIdStr, out int userId) && ticket.UserID != userId && !User.IsInRole("Admin"))
                    {
                        TempData["ToastError"] = "Bạn không có quyền xem vé này.";
                        return RedirectToAction(nameof(Index));
                    }
                }

                return View(ticket);
            }
            catch (Exception ex)
            {
                // Log error
                TempData["ToastError"] = "Có lỗi xảy ra khi tải thông tin vé.";
                return RedirectToAction(nameof(Index));
            }
        }

        // Alternative method for TicketConfirmation if needed
        public async Task<IActionResult> TicketConfirmation(int? ticketId)
        {
            return await OrderConfirmation(ticketId);
        }
    }
}