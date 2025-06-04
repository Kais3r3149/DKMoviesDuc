using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;

namespace DKMovies.Controllers
{
    public class ShowTimesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ShowTimesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: ShowTimes
        public async Task<IActionResult> Index()
        {
            var applicationDbContext = _context.ShowTimes
                .Include(s => s.Auditorium)
                    .ThenInclude(a => a.Theater)
                .Include(s => s.Movie)
                .Include(s => s.SubtitleLanguage)
                .Include(s => s.Tickets)
                    .ThenInclude(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                .OrderByDescending(s => s.StartTime);

            return View(await applicationDbContext.ToListAsync());
        }

        // GET: ShowTimes/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var showTime = await _context.ShowTimes
                .Include(s => s.Auditorium)
                    .ThenInclude(a => a.Theater)
                .Include(s => s.Movie)
                .Include(s => s.SubtitleLanguage)
                .Include(s => s.Tickets)
                    .ThenInclude(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                .Include(s => s.Tickets)
                    .ThenInclude(t => t.User) // Customer information
                .FirstOrDefaultAsync(m => m.ID == id);

            if (showTime == null)
            {
                return NotFound();
            }

            return View(showTime);
        }

        // GET: API để lấy danh sách vé đã bán theo suất chiếu
        [HttpGet]
        public async Task<JsonResult> GetSoldTickets(int showtimeId)
        {
            try
            {
                var tickets = await _context.Tickets
                    .Where(t => t.ShowTimeID == showtimeId &&
                               (t.Status == TicketStatus.PAID || t.Status == TicketStatus.CONFIRMED))
                    .Include(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                    .Include(t => t.User)
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Auditorium)
                            .ThenInclude(a => a.Theater)
                    .OrderByDescending(t => t.PurchaseTime)
                    .Select(t => new
                    {
                        id = t.ID,
                        seatNames = string.Join(", ", t.TicketSeats.Select(ts => ts.Seat.RowLabel + ts.Seat.SeatNumber)),
                        totalPrice = t.TotalPrice,
                        status = t.Status.ToString(),
                        purchaseTime = t.PurchaseTime.ToString("dd/MM/yyyy HH:mm"),
                        customerName = t.User != null ? t.User.FullName : "N/A",
                        customerPhone = t.User != null ? t.User.Phone : "N/A"
                    })
                    .ToListAsync();

                return Json(new { success = true, data = tickets });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: API để lấy thống kê vé theo phim
        [HttpGet]
        public async Task<JsonResult> GetTicketStatistics(int movieId)
        {
            try
            {
                var statistics = await _context.ShowTimes
                    .Where(st => st.MovieID == movieId)
                    .Include(st => st.Tickets)
                    .SelectMany(st => st.Tickets)
                    .Where(t => t.Status == TicketStatus.PAID || t.Status == TicketStatus.CONFIRMED)
                    .GroupBy(t => 1) // Group all tickets together
                    .Select(g => new
                    {
                        totalTickets = g.Count(),
                        totalRevenue = g.Sum(t => t.TotalPrice),
                        averagePrice = g.Average(t => t.TotalPrice)
                    })
                    .FirstOrDefaultAsync();

                if (statistics == null)
                {
                    statistics = new { totalTickets = 0, totalRevenue = 0.0m, averagePrice = 0.0m };
                }

                return Json(new { success = true, data = statistics });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: API để làm mới dữ liệu vé
        [HttpGet]
        public async Task<JsonResult> RefreshTicketData(int movieId)
        {
            try
            {
                var movie = await _context.Movies
                    .Include(m => m.ShowTimes)
                        .ThenInclude(st => st.Tickets)
                            .ThenInclude(t => t.TicketSeats)
                                .ThenInclude(ts => ts.Seat)
                    .Include(m => m.ShowTimes)
                        .ThenInclude(st => st.Auditorium)
                            .ThenInclude(a => a.Theater)
                    .Include(m => m.ShowTimes)
                        .ThenInclude(st => st.Tickets)
                            .ThenInclude(t => t.User)
                    .FirstOrDefaultAsync(m => m.ID == movieId);

                if (movie == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy phim" });
                }

                var soldTickets = movie.ShowTimes
                    .Where(st => st.Tickets != null)
                    .SelectMany(st => st.Tickets)
                    .Where(t => t.Status == TicketStatus.PAID || t.Status == TicketStatus.CONFIRMED)
                    .OrderByDescending(t => t.PurchaseTime)
                    .Take(50)
                    .Select(t => new
                    {
                        id = t.ID,
                        showtimeDate = t.ShowTime?.StartTime.ToString("dd/MM/yyyy"),
                        showtimeTime = t.ShowTime?.StartTime.ToString("HH:mm"),
                        theaterName = t.ShowTime?.Auditorium?.Theater?.Name ?? "N/A",
                        auditoriumName = t.ShowTime?.Auditorium?.Name ?? "N/A",
                        seatNames = t.TicketSeats != null && t.TicketSeats.Any()
                            ? string.Join(", ", t.TicketSeats.Select(ts => ts.Seat.RowLabel + ts.Seat.SeatNumber))
                            : "N/A",
                        price = t.TotalPrice.ToString("N0") + " VND",
                        status = GetStatusText(t.Status.ToString()),
                        statusClass = GetStatusClass(t.Status.ToString()),
                        purchaseTime = t.PurchaseTime.ToString("dd/MM/yyyy HH:mm"),
                        customerName = t.User?.FullName ?? "N/A",
                        customerPhone = t.User?.Phone ?? "N/A"
                    })
                    .ToList();

                var statistics = new
                {
                    totalTickets = movie.ShowTimes
                        .Where(st => st.Tickets != null)
                        .SelectMany(st => st.Tickets)
                        .Count(t => t.Status == TicketStatus.PAID || t.Status == TicketStatus.CONFIRMED),

                    totalShowtimes = movie.ShowTimes?.Count() ?? 0,

                    totalRevenue = movie.ShowTimes
                        .Where(st => st.Tickets != null)
                        .SelectMany(st => st.Tickets)
                        .Where(t => t.Status == TicketStatus.PAID || t.Status == TicketStatus.CONFIRMED)
                        .Sum(t => t.TotalPrice).ToString("N0") + " VND"
                };

                return Json(new
                {
                    success = true,
                    tickets = soldTickets,
                    statistics = statistics
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string GetStatusText(string status)
        {
            return status?.ToUpper() switch
            {
                "PAID" => "Đã thanh toán",
                "CONFIRMED" => "Đã xác nhận",
                "PENDING" => "Chờ xử lý",
                "CANCELLED" => "Đã hủy",
                _ => "Không rõ"
            };
        }

        private string GetStatusClass(string status)
        {
            return status?.ToUpper() switch
            {
                "PAID" or "CONFIRMED" => "bg-success",
                "PENDING" => "bg-warning",
                "CANCELLED" => "bg-danger",
                _ => "bg-secondary"
            };
        }

        [HttpGet]
        public async Task<JsonResult> GetAuditoriumsByTheater(int theaterId)
        {
            var auditoriums = await _context.Auditoriums
                .Where(a => a.TheaterID == theaterId)
                .OrderBy(a => a.Name)
                .Select(a => new { id = a.ID, name = a.Name })
                .ToListAsync();

            return Json(auditoriums);
        }

        // GET: ShowTimes/Create
        public async Task<IActionResult> Create(int? movieId)
        {
            var showtime = new ShowTime();

            if (movieId.HasValue)
            {
                var movie = await _context.Movies.FirstOrDefaultAsync(m => m.ID == movieId.Value);
                if (movie != null)
                {
                    showtime.MovieID = movie.ID;
                    ViewBag.MovieID = movie.ID;
                    ViewBag.MovieTitle = movie.Title;
                    ViewBag.MovieDuration = movie.DurationMinutes;
                }
            }

            ViewBag.Theaters = await _context.Theaters.OrderBy(t => t.Name).ToListAsync();
            ViewData["LanguageID"] = new SelectList(_context.Languages, "ID", "Name");

            return View(showtime);
        }

        // POST: ShowTimes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ID,MovieID,AuditoriumID,StartTime,SubtitleLanguageID,Is3D,Price")] ShowTime showTime, int? TheaterID)
        {
            // Ensure MovieID is present
            if (showTime.MovieID == 0)
            {
                ModelState.AddModelError("MovieID", "The MovieID field is required.");
            }

            var movie = await _context.Movies.FirstOrDefaultAsync(m => m.ID == showTime.MovieID);
            if (movie == null && showTime.MovieID != 0)
            {
                ModelState.AddModelError("MovieID", "Selected movie does not exist.");
            }

            // Ensure AuditoriumID is present
            if (showTime.AuditoriumID == 0)
            {
                ModelState.AddModelError("AuditoriumID", "The AuditoriumID field is required.");
            }
            else
            {
                var auditorium = await _context.Auditoriums
                    .Include(a => a.Theater)
                    .FirstOrDefaultAsync(a => a.ID == showTime.AuditoriumID);
                if (auditorium == null)
                {
                    ModelState.AddModelError("AuditoriumID", "Selected auditorium does not exist.");
                }
            }

            if (movie != null)
            {
                showTime.DurationMinutes = movie.DurationMinutes;

                // Conflict check logic
                var newShowStart = showTime.StartTime;
                var newShowEnd = showTime.StartTime.AddMinutes(movie.DurationMinutes);

                var conflictingShowtimeExists = await _context.ShowTimes
                    .Where(s => s.AuditoriumID == showTime.AuditoriumID)
                    .AnyAsync(s =>
                        newShowStart < s.StartTime.AddMinutes(s.DurationMinutes + 30) &&
                        newShowEnd.AddMinutes(30) > s.StartTime
                    );

                if (conflictingShowtimeExists)
                {
                    ModelState.AddModelError("", "This showtime conflicts with another showtime in the same auditorium.");
                }
            }

            if (ModelState.IsValid)
            {
                _context.Add(showTime);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            // Repopulate ViewBags/Data for view if ModelState is not valid
            ViewBag.Theaters = await _context.Theaters.OrderBy(t => t.Name).ToListAsync();
            ViewData["LanguageID"] = new SelectList(_context.Languages, "ID", "Name", showTime.SubtitleLanguageID);

            if (TheaterID.HasValue)
            {
                var auditoriums = await _context.Auditoriums
                    .Where(a => a.TheaterID == TheaterID.Value)
                    .OrderBy(a => a.Name)
                    .ToListAsync();
                ViewData["AuditoriumID"] = new SelectList(auditoriums, "ID", "Name", showTime.AuditoriumID);
                ViewBag.SelectedTheaterID = TheaterID.Value;
            }
            else
            {
                ViewData["AuditoriumID"] = new SelectList(Enumerable.Empty<Auditorium>(), "ID", "Name");
            }

            if (movie != null)
            {
                ViewBag.MovieTitle = movie.Title;
                ViewBag.MovieDuration = movie.DurationMinutes;
            }
            ViewBag.MovieID = showTime.MovieID;

            return View(showTime);
        }

        // GET: ShowTimes/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var showTime = await _context.ShowTimes
                .Include(st => st.Auditorium)
                    .ThenInclude(a => a.Theater)
                .Include(st => st.Movie)
                .FirstOrDefaultAsync(st => st.ID == id);

            if (showTime == null)
            {
                return NotFound();
            }

            // Get theaters for dropdown
            ViewBag.Theaters = await _context.Theaters.OrderBy(t => t.Name).ToListAsync();
            ViewBag.SelectedTheaterID = showTime.Auditorium?.TheaterID;

            // Get auditoriums for selected theater
            if (showTime.Auditorium?.TheaterID != null)
            {
                var auditoriums = await _context.Auditoriums
                    .Where(a => a.TheaterID == showTime.Auditorium.TheaterID)
                    .OrderBy(a => a.Name)
                    .ToListAsync();
                ViewData["AuditoriumID"] = new SelectList(auditoriums, "ID", "Name", showTime.AuditoriumID);
            }
            else
            {
                ViewData["AuditoriumID"] = new SelectList(Enumerable.Empty<Auditorium>(), "ID", "Name");
            }

            ViewData["MovieID"] = new SelectList(_context.Movies, "ID", "Title", showTime.MovieID);
            ViewData["SubtitleLanguageID"] = new SelectList(_context.Languages, "ID", "Name", showTime.SubtitleLanguageID);

            return View(showTime);
        }

        // POST: ShowTimes/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ID,MovieID,AuditoriumID,StartTime,DurationMinutes,SubtitleLanguageID,Is3D,Price")] ShowTime showTime)
        {
            if (id != showTime.ID)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(showTime);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ShowTimeExists(showTime.ID))
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

            ViewData["AuditoriumID"] = new SelectList(_context.Auditoriums, "ID", "Name", showTime.AuditoriumID);
            ViewData["MovieID"] = new SelectList(_context.Movies, "ID", "Title", showTime.MovieID);
            ViewData["SubtitleLanguageID"] = new SelectList(_context.Languages, "ID", "Name", showTime.SubtitleLanguageID);
            return View(showTime);
        }

        // GET: ShowTimes/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var showTime = await _context.ShowTimes
                .Include(s => s.Auditorium)
                    .ThenInclude(a => a.Theater)
                .Include(s => s.Movie)
                .Include(s => s.SubtitleLanguage)
                .Include(s => s.Tickets)
                .FirstOrDefaultAsync(m => m.ID == id);

            if (showTime == null)
            {
                return NotFound();
            }

            return View(showTime);
        }

        // POST: ShowTimes/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var showTime = await _context.ShowTimes
                .Include(st => st.Tickets)
                .FirstOrDefaultAsync(st => st.ID == id);

            if (showTime != null)
            {
                // Check if there are sold tickets
                var hasSoldTickets = showTime.Tickets?.Any(t =>
                    t.Status == TicketStatus.PAID || t.Status == TicketStatus.CONFIRMED) ?? false;

                if (hasSoldTickets)
                {
                    TempData["Error"] = "Không thể xóa suất chiếu này vì đã có vé được bán.";
                    return RedirectToAction(nameof(Index));
                }

                _context.ShowTimes.Remove(showTime);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Xóa suất chiếu thành công.";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool ShowTimeExists(int id)
        {
            return _context.ShowTimes.Any(e => e.ID == id);
        }
    }
}