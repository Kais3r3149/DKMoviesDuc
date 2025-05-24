using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;

namespace DKMovies.Controllers
{
    public class AdminShowTimeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminShowTimeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: AdminShowTime
        public async Task<IActionResult> Index()
        {
            var showtimes = await _context.ShowTimes
                .Include(s => s.Movie)
                .Include(s => s.Auditorium)
                    .ThenInclude(a => a.Theater)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();

            return View(showtimes);
        }

        // GET: AdminShowTime/Create
        public IActionResult Create()
        {
            ViewData["MovieID"] = new SelectList(_context.Movies, "ID", "Title");
            ViewData["AuditoriumID"] = new SelectList(
                _context.Auditoriums.Include(a => a.Theater)
                .Select(a => new
                {
                    a.ID,
                    Name = a.Theater.Name + " - Phòng " + a.Name
                }),
                "ID", "Name"
            );
            return View();
        }

        // POST: AdminShowTime/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ID,MovieID,AuditoriumID,StartTime,Price")] ShowTime showTime)
        {
            if (ModelState.IsValid)
            {
                _context.Add(showTime);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Thêm suất chiếu thành công!";
                return RedirectToAction(nameof(Index));
            }

            ViewData["MovieID"] = new SelectList(_context.Movies, "ID", "Title", showTime.MovieID);
            ViewData["AuditoriumID"] = new SelectList(_context.Auditoriums, "ID", "Name", showTime.AuditoriumID);
            TempData["ErrorMessage"] = "Lỗi khi thêm suất chiếu.";
            return View(showTime);
        }

        // GET: AdminShowTime/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var showTime = await _context.ShowTimes.FindAsync(id);
            if (showTime == null) return NotFound();

            ViewData["MovieID"] = new SelectList(_context.Movies, "ID", "Title", showTime.MovieID);
            ViewData["AuditoriumID"] = new SelectList(_context.Auditoriums.Include(a => a.Theater)
                .Select(a => new
                {
                    a.ID,
                    Name = a.Theater.Name + " - Phòng " + a.Name
                }), "ID", "Name", showTime.AuditoriumID);
            return View(showTime);
        }

        // POST: AdminShowTime/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ID,MovieID,AuditoriumID,StartTime,Price")] ShowTime showTime)
        {
            if (id != showTime.ID) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(showTime);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Cập nhật thành công!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.ShowTimes.Any(e => e.ID == showTime.ID))
                        return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = "Lỗi khi cập nhật.";
            return View(showTime);
        }

        // GET: AdminShowTime/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var showTime = await _context.ShowTimes
                .Include(s => s.Movie)
                .Include(s => s.Auditorium)
                    .ThenInclude(a => a.Theater)
                .FirstOrDefaultAsync(m => m.ID == id);

            if (showTime == null) return NotFound();

            return View(showTime);
        }

        // POST: AdminShowTime/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var showTime = await _context.ShowTimes.FindAsync(id);
            if (showTime != null)
            {
                _context.ShowTimes.Remove(showTime);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Xóa thành công!";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
