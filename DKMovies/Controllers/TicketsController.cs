using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

public class TicketsController : Controller
{
    private readonly ApplicationDbContext _context;

    public TicketsController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: Tickets/OrderTicket/5
    public async Task<IActionResult> OrderTicket(int? id, string? search, string? date, int? theaterId)
    {
        if (id == null || id <= 0)
        {
            return NotFound("Movie ID is invalid.");
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
                return NotFound("Movie not found.");
            }

            var now = DateTime.Now;

            // Query toàn bộ showtime của phim trong tương lai
            var allShowtimes = await _context.ShowTimes
                .Include(s => s.Auditorium)
                    .ThenInclude(a => a.Theater)
                .Include(s => s.SubtitleLanguage)
                .Where(s => s.MovieID == id.Value && s.StartTime >= now)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            if (!allShowtimes.Any())
            {
                TempData["ToastError"] = "Phim này hiện chưa có suất chiếu.";
                return RedirectToAction("Details", "MoviesList", new { id });
            }

            // Xử lý ngày được chọn (fallback nếu không hợp lệ)
            DateTime selectedDate;
            if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out selectedDate))
            {
                // Valid date
            }
            else
            {
                selectedDate = allShowtimes.First().StartTime.Date;
            }

            // Lọc showtime theo ngày đã chọn
            var filteredShowtimes = allShowtimes
                .Where(s => s.StartTime.Date == selectedDate.Date)
                .ToList();

            // Áp dụng các filter thêm nếu có
            if (!string.IsNullOrWhiteSpace(search))
            {
                filteredShowtimes = filteredShowtimes
                    .Where(s => s.Auditorium.Theater.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || s.Auditorium.Theater.Location.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (theaterId.HasValue && theaterId > 0)
            {
                filteredShowtimes = filteredShowtimes
                    .Where(s => s.Auditorium.TheaterID == theaterId.Value)
                    .ToList();
            }

            // Lấy danh sách rạp dùng cho bộ lọc
            var availableTheaters = allShowtimes
                .Select(s => s.Auditorium.Theater)
                .Distinct()
                .OrderBy(t => t.Name)
                .ToList();

            ViewData["Movie"] = movie;
            ViewData["Search"] = search;
            ViewData["Date"] = selectedDate.ToString("yyyy-MM-dd");
            ViewData["SelectedTheaterId"] = theaterId;
            ViewData["AvailableTheaters"] = availableTheaters;

            return View("OrderTicket", filteredShowtimes);
        }
        catch
        {
            TempData["ToastError"] = "Có lỗi xảy ra khi tải thông tin đặt vé. Vui lòng thử lại.";
            return RedirectToAction("Details", "MoviesList", new { id });
        }
    }


    // GET: Tickets/OrderTicketDetails/5 (id = ShowTimeID)
    public async Task<IActionResult> OrderTicketDetails(int? id)
    {
        if (id == null || id <= 0)
        {
            return NotFound("Showtime ID is invalid.");
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
                return RedirectToAction("Index", "MoviesList");
            }

            // Check if showtime is still valid (not started yet)
            if (showtime.StartTime <= DateTime.Now)
            {
                TempData["ToastError"] = "Suất chiếu này đã bắt đầu hoặc đã kết thúc.";
                return RedirectToAction("OrderTicket", new { id = showtime.MovieID });
            }

            if (showtime.Auditorium?.Seats == null)
            {
                TempData["ToastError"] = "Thông tin phòng chiếu không đầy đủ.";
                return RedirectToAction("OrderTicket", new { id = showtime.MovieID });
            }

            var seats = showtime.Auditorium.Seats.OrderBy(s => s.RowLabel).ThenBy(s => s.SeatNumber).ToList();

            // Get taken seat IDs for this showtime (only confirmed tickets)
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
            return RedirectToAction("Index", "MoviesList");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "User")]
    public async Task<IActionResult> ConfirmOrder(int ShowTimeID, List<int> SelectedSeats)
    {
        // Validate input
        if (ShowTimeID <= 0)
        {
            TempData["ToastError"] = "Thông tin suất chiếu không hợp lệ.";
            return RedirectToAction("Index", "MoviesList");
        }

        if (SelectedSeats == null || !SelectedSeats.Any())
        {
            TempData["ToastError"] = "Vui lòng chọn ít nhất một ghế.";
            return RedirectToAction("OrderTicketDetails", new { id = ShowTimeID });
        }

        if (SelectedSeats.Count > 10) // Reasonable limit
        {
            TempData["ToastError"] = "Chỉ được đặt tối đa 10 ghế trong một lần.";
            return RedirectToAction("OrderTicketDetails", new { id = ShowTimeID });
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
                return RedirectToAction("Index", "MoviesList");
            }

            // Check if showtime is still bookable
            if (showTime.StartTime <= DateTime.Now.AddMinutes(30)) // 30 minutes before showtime
            {
                TempData["ToastError"] = "Không thể đặt vé trong vòng 30 phút trước giờ chiếu.";
                return RedirectToAction("OrderTicket", new { id = showTime.MovieID });
            }

            // Validate selected seats exist in the auditorium
            var validSeats = await _context.Seats
                .Where(s => s.AuditoriumID == showTime.AuditoriumID &&
                           SelectedSeats.Contains(s.ID))
                .ToListAsync();

            if (validSeats.Count != SelectedSeats.Count)
            {
                TempData["ToastError"] = "Một số ghế được chọn không hợp lệ.";
                return RedirectToAction("OrderTicketDetails", new { id = ShowTimeID });
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
                return RedirectToAction("OrderTicketDetails", new { id = ShowTimeID });
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
                return RedirectToAction("OrderConfirmation", new { ticketId = ticket.ID });
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
            return RedirectToAction("OrderTicketDetails", new { id = ShowTimeID });
        }
    }

    // GET: Tickets/OrderConfirmation/5
    public async Task<IActionResult> OrderConfirmation(int? ticketId)
    {
        if (ticketId == null || ticketId <= 0)
        {
            return NotFound("Ticket ID is invalid.");
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
                return NotFound("Không tìm thấy vé.");
            }

            // Verify ownership if user is authenticated
            if (User.Identity.IsAuthenticated)
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdStr, out int userId) && ticket.UserID != userId && !User.IsInRole("Admin"))
                {
                    return Forbid("Bạn không có quyền xem vé này.");
                }
            }

            return View(ticket);
        }
        catch (Exception ex)
        {
            // Log error
            TempData["ToastError"] = "Có lỗi xảy ra khi tải thông tin vé.";
            return RedirectToAction("Index", "MoviesList");
        }
    }

    // GET: Tickets/UserTickets
    [Authorize(Roles = "User")]
    public async Task<IActionResult> UserTickets(string? search, string? status, string? sort, int page = 1)
    {
        try
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
            {
                TempData["ToastError"] = "Phiên đăng nhập không hợp lệ.";
                return RedirectToAction("Login", "Account");
            }

            const int pageSize = 10;
            var query = _context.Tickets
                .Where(t => t.UserID == userId)
                .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Movie)
                .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Auditorium)
                        .ThenInclude(a => a.Theater)
                .Include(t => t.TicketSeats)
                    .ThenInclude(ts => ts.Seat)
                .AsQueryable();

            // Search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchTerm = search.Trim().ToLower();
                query = query.Where(t =>
                    t.ShowTime.Movie.Title.ToLower().Contains(searchTerm) ||
                    t.ShowTime.Auditorium.Theater.Name.ToLower().Contains(searchTerm) ||
                    t.ShowTime.Auditorium.Theater.Location.ToLower().Contains(searchTerm));
            }

            // Status filter
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse(status.ToUpper(), out TicketStatus parsedStatus))
            {
                query = query.Where(t => t.Status == parsedStatus);
            }

            // Sorting
            query = sort?.ToLower() switch
            {
                "date_asc" => query.OrderBy(t => t.PurchaseTime),
                "date_desc" => query.OrderByDescending(t => t.PurchaseTime),
                "showtime_asc" => query.OrderBy(t => t.ShowTime.StartTime),
                "showtime_desc" => query.OrderByDescending(t => t.ShowTime.StartTime),
                "price_asc" => query.OrderBy(t => t.ShowTime.Price),
                "price_desc" => query.OrderByDescending(t => t.ShowTime.Price),
                _ => query.OrderByDescending(t => t.PurchaseTime)
            };

            // Pagination
            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var tickets = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewData["Search"] = search;
            ViewData["Status"] = status;
            ViewData["Sort"] = sort;
            ViewData["CurrentPage"] = page;
            ViewData["TotalPages"] = totalPages;
            ViewData["TotalCount"] = totalCount;

            return View(tickets);
        }
        catch (Exception ex)
        {
            // Log error
            TempData["ToastError"] = "Có lỗi xảy ra khi tải danh sách vé.";
            return View(new List<Ticket>());
        }
    }

    // POST: Tickets/CancelTicket/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "User")]
    public async Task<IActionResult> CancelTicket(int ticketId)
    {
        if (ticketId <= 0)
        {
            TempData["ToastError"] = "ID vé không hợp lệ.";
            return RedirectToAction("UserTickets");
        }

        try
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
            {
                TempData["ToastError"] = "Phiên đăng nhập không hợp lệ.";
                return RedirectToAction("Login", "Account");
            }

            var ticket = await _context.Tickets
                .Include(t => t.ShowTime)
                .FirstOrDefaultAsync(t => t.ID == ticketId && t.UserID == userId);

            if (ticket == null)
            {
                TempData["ToastError"] = "Không tìm thấy vé hoặc bạn không có quyền hủy vé này.";
                return RedirectToAction("UserTickets");
            }

            // Only pending tickets can be cancelled
            if (ticket.Status != TicketStatus.PENDING)
            {
                TempData["ToastError"] = "Chỉ có thể hủy vé ở trạng thái chờ xác nhận.";
                return RedirectToAction("UserTickets");
            }

            // Cannot cancel if showtime is too close (e.g., within 2 hours)
            if (ticket.ShowTime.StartTime <= DateTime.Now.AddHours(2))
            {
                TempData["ToastError"] = "Không thể hủy vé trong vòng 2 tiếng trước giờ chiếu.";
                return RedirectToAction("UserTickets");
            }

            ticket.Status = TicketStatus.CANCELLED;
            _context.Update(ticket);
            await _context.SaveChangesAsync();

            TempData["ToastSuccess"] = "Vé đã được hủy thành công.";
            return RedirectToAction("UserTickets");
        }
        catch (Exception ex)
        {
            // Log error
            TempData["ToastError"] = "Có lỗi xảy ra khi hủy vé. Vui lòng thử lại.";
            return RedirectToAction("UserTickets");
        }
    }

    // POST: Tickets/ConfirmTicket/5 (for admin use)
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ConfirmTicket(int ticketId)
    {
        try
        {
            var ticket = await _context.Tickets.FindAsync(ticketId);
            if (ticket == null)
            {
                TempData["ToastError"] = "Không tìm thấy vé.";
                return NotFound();
            }

            if (ticket.Status != TicketStatus.PENDING)
            {
                TempData["ToastError"] = "Chỉ có thể xác nhận vé ở trạng thái chờ.";
                return BadRequest();
            }

            ticket.Status = TicketStatus.CONFIRMED;
            _context.Update(ticket);
            await _context.SaveChangesAsync();

            TempData["ToastSuccess"] = "Vé đã được xác nhận thành công.";
            return Ok();
        }
        catch (Exception ex)
        {
            // Log error
            TempData["ToastError"] = "Có lỗi xảy ra khi xác nhận vé.";
            return StatusCode(500);
        }
    }
}