using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DKMovies.Controllers
{
    public class AdminMovieController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminMovieController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: AdminMovie
        public async Task<IActionResult> Index()
        {
            var movies = await _context.Movies
                .Include(m => m.MovieGenres) // nếu có liên kết thể loại
                .ToListAsync();
            return View(movies);
        }

        // GET: AdminMovie/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var movie = await _context.Movies
                .Include(m => m.MovieGenres)
                .FirstOrDefaultAsync(m => m.ID == id);
            if (movie == null) return NotFound();

            return View(movie);
        }

        // GET: AdminMovie/Create
        public IActionResult Create()
        {
            ViewData["GenreID"] = new SelectList(_context.Genres, "ID", "Name");
            return View();
        }

        // POST: AdminMovie/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ID,Title,Description,Duration,ReleaseDate,GenreID,PosterUrl,TrailerUrl")] Movie movie)
        {
            if (ModelState.IsValid)
            {
                _context.Add(movie);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã thêm phim thành công.";
                return RedirectToAction(nameof(Index));
            }
            ViewData["GenreID"] = new SelectList(_context.Genres, "ID", "Name", movie.MovieGenres);
            return View(movie);
        }

        // GET: AdminMovie/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var movie = await _context.Movies.FindAsync(id);
            if (movie == null) return NotFound();

            ViewData["GenreID"] = new SelectList(_context.Genres, "ID", "Name", movie.MovieGenres);
            return View(movie);
        }

        // POST: AdminMovie/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ID,Title,Description,Duration,ReleaseDate,GenreID,PosterUrl,TrailerUrl")] Movie movie)
        {
            if (id != movie.ID) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(movie);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Đã cập nhật phim.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!MovieExists(movie.ID)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }

            ViewData["GenreID"] = new SelectList(_context.Genres, "ID", "Name", movie.MovieGenres);
            return View(movie);
        }

        // GET: AdminMovie/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var movie = await _context.Movies
                .Include(m => m.MovieGenres)
                .FirstOrDefaultAsync(m => m.ID == id);
            if (movie == null) return NotFound();

            return View(movie);
        }

        // POST: AdminMovie/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var movie = await _context.Movies.FindAsync(id);
            if (movie != null)
            {
                _context.Movies.Remove(movie);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã xóa phim.";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool MovieExists(int id)
        {
            return _context.Movies.Any(e => e.ID == id);
        }
    }
}
