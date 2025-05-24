using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DKMovies.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DKMovies.Controllers
{
    [Authorize(Roles = "User")]
    public class WatchlistController : Controller
    {
        private readonly ApplicationDbContext _context;

        public WatchlistController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: /Watchlist/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int movieId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var exists = await _context.WatchList
                .AnyAsync(w => w.UserID == userId && w.MovieID == movieId);

            if (!exists)
            {
                var item = new WatchListSingular
                {
                    MovieID = movieId,
                    UserID = userId,
                    AddedAt = DateTime.Now
                };

                _context.WatchList.Add(item);
                await _context.SaveChangesAsync();

                TempData["ToastSuccess"] = "Đã thêm phim vào danh sách yêu thích.";
            }
            else
            {
                TempData["ToastInfo"] = "Phim đã có trong danh sách.";
            }

            return RedirectToAction("Details", "MoviesList", new { id = movieId });
        }

        // GET: /Watchlist/MyWatchlist
        public async Task<IActionResult> MyWatchlist()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var items = await _context.WatchList
                .Include(w => w.Movie)
                .Where(w => w.UserID == userId)
                .OrderByDescending(w => w.AddedAt)
                .ToListAsync();

            return View(items);
        }

        // POST: /Watchlist/Remove
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int movieId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var entry = await _context.WatchList
                .FirstOrDefaultAsync(w => w.UserID == userId && w.MovieID == movieId);

            if (entry != null)
            {
                _context.WatchList.Remove(entry);
                await _context.SaveChangesAsync();

                TempData["ToastSuccess"] = "Đã xóa khỏi danh sách yêu thích.";
            }

            return RedirectToAction("MyWatchlist");
        }
    }
}
