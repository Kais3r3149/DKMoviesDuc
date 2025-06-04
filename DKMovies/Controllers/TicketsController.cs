using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace DKMovies.Controllers
{
    public class TicketsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TicketsController> _logger;

        public TicketsController(ApplicationDbContext context, ILogger<TicketsController> logger)
        {
            _context = context;
            _logger = logger;
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

                // Xử lý ngày được chọn với validation
                DateTime selectedDate;
                if (!string.IsNullOrWhiteSpace(date) &&
                    DateTime.TryParse(date, out selectedDate) &&
                    selectedDate >= now.Date)
                {
                    // Valid future date
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
                    var searchLower = search.Trim().ToLower();
                    filteredShowtimes = filteredShowtimes
                        .Where(s => s.Auditorium.Theater.Name.ToLower().Contains(searchLower) ||
                                   s.Auditorium.Theater.Location.ToLower().Contains(searchLower))
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
                    .GroupBy(t => t.ID)
                    .Select(g => g.First())
                    .OrderBy(t => t.Name)
                    .ToList();

                ViewData["Movie"] = movie;
                ViewData["Search"] = search?.Trim();
                ViewData["Date"] = selectedDate.ToString("yyyy-MM-dd");
                ViewData["SelectedTheaterId"] = theaterId;
                ViewData["AvailableTheaters"] = availableTheaters;

                return View("OrderTicket", filteredShowtimes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OrderTicket for movie {MovieId}", id);
                TempData["ToastError"] = "Có lỗi xảy ra khi tải thông tin đặt vé. Vui lòng thử lại.";
                return RedirectToAction("Details", "MoviesList", new { id });
            }
        }

        // GET: Tickets/OrderTicketDetails/5 (id = ShowTimeID) - ALIAS for SeatSelection
        public async Task<IActionResult> OrderTicketDetails(int? id)
        {
            return await SeatSelection(id);
        }

        // ✅ Seat Selection - Main method for seat selection
        public async Task<IActionResult> SeatSelection(int? id)
        {
            if (id == null || id <= 0)
            {
                TempData["ToastError"] = "ID suất chiếu không hợp lệ.";
                return RedirectToAction("Index", "MoviesList");
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
                if (showtime.StartTime <= DateTime.Now.AddMinutes(30))
                {
                    TempData["ToastError"] = "Không thể đặt vé trong vòng 30 phút trước giờ chiếu.";
                    return RedirectToAction("OrderTicket", new { id = showtime.MovieID });
                }

                if (showtime.Auditorium?.Seats == null || !showtime.Auditorium.Seats.Any())
                {
                    TempData["ToastError"] = "Thông tin phòng chiếu không đầy đủ.";
                    return RedirectToAction("OrderTicket", new { id = showtime.MovieID });
                }

                var seats = showtime.Auditorium.Seats
                    .OrderBy(s => s.RowLabel)
                    .ThenBy(s => s.SeatNumber)
                    .ToList();

                // Get taken seat IDs for this showtime (only non-cancelled tickets)
                var takenSeats = await _context.TicketSeats
                    .Include(ts => ts.Ticket)
                    .Where(ts => ts.Ticket.ShowTimeID == id.Value &&
                               (ts.Ticket.Status == TicketStatus.CONFIRMED ||
                                ts.Ticket.Status == TicketStatus.PENDING ||
                                ts.Ticket.Status == TicketStatus.PAID))
                    .Select(ts => ts.SeatID)
                    .Distinct()
                    .ToListAsync();

                // Get available concessions for this theater
                var availableConcessions = await _context.TheaterConcessions
                    .Include(tc => tc.Concession)
                    .Where(tc => tc.TheaterID == showtime.Auditorium.TheaterID &&
                               tc.IsAvailable &&
                               tc.Concession.IsActive &&
                               tc.StockLeft > 0)
                    .OrderBy(tc => tc.Concession.Category)
                    .ThenBy(tc => tc.Concession.Name)
                    .ToListAsync();

                ViewData["TakenSeats"] = takenSeats;
                ViewData["ShowTime"] = showtime;
                ViewData["AvailableConcessions"] = availableConcessions;

                return View("OrderTicketDetails", seats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SeatSelection for showtime {ShowTimeId}", id);
                TempData["ToastError"] = "Có lỗi xảy ra khi tải thông tin ghế ngồi.";
                return RedirectToAction("Index", "MoviesList");
            }
        }

        // ✅ ENHANCED: Confirm Order with comprehensive debugging
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User")]
        public async Task<IActionResult> ConfirmOrder(
            int ShowTimeID,
            List<int> SelectedSeats,
            IFormCollection form)
        {
            try
            {
                // ✅ ENHANCED: Comprehensive input logging
                _logger.LogInformation("🔍 ConfirmOrder started with ShowTimeID: {ShowTimeID}, Seats: {SeatCount}",
                    ShowTimeID, SelectedSeats?.Count ?? 0);

                Console.WriteLine($"🔍 DEBUG ConfirmOrder Input:");
                Console.WriteLine($"   ShowTimeID: {ShowTimeID}");
                Console.WriteLine($"   SelectedSeats Count: {SelectedSeats?.Count ?? 0}");
                Console.WriteLine($"   SelectedSeats: [{string.Join(", ", SelectedSeats ?? new List<int>())}]");
                Console.WriteLine($"   Form Keys Count: {form.Keys.Count}");

                foreach (var key in form.Keys.Take(10)) // Limit to first 10 keys for readability
                {
                    Console.WriteLine($"   Form[{key}] = '{form[key]}'");
                }

                // ✅ ENHANCED: Input validation with detailed error tracking
                if (ShowTimeID <= 0)
                {
                    _logger.LogWarning("Invalid ShowTimeID: {ShowTimeID}", ShowTimeID);
                    Console.WriteLine($"❌ ERROR: Invalid ShowTimeID: {ShowTimeID}");
                    TempData["ToastError"] = "Thông tin suất chiếu không hợp lệ.";
                    return RedirectToAction("Index", "MoviesList");
                }

                if (SelectedSeats == null || !SelectedSeats.Any())
                {
                    _logger.LogWarning("No seats selected. SelectedSeats is null: {IsNull}", SelectedSeats == null);
                    Console.WriteLine($"❌ ERROR: No seats selected. SelectedSeats is null: {SelectedSeats == null}");
                    TempData["ToastError"] = "Vui lòng chọn ít nhất một ghế.";
                    return RedirectToAction("OrderTicketDetails", new { id = ShowTimeID });
                }

                if (SelectedSeats.Count > 10)
                {
                    _logger.LogWarning("Too many seats selected: {SeatCount}", SelectedSeats.Count);
                    Console.WriteLine($"❌ ERROR: Too many seats: {SelectedSeats.Count}");
                    TempData["ToastError"] = "Chỉ được đặt tối đa 10 ghế trong một lần.";
                    return RedirectToAction("OrderTicketDetails", new { id = ShowTimeID });
                }

                // Remove duplicates and invalid seat IDs
                var originalSeatCount = SelectedSeats.Count;
                SelectedSeats = SelectedSeats.Where(id => id > 0).Distinct().ToList();

                if (SelectedSeats.Count != originalSeatCount)
                {
                    _logger.LogWarning("Filtered seats from {OriginalCount} to {FilteredCount}",
                        originalSeatCount, SelectedSeats.Count);
                    Console.WriteLine($"⚠️ Filtered seats from {originalSeatCount} to {SelectedSeats.Count}");
                }

                Console.WriteLine($"✅ Cleaned SelectedSeats: [{string.Join(", ", SelectedSeats)}]");

                // ✅ ENHANCED: User validation with detailed logging
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                _logger.LogInformation("User authentication - UserIdStr: '{UserIdStr}', IsAuthenticated: {IsAuthenticated}",
                    userIdStr, User.Identity.IsAuthenticated);

                Console.WriteLine($"🔍 User authentication check:");
                Console.WriteLine($"   User.Identity.IsAuthenticated: {User.Identity.IsAuthenticated}");
                Console.WriteLine($"   User.Identity.Name: {User.Identity.Name}");
                Console.WriteLine($"   UserIdStr from claims: '{userIdStr}'");
                Console.WriteLine($"   Claims count: {User.Claims.Count()}");

                if (!int.TryParse(userIdStr, out int userId))
                {
                    _logger.LogError("Cannot parse user ID: '{UserIdStr}'", userIdStr);
                    Console.WriteLine($"❌ ERROR: Cannot parse user ID: '{userIdStr}'");
                    TempData["ToastError"] = "Phiên đăng nhập không hợp lệ.";
                    return RedirectToAction("Login", "Account");
                }

                Console.WriteLine($"✅ Parsed User ID: {userId}");

                var userExists = await _context.Users.AnyAsync(u => u.ID == userId);
                if (!userExists)
                {
                    _logger.LogError("User not found in database: {UserId}", userId);
                    Console.WriteLine($"❌ ERROR: User not found in database: {userId}");
                    TempData["ToastError"] = "Tài khoản người dùng không tồn tại.";
                    return RedirectToAction("Login", "Account");
                }

                Console.WriteLine($"✅ User exists in database: {userId}");

                // ✅ ENHANCED: Concession parsing with multiple fallback methods
                var concessions = ParseConcessions(form);
                _logger.LogInformation("Parsed {ConcessionCount} concessions", concessions.Count);

                Console.WriteLine($"✅ Final concessions count: {concessions.Count}");
                foreach (var item in concessions)
                {
                    Console.WriteLine($"   Concession {item.Key}: {item.Value} items");
                }

                // ✅ ENHANCED: ShowTime validation
                Console.WriteLine($"🔍 Getting showtime {ShowTimeID}...");
                var showTime = await _context.ShowTimes
                    .Include(st => st.Movie)
                    .Include(st => st.Auditorium)
                        .ThenInclude(a => a.Theater)
                    .FirstOrDefaultAsync(st => st.ID == ShowTimeID);

                if (showTime == null)
                {
                    _logger.LogError("Showtime not found: {ShowTimeID}", ShowTimeID);
                    Console.WriteLine($"❌ ERROR: Showtime not found: {ShowTimeID}");
                    TempData["ToastError"] = "Không tìm thấy suất chiếu.";
                    return RedirectToAction("Index", "MoviesList");
                }

                _logger.LogInformation("Found showtime: {MovieTitle} at {StartTime}",
                    showTime.Movie.Title, showTime.StartTime);
                Console.WriteLine($"✅ Found showtime: {showTime.Movie.Title} at {showTime.StartTime}");

                // Check if showtime is still bookable
                var timeCheck = DateTime.Now.AddMinutes(30);
                if (showTime.StartTime <= timeCheck)
                {
                    _logger.LogWarning("Showtime too close. StartTime: {StartTime}, Cutoff: {Cutoff}",
                        showTime.StartTime, timeCheck);
                    Console.WriteLine($"❌ ERROR: Showtime too close. StartTime: {showTime.StartTime}, Cutoff: {timeCheck}");
                    TempData["ToastError"] = "Không thể đặt vé trong vòng 30 phút trước giờ chiếu.";
                    return RedirectToAction("OrderTicket", new { id = showTime.MovieID });
                }

                // ✅ ENHANCED: Seat validation
                Console.WriteLine($"🔍 Validating seats for auditorium {showTime.AuditoriumID}...");
                var validSeats = await _context.Seats
                    .Where(s => s.AuditoriumID == showTime.AuditoriumID &&
                               SelectedSeats.Contains(s.ID))
                    .ToListAsync();

                _logger.LogInformation("Seat validation - Requested: {RequestedCount}, Valid: {ValidCount}",
                    SelectedSeats.Count, validSeats.Count);
                Console.WriteLine($"   Requested seats: {SelectedSeats.Count}");
                Console.WriteLine($"   Valid seats found: {validSeats.Count}");

                if (validSeats.Count != SelectedSeats.Count)
                {
                    var invalidSeats = SelectedSeats.Except(validSeats.Select(s => s.ID)).ToList();
                    _logger.LogWarning("Invalid seats: {InvalidSeats}", string.Join(", ", invalidSeats));
                    Console.WriteLine($"❌ ERROR: Seat validation failed");
                    Console.WriteLine($"   Invalid seat IDs: [{string.Join(", ", invalidSeats)}]");
                    TempData["ToastError"] = "Một số ghế được chọn không hợp lệ.";
                    return RedirectToAction("OrderTicketDetails", new { id = ShowTimeID });
                }

                // ✅ ENHANCED: Check for taken seats
                Console.WriteLine($"🔍 Checking for taken seats...");
                var takenSeatIds = await _context.TicketSeats
                    .Include(ts => ts.Ticket)
                    .Where(ts => ts.Ticket.ShowTimeID == ShowTimeID &&
                               (ts.Ticket.Status == TicketStatus.CONFIRMED ||
                                ts.Ticket.Status == TicketStatus.PENDING ||
                                ts.Ticket.Status == TicketStatus.PAID) &&
                               SelectedSeats.Contains(ts.SeatID))
                    .Select(ts => ts.SeatID)
                    .ToListAsync();

                if (takenSeatIds.Any())
                {
                    _logger.LogWarning("Some seats are taken: {TakenSeats}", string.Join(", ", takenSeatIds));
                    Console.WriteLine($"❌ ERROR: Some seats are taken: [{string.Join(", ", takenSeatIds)}]");

                    var takenSeatNames = await _context.Seats
                        .Where(s => takenSeatIds.Contains(s.ID))
                        .Select(s => $"{s.RowLabel}{s.SeatNumber}")
                        .ToListAsync();

                    TempData["ToastError"] = $"Các ghế sau đã được đặt: {string.Join(", ", takenSeatNames)}. Vui lòng chọn ghế khác.";
                    return RedirectToAction("OrderTicketDetails", new { id = ShowTimeID });
                }

                Console.WriteLine($"✅ No seats are taken");

                // ✅ ENHANCED: Process concessions
                var orderItems = await ProcessConcessions(concessions, showTime.Auditorium.TheaterID, ShowTimeID);
                var concessionTotal = orderItems.Sum(oi => oi.Quantity * oi.PriceAtPurchase);

                // ✅ Calculate totals
                decimal ticketTotal = validSeats.Count * showTime.Price;
                decimal totalPrice = ticketTotal + concessionTotal;

                _logger.LogInformation("Order totals - Tickets: {TicketTotal}, Concessions: {ConcessionTotal}, Total: {TotalPrice}",
                    ticketTotal, concessionTotal, totalPrice);
                Console.WriteLine($"💰 Price calculation:");
                Console.WriteLine($"   Tickets: {validSeats.Count} x {showTime.Price:N0} = {ticketTotal:N0}₫");
                Console.WriteLine($"   Concessions: {concessionTotal:N0}₫");
                Console.WriteLine($"   Total: {totalPrice:N0}₫");

                // ✅ ENHANCED: Database transaction with better error handling
                Console.WriteLine($"🔄 Starting database transaction...");
                var ticket = await CreateTicketTransaction(userId, ShowTimeID, totalPrice, validSeats, orderItems);

                _logger.LogInformation("Ticket created successfully: {TicketId} for {TotalPrice}", ticket.ID, totalPrice);
                Console.WriteLine($"✅ Ticket created successfully: ID={ticket.ID}, Total={totalPrice:N0}₫");

                TempData["ToastSuccess"] = "Đặt vé thành công! Vui lòng thanh toán để hoàn tất đặt vé.";
                return RedirectToAction("PaymentSelection", new { ticketId = ticket.ID });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ CRITICAL ERROR in ConfirmOrder - ShowTimeID: {ShowTimeID}, User: {User}",
                    ShowTimeID, User?.Identity?.Name);

                Console.WriteLine($"❌ EXCEPTION MESSAGE: {ex.Message}");
                Console.WriteLine($"❌ STACK TRACE: {ex.StackTrace}");

                // 🔍 Nếu là lỗi liên quan tới database
                if (ex is DbUpdateException dbEx)
                {
                    Console.WriteLine("🔍 DATABASE UPDATE ERROR:");
                    Console.WriteLine($"   DbUpdateException Message: {dbEx.Message}");

                    if (dbEx.InnerException != null)
                    {
                        Console.WriteLine($"   Inner Exception: {dbEx.InnerException.Message}");
                        Console.WriteLine($"   Inner Exception Type: {dbEx.InnerException.GetType().Name}");

                        if (dbEx.InnerException.InnerException != null)
                        {
                            Console.WriteLine($"   Deep Inner Exception: {dbEx.InnerException.InnerException.Message}");
                        }
                    }

                    foreach (var entry in dbEx.Entries)
                    {
                        Console.WriteLine($"   Failed Entity: {entry.Entity.GetType().Name}");
                        Console.WriteLine($"   Entity State: {entry.State}");

                        if (entry.Entity is Ticket ticket)
                        {
                            Console.WriteLine($"     Ticket: UserID={ticket.UserID}, ShowTimeID={ticket.ShowTimeID}, Price={ticket.TotalPrice}");
                        }
                        else if (entry.Entity is TicketSeat ticketSeat)
                        {
                            Console.WriteLine($"     TicketSeat: TicketID={ticketSeat.TicketID}, SeatID={ticketSeat.SeatID}");
                        }
                        else if (entry.Entity is OrderItem orderItem)
                        {
                            Console.WriteLine($"     OrderItem: TicketID={orderItem.TicketID}, ConcessionID={orderItem.TheaterConcessionID}, Qty={orderItem.Quantity}");
                        }
                    }
                }

                Console.WriteLine("🔍 Debug info at error:");
                Console.WriteLine($"   ShowTimeID: {ShowTimeID}");
                Console.WriteLine($"   SelectedSeats: {SelectedSeats?.Count ?? 0} items");
                Console.WriteLine($"   User: {User?.Identity?.Name ?? "null"}");
                Console.WriteLine($"   Form keys: {form?.Keys?.Count ?? 0} items");

                // 👉 Hiển thị lỗi cụ thể hơn ra Toast khi DEV (chỉ dùng khi debug)
                string errorMessage = $"Chi tiết lỗi: {ex.Message}";

                // 👉 Nếu muốn show thông báo thân thiện hơn khi hết DEV thì dùng:
                if (ex is DbUpdateException && ex.InnerException?.Message.Contains("FOREIGN KEY") == true)
                {
                    errorMessage = "Dữ liệu không hợp lệ. Vui lòng làm mới trang và thử lại.";
                }
                else if (ex is DbUpdateException && ex.InnerException?.Message.Contains("PRIMARY KEY") == true)
                {
                    errorMessage = "Ghế đã được đặt bởi người khác. Vui lòng chọn ghế khác.";
                }
                else if (ex is DbUpdateException && ex.InnerException?.Message.Contains("NOT NULL") == true)
                {
                    errorMessage = "Thiếu thông tin bắt buộc. Vui lòng kiểm tra lại.";
                }

                TempData["ToastError"] = errorMessage;
                return RedirectToAction("OrderTicketDetails", new { id = ShowTimeID });
            }
        }


        // ✅ Payment Selection Page
        public async Task<IActionResult> PaymentSelection(int? ticketId)
        {
            if (ticketId == null || ticketId <= 0)
            {
                TempData["ToastError"] = "ID vé không hợp lệ.";
                return RedirectToAction("Index", "MoviesList");
            }

            try
            {
                Console.WriteLine($"🔍 Loading PaymentSelection with ticketId = {ticketId}");

                var ticket = await _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Auditorium)
                            .ThenInclude(a => a.Theater)
                    .Include(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                    .Include(t => t.OrderItems)
                        .ThenInclude(oi => oi.TheaterConcession)
                            .ThenInclude(tc => tc.Concession)
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.ID == ticketId.Value);

                if (ticket == null)
                {
                    Console.WriteLine($"❌ Không tìm thấy vé với ID {ticketId}");
                    TempData["ToastError"] = "Không tìm thấy vé.";
                    return RedirectToAction("Index", "MoviesList");
                }

                Console.WriteLine($"✅ Đã tìm thấy vé: TicketID={ticket.ID}, Status={ticket.Status}, UserID={ticket.UserID}");

                // Verify ownership
                if (User.Identity.IsAuthenticated)
                {
                    var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (int.TryParse(userIdStr, out int userId))
                    {
                        if (ticket.UserID != userId && !User.IsInRole("Admin"))
                        {
                            Console.WriteLine($"⛔ Người dùng không có quyền truy cập vé này. Ticket.UserID={ticket.UserID}, CurrentUser={userId}");
                            return Forbid("Bạn không có quyền xem vé này.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("❌ Không lấy được UserID từ claims.");
                    }
                }

                // Check if ticket is still pending
                if (ticket.Status != TicketStatus.PENDING)
                {
                    TempData["ToastError"] = "Vé này không ở trạng thái chờ thanh toán.";
                    return RedirectToAction("OrderConfirmation", new { ticketId });
                }

                // Check if payment is still valid
                if (ticket.PurchaseTime.AddMinutes(15) < DateTime.Now)
                {
                    await CancelExpiredTicket(ticket);
                    TempData["ToastError"] = "Vé đã hết hạn thanh toán (15 phút). Vui lòng đặt vé lại.";
                    return RedirectToAction("OrderTicket", new { id = ticket.ShowTime.MovieID });
                }

                return View(ticket);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in PaymentSelection for ticket {TicketId}", ticketId);
                Console.WriteLine($"❌ ERROR in PaymentSelection: {ex.Message}");

                Exception inner = ex;
                int level = 1;
                while (inner.InnerException != null)
                {
                    inner = inner.InnerException;
                    Console.WriteLine($"🔍 INNER EXCEPTION LEVEL {level++}: {inner.Message}");
                }

                TempData["ToastError"] = "Có lỗi xảy ra khi tải thông tin thanh toán.";
                return RedirectToAction("Index", "MoviesList");
            }

        }


        // ✅ Process Payment Selection
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(int ticketId, string paymentMethod)
        {
            try
            {
                _logger.LogInformation("Processing payment for ticket {TicketId} with method {PaymentMethod}", ticketId, paymentMethod);

                var ticket = await _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.OrderItems)
                    .FirstOrDefaultAsync(t => t.ID == ticketId);


                if (ticket == null)
                {
                    TempData["ToastError"] = "Không tìm thấy vé.";
                    return RedirectToAction("Index", "MoviesList");
                }

                // Verify ownership
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdStr, out int userId) && ticket.UserID != userId && !User.IsInRole("Admin"))
                {
                    return Forbid();
                }

                // Check ticket status
                if (ticket.Status != TicketStatus.PENDING)
                {
                    TempData["ToastError"] = "Vé này không ở trạng thái chờ thanh toán.";
                    return RedirectToAction("OrderConfirmation", new { ticketId });
                }

                // Check expiration
                if (ticket.PurchaseTime.AddMinutes(15) < DateTime.Now)
                {
                    await CancelExpiredTicket(ticket);
                    TempData["ToastError"] = "Vé đã hết hạn thanh toán.";
                    return RedirectToAction("OrderTicket", new { id = ticket.ShowTime.MovieID });
                }

                // Store payment method
                ticket.PaymentMethod = paymentMethod;
                _context.Update(ticket);
                await _context.SaveChangesAsync();

                // Process based on payment method
                switch (paymentMethod?.ToLower())
                {
                    case "cash":
                        // Cập nhật trạng thái vé
                        ticket.Status = TicketStatus.CONFIRMED;
                        ticket.PaymentMethod = "cash";
                        _context.Update(ticket);
                        await _context.SaveChangesAsync();

                        TempData["ToastSuccess"] = "Đặt vé thành công! Vui lòng thanh toán tại quầy khi đến rạp.";
                        return RedirectToAction("OrderConfirmation", new { ticketId });

                    case "stripe":
                        return RedirectToAction("CreateCheckoutSession", "Payment", new { ticketId });

                    default:
                        TempData["ToastError"] = "Phương thức thanh toán không hợp lệ.";
                        return RedirectToAction("PaymentSelection", new { ticketId });
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessPayment for ticket {TicketId}", ticketId);
                TempData["ToastError"] = "Có lỗi xảy ra khi xử lý thanh toán.";
                return RedirectToAction("PaymentSelection", new { ticketId });
            }
        }

        // ✅ Order Confirmation page
        public async Task<IActionResult> OrderConfirmation(int? ticketId)
        {
            if (ticketId == null || ticketId <= 0)
            {
                TempData["ToastError"] = "ID vé không hợp lệ.";
                return RedirectToAction("Index", "MoviesList");
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
                    .Include(t => t.OrderItems)
                        .ThenInclude(oi => oi.TheaterConcession)
                            .ThenInclude(tc => tc.Concession)
                    .Include(t => t.User)
                    .Include(t => t.TicketPayments)
                    .FirstOrDefaultAsync(t => t.ID == ticketId.Value);

                if (ticket == null)
                {
                    TempData["ToastError"] = "Không tìm thấy vé.";
                    return RedirectToAction("Index", "MoviesList");
                }

                // Verify ownership (allow admin to view all tickets)
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
                _logger.LogError(ex, "Error in OrderConfirmation for ticket {TicketId}", ticketId);
                TempData["ToastError"] = "Có lỗi xảy ra khi tải thông tin vé.";
                return RedirectToAction("Index", "MoviesList");
            }
        }

        // ✅ Cancel Ticket method
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
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.OrderItems)
                    .FirstOrDefaultAsync(t => t.ID == ticketId);


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

                // Cancel ticket and restore stock
                await CancelExpiredTicket(ticket);

                TempData["ToastSuccess"] = "Vé đã được hủy thành công.";
                return RedirectToAction("UserTickets");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CancelTicket for ticket {TicketId}", ticketId);
                TempData["ToastError"] = "Có lỗi xảy ra khi hủy vé. Vui lòng thử lại.";
                return RedirectToAction("UserTickets");
            }
        }

        // ✅ Admin method to confirm ticket
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
                _logger.LogError(ex, "Error in ConfirmTicket for ticket {TicketId}", ticketId);
                TempData["ToastError"] = "Có lỗi xảy ra khi xác nhận vé.";
                return StatusCode(500);
            }
        }

        // ✅ Get user tickets
        [Authorize(Roles = "User")]
        public async Task<IActionResult> UserTickets()
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdStr, out int userId))
                {
                    TempData["ToastError"] = "Phiên đăng nhập không hợp lệ.";
                    return RedirectToAction("Login", "Account");
                }

                var tickets = await _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Auditorium)
                            .ThenInclude(a => a.Theater)
                    .Include(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                    .Include(t => t.OrderItems)
                        .ThenInclude(oi => oi.TheaterConcession)
                            .ThenInclude(tc => tc.Concession)
                    .Include(t => t.TicketPayments)
                    .Where(t => t.UserID == userId)
                    .OrderByDescending(t => t.PurchaseTime)
                    .ToListAsync();

                return View(tickets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UserTickets for user {UserId}", User?.Identity?.Name);
                TempData["ToastError"] = "Có lỗi xảy ra khi tải danh sách vé.";
                return RedirectToAction("Index", "MoviesList");
            }
        }

        // ✅ Get ticket details for user
        [Authorize(Roles = "User")]
        public async Task<IActionResult> TicketDetails(int? id)
        {
            if (id == null || id <= 0)
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
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Auditorium)
                            .ThenInclude(a => a.Theater)
                    .Include(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                    .Include(t => t.OrderItems)
                        .ThenInclude(oi => oi.TheaterConcession)
                            .ThenInclude(tc => tc.Concession)
                    .Include(t => t.User)
                    .Include(t => t.TicketPayments)
                    .FirstOrDefaultAsync(t => t.ID == id.Value && t.UserID == userId);

                if (ticket == null)
                {
                    TempData["ToastError"] = "Không tìm thấy vé hoặc bạn không có quyền xem vé này.";
                    return RedirectToAction("UserTickets");
                }

                return View(ticket);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TicketDetails for ticket {TicketId}", id);
                TempData["ToastError"] = "Có lỗi xảy ra khi tải thông tin vé.";
                return RedirectToAction("UserTickets");
            }
        }

        // ✅ Admin methods for ticket management
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminTickets(string? status, string? search, int page = 1, int pageSize = 20)
        {
            try
            {
                var query = _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Auditorium)
                            .ThenInclude(a => a.Theater)
                    .Include(t => t.User)
                    .Include(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                    .AsQueryable();

                // Apply filters
                if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TicketStatus>(status, out var ticketStatus))
                {
                    query = query.Where(t => t.Status == ticketStatus);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchLower = search.Trim().ToLower();
                    query = query.Where(t =>
                        t.User.Username.ToLower().Contains(searchLower) ||
                        t.User.Email.ToLower().Contains(searchLower) ||
                        t.ShowTime.Movie.Title.ToLower().Contains(searchLower) ||
                        t.ID.ToString().Contains(searchLower));
                }

                var totalCount = await query.CountAsync();
                var tickets = await query
                    .OrderByDescending(t => t.PurchaseTime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                ViewData["CurrentStatus"] = status;
                ViewData["CurrentSearch"] = search;
                ViewData["CurrentPage"] = page;
                ViewData["TotalPages"] = (int)Math.Ceiling(totalCount / (double)pageSize);
                ViewData["TotalCount"] = totalCount;

                return View(tickets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AdminTickets");
                TempData["ToastError"] = "Có lỗi xảy ra khi tải danh sách vé.";
                return RedirectToAction("Index", "Home");
            }
        }

        // ✅ Get ticket statistics for admin dashboard
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetTicketStats()
        {
            try
            {
                var now = DateTime.Now;
                var startOfMonth = new DateTime(now.Year, now.Month, 1);
                var startOfDay = now.Date;

                var stats = new
                {
                    TotalTicketsToday = await _context.Tickets
                        .CountAsync(t => t.PurchaseTime >= startOfDay),

                    TotalTicketsThisMonth = await _context.Tickets
                        .CountAsync(t => t.PurchaseTime >= startOfMonth),

                    TotalRevenueToday = await _context.Tickets
                        .Where(t => t.PurchaseTime >= startOfDay &&
                                   (t.Status == TicketStatus.PAID || t.Status == TicketStatus.CONFIRMED))
                        .SumAsync(t => t.TotalPrice),

                    TotalRevenueThisMonth = await _context.Tickets
                        .Where(t => t.PurchaseTime >= startOfMonth &&
                                   (t.Status == TicketStatus.PAID || t.Status == TicketStatus.CONFIRMED))
                        .SumAsync(t => t.TotalPrice),

                    PendingTickets = await _context.Tickets
                        .CountAsync(t => t.Status == TicketStatus.PENDING),

                    TicketsByStatus = await _context.Tickets
                        .Where(t => t.PurchaseTime >= startOfMonth)
                        .GroupBy(t => t.Status)
                        .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
                        .ToListAsync()
                };

                return Json(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetTicketStats");
                return Json(new { error = "Không thể tải thống kê vé" });
            }
        }

        // ✅ Get ticket status for AJAX calls
        [HttpGet]
        public async Task<IActionResult> GetTicketStatus(int ticketId)
        {
            try
            {
                var ticket = await _context.Tickets
                    .Select(t => new { t.ID, t.Status, t.UserID })
                    .FirstOrDefaultAsync(t => t.ID == ticketId);

                if (ticket == null)
                {
                    return Json(new { error = "Ticket not found" });
                }

                // Verify ownership
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdStr, out int userId) && ticket.UserID != userId && !User.IsInRole("Admin"))
                {
                    return Json(new { error = "Access denied" });
                }

                return Json(new
                {
                    status = ticket.Status.ToString(),
                    ticketId = ticket.ID
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetTicketStatus for ticket {TicketId}", ticketId);
                return Json(new { error = "Internal error" });
            }
        }

        // ✅ DEBUG METHOD: Test basic functionality
        [HttpGet]
        [Authorize(Roles = "User")]
        public async Task<IActionResult> TestOrder(int showtimeId = 1)
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                Console.WriteLine($"🧪 Test Order - User ID: {userIdStr}");

                var showtime = await _context.ShowTimes
                    .Include(s => s.Movie)
                    .Include(s => s.Auditorium)
                        .ThenInclude(a => a.Theater)
                    .FirstOrDefaultAsync(s => s.ID == showtimeId);

                if (showtime == null)
                {
                    return Json(new { error = "Showtime not found" });
                }

                var availableSeats = await _context.Seats
                    .Where(s => s.AuditoriumID == showtime.AuditoriumID)
                    .Take(2)
                    .Select(s => s.ID)
                    .ToListAsync();

                return Json(new
                {
                    success = true,
                    userIdStr,
                    showtimeId,
                    movieTitle = showtime.Movie.Title,
                    theaterName = showtime.Auditorium.Theater.Name,
                    availableSeats,
                    seatCount = availableSeats.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TestOrder");
                return Json(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        // ✅ HELPER: Parse concessions with multiple fallback methods
        private Dictionary<int, int> ParseConcessions(IFormCollection form)
        {
            var concessions = new Dictionary<int, int>();

            Console.WriteLine($"🔍 Parsing concessions from {form.Keys.Count} form keys...");

            // Method 1: Pattern "concession_{ID}_quantity"
            foreach (var key in form.Keys)
            {
                Console.WriteLine($"   Checking form key: '{key}' = '{form[key]}'");

                if (key.StartsWith("concession_") && key.EndsWith("_quantity"))
                {
                    var parts = key.Split('_');
                    if (parts.Length == 3 && int.TryParse(parts[1], out int concessionId))
                    {
                        if (int.TryParse(form[key], out int quantity) && quantity > 0)
                        {
                            concessions[concessionId] = quantity;
                            Console.WriteLine($"   ✅ Method 1 - Added concession: ID={concessionId}, Qty={quantity}");
                        }
                    }
                }
            }

            // Method 2: Indexed format "ConcessionId_0", "ConcessionQty_0"
            if (!concessions.Any())
            {
                Console.WriteLine($"🔍 Method 1 failed, trying indexed format...");
                var concessionKeys = form.Keys.Where(k => k.StartsWith("ConcessionId_")).ToList();
                Console.WriteLine($"   Found {concessionKeys.Count} ConcessionId_ keys");

                foreach (var key in concessionKeys)
                {
                    var index = key.Replace("ConcessionId_", "");
                    var qtyKey = $"ConcessionQty_{index}";
                    Console.WriteLine($"   Checking: {key}='{form[key]}', {qtyKey}='{form[qtyKey]}'");

                    if (int.TryParse(form[key], out int concessionId) &&
                        int.TryParse(form[qtyKey], out int quantity) &&
                        quantity > 0)
                    {
                        concessions[concessionId] = quantity;
                        Console.WriteLine($"   ✅ Method 2 - Added indexed concession: ID={concessionId}, Qty={quantity}");
                    }
                }
            }

            // Method 3: Array format "ConcessionQuantities[123]"
            if (!concessions.Any())
            {
                Console.WriteLine($"🔍 Method 2 failed, trying array format...");
                foreach (var key in form.Keys.Where(k => k.StartsWith("ConcessionQuantities[")))
                {
                    Console.WriteLine($"   Array key: {key} = '{form[key]}'");
                    var match = System.Text.RegularExpressions.Regex.Match(key, @"ConcessionQuantities\[(\d+)\]");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int concessionId))
                    {
                        if (int.TryParse(form[key], out int quantity) && quantity > 0)
                        {
                            concessions[concessionId] = quantity;
                            Console.WriteLine($"   ✅ Method 3 - Added array concession: ID={concessionId}, Qty={quantity}");
                        }
                    }
                }
            }

            // Method 4: Simple name format (fallback)
            if (!concessions.Any())
            {
                Console.WriteLine($"🔍 All methods failed, trying simple name format...");
                foreach (var key in form.Keys)
                {
                    if (key.StartsWith("concession-") || key.Contains("quantity"))
                    {
                        Console.WriteLine($"   Found potential concession key: {key} = '{form[key]}'");
                        // Add more parsing logic if needed based on your actual form structure
                    }
                }
            }

            Console.WriteLine($"🔍 Concession parsing complete: {concessions.Count} items found");
            return concessions;
        }

        // Thêm các method này vào TicketsController hiện tại của bạn

        // ✅ Method kiểm tra trạng thái thanh toán cho Ajax
        [HttpGet]
        public async Task<IActionResult> CheckPaymentStatus(int ticketId)
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdStr, out int userId))
                {
                    return Json(new { error = "Unauthorized" });
                }

                var ticket = await _context.Tickets
                    .Select(t => new {
                        t.ID,
                        t.Status,
                        t.UserID,
                        t.PaymentTime,
                        t.PurchaseTime,
                        t.TotalPrice
                    })
                    .FirstOrDefaultAsync(t => t.ID == ticketId);

                if (ticket == null || (ticket.UserID != userId && !User.IsInRole("Admin")))
                {
                    return Json(new { error = "Ticket not found" });
                }

                // Kiểm tra xem có hết hạn không
                var isExpired = ticket.PurchaseTime.AddMinutes(15) < DateTime.Now;

                return Json(new
                {
                    status = ticket.Status.ToString(),
                    isExpired = isExpired,
                    paymentTime = ticket.PaymentTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                    remainingMinutes = Math.Max(0, (int)(ticket.PurchaseTime.AddMinutes(15) - DateTime.Now).TotalMinutes)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking payment status for ticket {TicketId}", ticketId);
                return Json(new { error = "Internal error" });
            }
        }

        // ✅ Method xử lý thanh toán với các phương thức khác nhau (Enhanced version)
        

        // ✅ Xử lý thanh toán VNPay (placeholder - cần implement thực tế)
        private async Task<IActionResult> ProcessVNPayPayment(Ticket ticket)
        {
            try
            {
                // TODO: Implement VNPay integration
                // Tạm thời redirect đến trang xác nhận với thông báo
                TempData["ToastInfo"] = "Đang chuyển hướng đến VNPay...";

                // Simulate payment URL (replace with real VNPay URL generation)
                var vnpayUrl = GenerateVNPayUrl(ticket);

                return Redirect(vnpayUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VNPay payment error for ticket {TicketId}", ticket.ID);
                TempData["ToastError"] = "Lỗi kết nối VNPay. Vui lòng thử lại.";
                return RedirectToAction("OrderConfirmation", new { ticketId = ticket.ID });
            }
        }

        // ✅ Xử lý thanh toán MoMo (placeholder)
        private async Task<IActionResult> ProcessMoMoPayment(Ticket ticket)
        {
            try
            {
                // TODO: Implement MoMo integration
                TempData["ToastInfo"] = "Đang chuyển hướng đến MoMo...";

                var momoUrl = GenerateMoMoUrl(ticket);

                return Redirect(momoUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MoMo payment error for ticket {TicketId}", ticket.ID);
                TempData["ToastError"] = "Lỗi kết nối MoMo. Vui lòng thử lại.";
                return RedirectToAction("OrderConfirmation", new { ticketId = ticket.ID });
            }
        }

        // ✅ Xử lý thanh toán Banking
        private async Task<IActionResult> ProcessBankingPayment(Ticket ticket)
        {
            try
            {
                // Tạo thông tin chuyển khoản
                var bankingInfo = new
                {
                    BankName = "Vietcombank",
                    AccountNumber = "0123456789",
                    AccountName = "CONG TY DK MOVIES",
                    Amount = ticket.TotalPrice,
                    TransactionCode = $"TICKET{ticket.ID:D6}",
                    Note = $"Thanh toan ve #{ticket.ID} - {ticket.ShowTime.Movie.Title}"
                };

                ViewData["BankingInfo"] = bankingInfo;
                ViewData["Ticket"] = ticket;

                return View("BankingPayment", ticket);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Banking payment error for ticket {TicketId}", ticket.ID);
                TempData["ToastError"] = "Lỗi tạo thông tin chuyển khoản.";
                return RedirectToAction("OrderConfirmation", new { ticketId = ticket.ID });
            }
        }

        // ✅ Xử lý thanh toán tiền mặt tại quầy
        private async Task<IActionResult> ProcessCashPayment(Ticket ticket)
        {
            try
            {
                // Với thanh toán tiền mặt, chuyển trạng thái thành CONFIRMED
                // Admin sẽ xác nhận thanh toán khi khách hàng đến quầy
                ticket.Status = TicketStatus.CONFIRMED;
                ticket.PaymentMethod = "cash";
                _context.Update(ticket);

                await _context.SaveChangesAsync();

                TempData["ToastSuccess"] = "Đặt vé thành công! Vui lòng thanh toán tại quầy khi đến rạp.";
                return RedirectToAction("OrderConfirmation", new { ticketId = ticket.ID });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cash payment error for ticket {TicketId}", ticket.ID);
                TempData["ToastError"] = "Lỗi xử lý thanh toán tiền mặt.";
                return RedirectToAction("OrderConfirmation", new { ticketId = ticket.ID });
            }
        }

        // ✅ Helper: Generate VNPay URL (placeholder - cần implement thật)
        private string GenerateVNPayUrl(Ticket ticket)
        {
            // TODO: Implement real VNPay URL generation
            // Tạm thời return về success URL để test
            var returnUrl = Url.Action("PaymentCallback", "Tickets", new
            {
                ticketId = ticket.ID,
                method = "vnpay",
                result = "success"
            }, Request.Scheme);

            return returnUrl;
        }

        // ✅ Helper: Generate MoMo URL (placeholder)
        private string GenerateMoMoUrl(Ticket ticket)
        {
            // TODO: Implement real MoMo URL generation
            var returnUrl = Url.Action("PaymentCallback", "Tickets", new
            {
                ticketId = ticket.ID,
                method = "momo",
                result = "success"
            }, Request.Scheme);

            return returnUrl;
        }

        // ✅ Payment callback handler
        [HttpGet]
        public async Task<IActionResult> PaymentCallback(int ticketId, string method, string result)
        {
            try
            {
                var ticket = await _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.ID == ticketId);

                if (ticket == null)
                {
                    TempData["ToastError"] = "Không tìm thấy vé.";
                    return RedirectToAction("Index", "MoviesList");
                }

                if (result == "success")
                {
                    // Cập nhật trạng thái thành công
                    ticket.Status = TicketStatus.PAID;
                    ticket.PaymentTime = DateTime.Now;

                    _context.Update(ticket);
                    await _context.SaveChangesAsync();

                    TempData["ToastSuccess"] = "Thanh toán thành công! Vé của bạn đã được xác nhận.";
                }
                else
                {
                    TempData["ToastError"] = "Thanh toán thất bại. Vui lòng thử lại.";
                }

                return RedirectToAction("OrderConfirmation", new { ticketId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Payment callback error for ticket {TicketId}", ticketId);
                TempData["ToastError"] = "Có lỗi xảy ra trong quá trình xử lý thanh toán.";
                return RedirectToAction("Index", "MoviesList");
            }
        }

        

        // ✅ Bulk cancel tickets
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkCancelTickets(List<int> ticketIds)
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdStr, out int userId))
                {
                    TempData["ToastError"] = "Phiên đăng nhập không hợp lệ.";
                    return RedirectToAction("UserTickets");
                }

                var tickets = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .Include(t => t.OrderItems)
                    .Where(t => ticketIds.Contains(t.ID) &&
                               t.UserID == userId &&
                               t.Status == TicketStatus.PENDING)
                    .ToListAsync();

                if (!tickets.Any())
                {
                    TempData["ToastError"] = "Không tìm thấy vé hợp lệ để hủy.";
                    return RedirectToAction("UserTickets");
                }

                var cancelledCount = 0;
                foreach (var ticket in tickets)
                {
                    // Chỉ hủy nếu còn thời gian (2 tiếng trước suất chiếu)
                    if (ticket.ShowTime.StartTime > DateTime.Now.AddHours(2))
                    {
                        await CancelExpiredTicketInternal(ticket);
                        cancelledCount++;
                    }
                }

                if (cancelledCount > 0)
                {
                    TempData["ToastSuccess"] = $"Đã hủy thành công {cancelledCount} vé.";
                }
                else
                {
                    TempData["ToastWarning"] = "Không thể hủy vé (quá gần giờ chiếu).";
                }

                return RedirectToAction("UserTickets");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk cancel error");
                TempData["ToastError"] = "Có lỗi xảy ra khi hủy vé.";
                return RedirectToAction("UserTickets");
            }
        }

        // ✅ Check bulk payment status
        [HttpGet]
        public async Task<IActionResult> CheckBulkPaymentStatus()
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdStr, out int userId))
                {
                    return Json(new { hasUpdates = false });
                }

                // Kiểm tra có vé nào được cập nhật trong 30 giây qua không
                var recentlyUpdated = await _context.Tickets
                    .Where(t => t.UserID == userId &&
                               t.PaymentTime.HasValue &&
                               t.PaymentTime.Value > DateTime.Now.AddSeconds(-30))
                    .AnyAsync();

                return Json(new { hasUpdates = recentlyUpdated });
            }
            catch
            {
                return Json(new { hasUpdates = false });
            }
        }

        // ✅ Enhanced UserTickets with filtering and pagination
        [Authorize(Roles = "User")]
        public async Task<IActionResult> UserTickets(string? search, string? status, string? sort, int page = 1, int pageSize = 10)
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdStr, out int userId))
                {
                    TempData["ToastError"] = "Phiên đăng nhập không hợp lệ.";
                    return RedirectToAction("Login", "Account");
                }

                var query = _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Auditorium)
                            .ThenInclude(a => a.Theater)
                    .Include(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                    .Include(t => t.OrderItems)
                        .ThenInclude(oi => oi.TheaterConcession)
                            .ThenInclude(tc => tc.Concession)
                    .Include(t => t.TicketPayments)
                    .Where(t => t.UserID == userId)
                    .AsQueryable();

                // Apply filters
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var searchLower = search.Trim().ToLower();
                    query = query.Where(t =>
                        t.ShowTime.Movie.Title.ToLower().Contains(searchLower) ||
                        t.ShowTime.Auditorium.Theater.Name.ToLower().Contains(searchLower) ||
                        t.ShowTime.Auditorium.Theater.Location.ToLower().Contains(searchLower));
                }

                if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TicketStatus>(status, out var ticketStatus))
                {
                    query = query.Where(t => t.Status == ticketStatus);
                }

                // Apply sorting
                query = sort switch
                {
                    "date_asc" => query.OrderBy(t => t.PurchaseTime),
                    "showtime_asc" => query.OrderBy(t => t.ShowTime.StartTime),
                    "showtime_desc" => query.OrderByDescending(t => t.ShowTime.StartTime),
                    "price_asc" => query.OrderBy(t => t.TotalPrice),
                    "price_desc" => query.OrderByDescending(t => t.TotalPrice),
                    _ => query.OrderByDescending(t => t.PurchaseTime)
                };

                var totalCount = await query.CountAsync();
                var tickets = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                ViewData["Search"] = search;
                ViewData["Status"] = status;
                ViewData["Sort"] = sort;
                ViewData["CurrentPage"] = page;
                ViewData["TotalPages"] = (int)Math.Ceiling(totalCount / (double)pageSize);
                ViewData["TotalCount"] = totalCount;

                return View(tickets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UserTickets for user {UserId}", User?.Identity?.Name);
                TempData["ToastError"] = "Có lỗi xảy ra khi tải danh sách vé.";
                return RedirectToAction("Index", "MoviesList");
            }
        }

        // ✅ Internal helper để cancel expired ticket (phiên bản nâng cao)
        private async Task CancelExpiredTicketInternal(Ticket ticket)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                ticket.Status = TicketStatus.CANCELLED;
                _context.Update(ticket);

                // Restore concession stock
                if (ticket.OrderItems?.Any() == true)
                {
                    foreach (var orderItem in ticket.OrderItems)
                    {
                        var concession = await _context.TheaterConcessions
                            .FirstOrDefaultAsync(tc => tc.ID == orderItem.TheaterConcessionID);

                        if (concession != null)
                        {
                            concession.StockLeft += orderItem.Quantity;
                            _context.Update(concession);
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Cancelled expired ticket: {TicketId}", ticket.ID);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to cancel expired ticket: {TicketId}", ticket.ID);
                throw;
            }
        }

        // ✅ Auto-cancel expired tickets (Background job method)
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AutoCancelExpiredTickets()
        {
            try
            {
                var expiredTickets = await _context.Tickets
                    .Include(t => t.OrderItems)
                    .Where(t => t.Status == TicketStatus.PENDING &&
                               t.PurchaseTime.AddMinutes(15) < DateTime.Now)
                    .ToListAsync();

                var cancelledCount = 0;
                foreach (var ticket in expiredTickets)
                {
                    try
                    {
                        await CancelExpiredTicketInternal(ticket);
                        cancelledCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to auto-cancel ticket {TicketId}", ticket.ID);
                    }
                }

                return Json(new
                {
                    success = true,
                    cancelledCount,
                    message = $"Đã hủy {cancelledCount} vé hết hạn"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AutoCancelExpiredTickets");
                return Json(new
                {
                    success = false,
                    error = "Có lỗi xảy ra khi hủy vé hết hạn"
                });
            }
        }

        // ✅ Get payment methods available
        [HttpGet]
        public async Task<IActionResult> GetPaymentMethods()
        {
            try
            {
            var paymentMethods = new[]
            {
                new {
                    Code = "cash",
                    Name = "Tiền mặt",
                    Description = "Thanh toán tại quầy",
                    Icon = "bi-cash",
                    IsActive = true
                },
                new {
                    Code = "stripe",
                    Name = "Thẻ quốc tế",
                    Description = "Thanh toán bằng thẻ",
                    Icon = "bi-credit-card-2-front",
                    IsActive = true
                }
            };


                return Json(paymentMethods);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting payment methods");
                return Json(new { error = "Không thể tải phương thức thanh toán" });
            }
        }

        // ✅ HELPER: Process concessions with validation
        private async Task<List<OrderItem>> ProcessConcessions(Dictionary<int, int> concessions, int theaterID, int showTimeID)
        {
            var orderItems = new List<OrderItem>();

            if (!concessions.Any())
            {
                Console.WriteLine($"🍿 No concessions to process");
                return orderItems;
            }

            Console.WriteLine($"🔍 Processing {concessions.Count} concessions for theater {theaterID}...");

            var concessionIds = concessions.Keys.ToList();
            var validConcessions = await _context.TheaterConcessions
                .Include(tc => tc.Concession)
                .Where(tc => tc.TheaterID == theaterID &&
                           tc.IsAvailable &&
                           tc.Concession.IsActive &&
                           concessionIds.Contains(tc.ID))
                .ToListAsync();

            Console.WriteLine($"   Found {validConcessions.Count} valid concessions in database");

            foreach (var concessionItem in concessions)
            {
                var concession = validConcessions.FirstOrDefault(c => c.ID == concessionItem.Key);
                var quantity = concessionItem.Value;

                Console.WriteLine($"   Processing concession {concessionItem.Key}: qty={quantity}");

                if (concession != null && quantity > 0 && quantity <= 20)
                {
                    // Check stock availability
                    if (concession.StockLeft < quantity)
                    {
                        Console.WriteLine($"   ❌ Not enough stock: needed={quantity}, available={concession.StockLeft}");
                        throw new InvalidOperationException($"Không đủ tồn kho cho {concession.Concession.Name}. Còn lại: {concession.StockLeft}");
                    }

                    orderItems.Add(new OrderItem
                    {
                        TheaterConcessionID = concession.ID,
                        Quantity = quantity,
                        PriceAtPurchase = concession.Price
                    });

                    Console.WriteLine($"   ✅ Added: {concession.Concession.Name} x{quantity} = {quantity * concession.Price:N0}₫");
                }
                else
                {
                    Console.WriteLine($"   ⚠️ Skipped invalid concession: ID={concessionItem.Key}, Found={concession != null}, Qty={quantity}");
                }
            }

            return orderItems;
        }

        // ✅ HELPER: Create ticket transaction
        private async Task<Ticket> CreateTicketTransaction(int userId, int showTimeID, decimal totalPrice,
    List<Seat> validSeats, List<OrderItem> orderItems)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                Console.WriteLine($"🔍 Pre-validation checks...");

                if (!await _context.Users.AnyAsync(u => u.ID == userId))
                    throw new InvalidOperationException($"User with ID {userId} does not exist");

                if (!await _context.ShowTimes.AnyAsync(st => st.ID == showTimeID))
                    throw new InvalidOperationException($"Showtime with ID {showTimeID} does not exist");

                var showtime = await _context.ShowTimes
                    .Include(st => st.Auditorium)
                    .FirstAsync(st => st.ID == showTimeID);

                var seatIds = validSeats.Select(s => s.ID).ToList();
                int validSeatCount = await _context.Seats
                    .CountAsync(s => seatIds.Contains(s.ID) && s.AuditoriumID == showtime.AuditoriumID);

                if (validSeatCount != validSeats.Count)
                    throw new InvalidOperationException("Some seats are invalid or don't belong to the auditorium");

                bool takenSeatCheck = await _context.TicketSeats
                    .Include(ts => ts.Ticket)
                    .AnyAsync(ts => seatIds.Contains(ts.SeatID) &&
                                    ts.Ticket.ShowTimeID == showTimeID &&
                                    (ts.Ticket.Status == TicketStatus.CONFIRMED ||
                                     ts.Ticket.Status == TicketStatus.PENDING ||
                                     ts.Ticket.Status == TicketStatus.PAID));

                if (takenSeatCheck)
                    throw new InvalidOperationException("Some seats have been taken by another user");

                Console.WriteLine($"✅ Pre-validation passed");

                var ticket = new Ticket
                {
                    UserID = userId,
                    ShowTimeID = showTimeID,
                    PurchaseTime = DateTime.Now,
                    Status = TicketStatus.PENDING,
                    TotalPrice = totalPrice,
                    PaymentMethod = "stripe" // 👈 Tạm thời gán nếu cột không cho null
                };

                if (ticket.TotalPrice <= 0)
                    throw new InvalidOperationException("Total price must be greater than 0");

                _context.Tickets.Add(ticket);

                try
                {
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"✅ Created ticket with ID: {ticket.ID}");
                }
                catch (DbUpdateException dbEx)
                {
                    Console.WriteLine($"❌ Failed to save Ticket:");
                    LogDeepException(dbEx);
                    throw new InvalidOperationException($"Failed to create ticket: {dbEx.Message}", dbEx);
                }

                var ticketSeats = validSeats.Select(seat => new TicketSeat
                {
                    TicketID = ticket.ID,
                    SeatID = seat.ID
                }).ToList();

                _context.TicketSeats.AddRange(ticketSeats);
                Console.WriteLine($"✅ Added {ticketSeats.Count} ticket seats");

                if (orderItems.Any())
                {
                    foreach (var item in orderItems)
                    {
                        item.TicketID = ticket.ID;

                        if (item.TicketID <= 0 || item.TheaterConcessionID <= 0 || item.Quantity <= 0 || item.PriceAtPurchase <= 0)
                            throw new InvalidOperationException($"Invalid OrderItem: TicketID={item.TicketID}, ConcessionID={item.TheaterConcessionID}, Qty={item.Quantity}, Price={item.PriceAtPurchase}");

                        var concession = await _context.TheaterConcessions.FirstOrDefaultAsync(tc => tc.ID == item.TheaterConcessionID);

                        if (concession == null)
                            throw new InvalidOperationException($"Concession with ID {item.TheaterConcessionID} not found");

                        if (concession.StockLeft < item.Quantity)
                            throw new InvalidOperationException($"Not enough stock for concession {concession.ID}. Available: {concession.StockLeft}, Requested: {item.Quantity}");

                        concession.StockLeft -= item.Quantity;
                        _context.Update(concession);
                        Console.WriteLine($"📦 Updated stock: {concession.ID} - {item.Quantity} = {concession.StockLeft}");
                    }

                    _context.OrderItems.AddRange(orderItems);
                    Console.WriteLine($"✅ Added {orderItems.Count} order items");
                }

                try
                {
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    Console.WriteLine($"✅ Transaction completed successfully!");
                    return ticket;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error saving final changes: {ex.Message}");
                    LogDeepException(ex);
                    throw;
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"❌ Transaction failed: {ex.Message}");
                LogDeepException(ex);
                throw new InvalidOperationException($"Failed to create ticket transaction: {ex.Message}", ex);
            }
        }

        // Tiện ích ghi lỗi sâu nhất
        private void LogDeepException(Exception ex)
        {
            Console.WriteLine($"⚠️  TOP-LEVEL ERROR: {ex.Message}");
            Exception inner = ex.InnerException;
            int depth = 1;
            while (inner != null)
            {
                Console.WriteLine($"🔍 INNER {depth}: {inner.Message}");
                inner = inner.InnerException;
                depth++;
            }
        }


        // ✅ Helper method to cancel expired tickets and restore stock
        private async Task CancelExpiredTicket(Ticket ticket)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Cancel ticket
                ticket.Status = TicketStatus.CANCELLED;
                _context.Update(ticket);

                // Restore concession stock
                if (ticket.OrderItems?.Any() == true)
                {
                    foreach (var orderItem in ticket.OrderItems)
                    {
                        var concession = await _context.TheaterConcessions
                            .FirstOrDefaultAsync(tc => tc.ID == orderItem.TheaterConcessionID);

                        if (concession != null)
                        {
                            concession.StockLeft += orderItem.Quantity;
                            _context.Update(concession);
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Cancelled expired ticket: {TicketId}", ticket.ID);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to cancel ticket: {TicketId}", ticket.ID);
                throw;
            }
        }
    }
}