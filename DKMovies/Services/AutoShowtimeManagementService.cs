using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace DKMovies.Services
{
    public class AutoShowtimeManagementService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AutoShowtimeManagementService> _logger;
        private DateTime _lastWeeklyReset = DateTime.MinValue;

        public AutoShowtimeManagementService(
            IServiceProvider serviceProvider,
            ILogger<AutoShowtimeManagementService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Auto Showtime Management Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndPerformAutoManagement();

                    // Wait for 1 hour before next check
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Auto Showtime Management Service");
                    // Wait 5 minutes before retry on error
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }

        private async Task CheckAndPerformAutoManagement()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.Now;

            // Check if it's time for weekly reset (Sunday 2 AM)
            if (ShouldPerformWeeklyReset(now))
            {
                _logger.LogInformation("Performing weekly auto showtime management");
                await PerformAutoShowtimeManagement(context);
                _lastWeeklyReset = now;
            }

            // Also check for daily optimizations (every day at 3 AM)
            if (now.Hour == 3 && now.Minute < 5)
            {
                _logger.LogInformation("Performing daily showtime optimization");
                await PerformDailyOptimization(context);
            }
        }

        private bool ShouldPerformWeeklyReset(DateTime now)
        {
            return now.DayOfWeek == DayOfWeek.Sunday &&
                   now.Hour == 2 &&
                   now.Minute < 5 &&
                   (now - _lastWeeklyReset).TotalDays >= 6; // Prevent multiple runs
        }

        private async Task PerformAutoShowtimeManagement(ApplicationDbContext context)
        {
            try
            {
                var oneWeekAgo = DateTime.Now.AddDays(-7);

                // ✅ SỬA: PurchaseTime thay vì PurchaseDate
                var moviePerformance = await context.Tickets
                    .Include(t => t.ShowTime)
                    .ThenInclude(st => st.Movie)
                    .Where(t => t.PurchaseTime >= oneWeekAgo &&
                               t.ShowTime != null &&
                               t.ShowTime.Movie != null)
                    .GroupBy(t => t.ShowTime.MovieID)
                    .Select(g => new
                    {
                        MovieID = g.Key,
                        Movie = g.First().ShowTime.Movie,
                        TicketsSold = g.Count(),
                        TotalRevenue = g.Sum(t => t.ShowTime.Price),
                        UniqueShowtimes = g.Select(t => t.ShowTime.ID).Distinct().Count()
                    })
                    .ToListAsync();

                if (!moviePerformance.Any())
                {
                    _logger.LogInformation("No performance data available for auto management");
                    return;
                }

                // Calculate performance thresholds
                var avgRevenue = moviePerformance.Average(x => (double)x.TotalRevenue);
                var avgTickets = moviePerformance.Average(x => x.TicketsSold);

                _logger.LogInformation($"Performance thresholds - Avg Revenue: {avgRevenue:F2}, Avg Tickets: {avgTickets:F2}");

                // Identify high and low performers
                var highPerformers = moviePerformance
                    .Where(x => x.TotalRevenue >= (decimal)(avgRevenue * 1.3) || x.TicketsSold >= avgTickets * 1.3)
                    .OrderByDescending(x => x.TotalRevenue)
                    .Take(3)
                    .ToList();

                var lowPerformers = moviePerformance
                    .Where(x => x.TotalRevenue <= (decimal)(avgRevenue * 0.4) && x.TicketsSold <= avgTickets * 0.4)
                    .ToList();

                int totalAdded = 0, totalRemoved = 0;

                // Remove showtimes for low performers
                foreach (var lowPerformer in lowPerformers)
                {
                    var removed = await RemoveUnderperformingShowtimes(context, lowPerformer.MovieID, lowPerformer.Movie.Title);
                    totalRemoved += removed;
                }

                // Add showtimes for high performers
                foreach (var highPerformer in highPerformers)
                {
                    var added = await AddShowtimesForHighPerformer(context, highPerformer.MovieID, highPerformer.Movie.Title);
                    totalAdded += added;
                }

                await context.SaveChangesAsync();

                _logger.LogInformation($"Auto management completed - Added: {totalAdded}, Removed: {totalRemoved}");

                // Log the management activity
                await LogManagementActivity(context, totalAdded, totalRemoved,
                    $"Weekly Reset - {highPerformers.Count} high performers, {lowPerformers.Count} low performers");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in auto showtime management");
            }
        }

        private async Task<int> RemoveUnderperformingShowtimes(ApplicationDbContext context, int movieId, string movieTitle)
        {
            var now = DateTime.Now;

            // Get future showtimes for this movie
            var futureShowtimes = await context.ShowTimes
                .Where(st => st.MovieID == movieId &&
                            st.StartTime > now.AddHours(2)) // Keep showtimes starting in next 2 hours
                .Include(st => st.Tickets)
                .ToListAsync();

            // Only remove showtimes with no tickets sold, and keep at least 1 showtime
            var showtimesToRemove = futureShowtimes
                .Where(st => !st.Tickets.Any())
                .OrderBy(st => st.StartTime)
                .Take(Math.Max(0, futureShowtimes.Count - 1)) // Keep at least 1
                .ToList();

            if (showtimesToRemove.Any())
            {
                context.ShowTimes.RemoveRange(showtimesToRemove);
                _logger.LogInformation($"Removed {showtimesToRemove.Count} showtimes for low-performing movie: {movieTitle}");
            }

            return showtimesToRemove.Count;
        }

        private async Task<int> AddShowtimesForHighPerformer(ApplicationDbContext context, int movieId, string movieTitle)
        {
            var movie = await context.Movies.FindAsync(movieId);
            if (movie == null) return 0;

            var now = DateTime.Now;
            var addedCount = 0;

            // Generate optimal showtime slots for next week
            var timeSlots = GenerateOptimalTimeSlots(now);

            foreach (var timeSlot in timeSlots.Take(2)) // Add max 2 new showtimes
            {
                var bestAuditorium = await FindBestAvailableAuditorium(context, timeSlot, movie.DurationMinutes);
                if (bestAuditorium == null) continue;

                var newShowtime = new ShowTime
                {
                    MovieID = movieId,
                    AuditoriumID = bestAuditorium.ID,
                    StartTime = timeSlot,
                    DurationMinutes = movie.DurationMinutes,
                    SubtitleLanguageID = await GetDefaultLanguageId(context),
                    Is3D = await ShouldUse3D(context, movieId),
                    Price = await CalculateOptimalPrice(context, movieId)
                };

                context.ShowTimes.Add(newShowtime);
                addedCount++;
            }

            if (addedCount > 0)
            {
                _logger.LogInformation($"Added {addedCount} showtimes for high-performing movie: {movieTitle}");
            }

            return addedCount;
        }

        private List<DateTime> GenerateOptimalTimeSlots(DateTime startDate)
        {
            var timeSlots = new List<DateTime>();
            var baseDate = startDate.AddDays(1).Date; // Start from tomorrow

            // Popular time slots
            var popularTimes = new[] { 15, 18, 21 }; // 3 PM, 6 PM, 9 PM

            for (int day = 0; day < 7; day++)
            {
                var currentDate = baseDate.AddDays(day);

                // Add weekend prime time slots
                if (currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday)
                {
                    timeSlots.Add(currentDate.AddHours(19)); // 7 PM weekend
                    timeSlots.Add(currentDate.AddHours(21)); // 9 PM weekend
                }
                else
                {
                    // Weekday evening slots
                    timeSlots.Add(currentDate.AddHours(18)); // 6 PM weekday
                }
            }

            return timeSlots.OrderBy(t => t).ToList();
        }

        private async Task<Auditorium> FindBestAvailableAuditorium(ApplicationDbContext context, DateTime targetTime, int movieDuration)
        {
            // ✅ SỬA: Bỏ OrderByDescending SeatingCapacity vì property không tồn tại
            var auditoriums = await context.Auditoriums
                .Include(a => a.Theater)
                .ToListAsync();

            foreach (var auditorium in auditoriums)
            {
                var hasConflict = await context.ShowTimes
                    .AnyAsync(st => st.AuditoriumID == auditorium.ID &&
                                   targetTime < st.StartTime.AddMinutes(st.DurationMinutes + 30) &&
                                   targetTime.AddMinutes(movieDuration + 30) > st.StartTime);

                if (!hasConflict)
                    return auditorium;
            }

            return null;
        }

        private async Task<int> GetDefaultLanguageId(ApplicationDbContext context)
        {
            // ✅ SỬA: Check if Languages table exists
            try
            {
                var defaultLang = await context.Languages.FirstOrDefaultAsync();
                return defaultLang?.ID ?? 1;
            }
            catch
            {
                // If Languages table doesn't exist, return default
                return 1;
            }
        }

        private async Task<bool> ShouldUse3D(ApplicationDbContext context, int movieId)
        {
            // Check if this movie has 3D showtimes before
            var has3D = await context.ShowTimes
                .Where(st => st.MovieID == movieId)
                .AnyAsync(st => st.Is3D);

            return has3D;
        }

        private async Task<decimal> CalculateOptimalPrice(ApplicationDbContext context, int movieId)
        {
            // Get average price for this movie
            var avgPrice = await context.ShowTimes
                .Where(st => st.MovieID == movieId)
                .AverageAsync(st => (decimal?)st.Price) ?? 10.0m;

            // Increase price slightly for high-demand movies
            var optimizedPrice = avgPrice * 1.05m;

            // Keep within reasonable bounds
            return Math.Max(5.0m, Math.Min(25.0m, optimizedPrice));
        }

        private async Task PerformDailyOptimization(ApplicationDbContext context)
        {
            try
            {
                // Clean up past showtimes (older than 30 days)
                var cutoffDate = DateTime.Now.AddDays(-30);
                var oldShowtimes = await context.ShowTimes
                    .Where(st => st.StartTime < cutoffDate)
                    .Include(st => st.Tickets)
                    .Where(st => !st.Tickets.Any()) // Only remove showtimes with no tickets
                    .ToListAsync();

                if (oldShowtimes.Any())
                {
                    context.ShowTimes.RemoveRange(oldShowtimes);
                    await context.SaveChangesAsync();
                    _logger.LogInformation($"Cleaned up {oldShowtimes.Count} old showtimes");
                }

                // Update showtime prices based on recent demand
                await UpdatePricesBasedOnDemand(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in daily optimization");
            }
        }

        private async Task UpdatePricesBasedOnDemand(ApplicationDbContext context)
        {
            var now = DateTime.Now;

            // ✅ SỬA: Simplified approach without SeatingCapacity dependency
            var highDemandShowtimes = await context.ShowTimes
                .Include(st => st.Auditorium)
                .Include(st => st.Tickets)
                .Where(st => st.StartTime > now && st.StartTime < now.AddDays(7))
                .ToListAsync();

            foreach (var showtime in highDemandShowtimes)
            {
                var ticketCount = showtime.Tickets.Count;

                // ✅ SỬA: Use simple logic instead of SeatingCapacity
                // Assume average auditorium has 100 seats
                var estimatedCapacity = 100;
                var occupancyRate = (double)ticketCount / estimatedCapacity;

                // If high demand (>80 tickets sold), increase price by 5%
                if (ticketCount > 80)
                {
                    showtime.Price *= 1.05m;
                    showtime.Price = Math.Min(25.0m, showtime.Price); // Cap at 25
                }
                // If low demand (<20 tickets sold), decrease price by 5%
                else if (ticketCount < 20)
                {
                    showtime.Price *= 0.95m;
                    showtime.Price = Math.Max(5.0m, showtime.Price); // Floor at 5
                }
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Updated showtime prices based on demand");
        }

        private async Task LogManagementActivity(ApplicationDbContext context, int added, int removed, string details)
        {
            // If you have a ManagementLog table, log the activity here
            // For now, just log to system logger
            _logger.LogInformation($"Management Activity - Added: {added}, Removed: {removed}, Details: {details}");
        }
    }

    // Extension method to register the service
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAutoShowtimeManagement(this IServiceCollection services)
        {
            services.AddHostedService<AutoShowtimeManagementService>();
            return services;
        }
    }
}