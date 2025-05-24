//Movies Controller
using Microsoft.AspNetCore.Mvc;
using DKMovies.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace DKMovies.Controllers
{
    public class MoviesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MoviesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Movies/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var movie = await _context.Movies
                .Include(m => m.Language)
                .Include(m => m.Country)
                .Include(m => m.Rating)
                .Include(m => m.Director)
                .FirstOrDefaultAsync(m => m.ID == id);

            if (movie == null) return NotFound();

            // ✅ Gán đường dẫn tương đối cho ảnh
            movie.PosterImagePath = "~/assets/images/movie_posters/" + movie.PosterImagePath;
            movie.WallpaperImagePath = "~/assets/images/movie_wallpapers/" + movie.WallpaperImagePath;

            if (movie.Director != null && !string.IsNullOrEmpty(movie.Director.ProfileImagePath))
            {
                movie.Director.ProfileImagePath = "~/assets/images/directors/" + movie.Director.ProfileImagePath;
            }

            // ✅ Nếu có TrailerUrl và là YouTube link, chuyển sang dạng nhúng iframe
            if (!string.IsNullOrEmpty(movie.TrailerUrl) && movie.TrailerUrl.Contains("watch?v="))
            {
                movie.TrailerUrl = movie.TrailerUrl.Replace("watch?v=", "embed/");
            }

            return View(movie);
        }
    }
}
