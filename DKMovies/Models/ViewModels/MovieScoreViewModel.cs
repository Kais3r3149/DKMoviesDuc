// MovieScoreViewModel.cs - Nâng cao
namespace DKMovies.Models.ViewModels
{
    public class MovieScoreViewModel
    {
        public int MovieID { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TicketsSold { get; set; }
        public decimal TotalRevenue { get; set; }
        public double AvgRating { get; set; }
        public double PriorityScore { get; set; }

        // ===== ADDITIONAL PROPERTIES =====
        public string Genre { get; set; } = string.Empty;
        public string Director { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string PosterImagePath { get; set; } = string.Empty;
        public int TotalShowtimes { get; set; }
        public int ReviewCount { get; set; }
        public decimal AverageTicketPrice { get; set; }

        // ===== CALCULATED PROPERTIES =====
        public string FormattedRevenue => TotalRevenue.ToString("N0") + " ₫";
        public string FormattedRating => AvgRating.ToString("F1");
        public string FormattedScore => PriorityScore.ToString("F1");
        public string FormattedDuration => DurationMinutes > 0 ? $"{DurationMinutes} phút" : "N/A";

        // Performance level based on priority score
        public string PerformanceLevel
        {
            get
            {
                if (PriorityScore >= 80) return "Xuất sắc";
                if (PriorityScore >= 60) return "Tốt";
                if (PriorityScore >= 40) return "Trung bình";
                if (PriorityScore >= 20) return "Kém";
                return "Rất kém";
            }
        }

        // Performance color for UI
        public string PerformanceColor
        {
            get
            {
                if (PriorityScore >= 80) return "success";
                if (PriorityScore >= 60) return "primary";
                if (PriorityScore >= 40) return "warning";
                if (PriorityScore >= 20) return "orange";
                return "danger";
            }
        }

        // Revenue per ticket
        public decimal RevenuePerTicket
        {
            get
            {
                return TicketsSold > 0 ? TotalRevenue / TicketsSold : 0;
            }
        }

        // Occupancy rate (if we know theater capacity)
        public double OccupancyRate { get; set; }

        // Trending indicator
        public TrendingDirection Trending { get; set; } = TrendingDirection.Stable;
        public string TrendingIcon
        {
            get
            {
                return Trending switch
                {
                    TrendingDirection.Up => "fas fa-arrow-up text-success",
                    TrendingDirection.Down => "fas fa-arrow-down text-danger",
                    _ => "fas fa-minus text-muted"
                };
            }
        }

        // Days since release
        public int DaysSinceRelease
        {
            get
            {
                if (!ReleaseDate.HasValue) return 0;
                return (DateTime.Now - ReleaseDate.Value).Days;
            }
        }

        // Movie age category
        public string AgeCategory
        {
            get
            {
                var days = DaysSinceRelease;
                if (days <= 7) return "Mới";
                if (days <= 30) return "Gần đây";
                if (days <= 90) return "Phổ biến";
                return "Kinh điển";
            }
        }

        // Profit margin (simplified calculation)
        public decimal ProfitMargin
        {
            get
            {
                // Assuming 30% cost ratio for simplification
                var estimatedCost = TotalRevenue * 0.3m;
                var profit = TotalRevenue - estimatedCost;
                return TotalRevenue > 0 ? (profit / TotalRevenue) * 100 : 0;
            }
        }

        // Recommendation for management
        public string ManagementRecommendation
        {
            get
            {
                if (PriorityScore >= 80 && TicketsSold > 100)
                    return "Tăng số suất chiếu";
                if (PriorityScore >= 60)
                    return "Duy trì hiện tại";
                if (PriorityScore >= 40)
                    return "Cần theo dõi";
                if (PriorityScore >= 20)
                    return "Giảm suất chiếu";
                return "Ngừng chiếu";
            }
        }

        // Star rating HTML for display
        public string StarRatingHtml
        {
            get
            {
                var stars = "";
                for (int i = 1; i <= 5; i++)
                {
                    if (i <= AvgRating)
                        stars += "<i class='fas fa-star text-warning'></i>";
                    else if (i - 0.5 <= AvgRating)
                        stars += "<i class='fas fa-star-half-alt text-warning'></i>";
                    else
                        stars += "<i class='far fa-star text-muted'></i>";
                }
                return stars;
            }
        }

        // Market share percentage (compared to total tickets sold)
        public double MarketSharePercentage { get; set; }

        // Expected revenue for next week (simple projection)
        public decimal ProjectedWeeklyRevenue
        {
            get
            {
                // Simple projection based on current performance
                var dailyAverage = TicketsSold > 0 ? TotalRevenue / Math.Max(DaysSinceRelease, 1) : 0;
                return dailyAverage * 7; // Project for next 7 days
            }
        }

        // Competition level
        public CompetitionLevel Competition { get; set; } = CompetitionLevel.Medium;
        public string CompetitionText
        {
            get
            {
                return Competition switch
                {
                    CompetitionLevel.Low => "Cạnh tranh thấp",
                    CompetitionLevel.Medium => "Cạnh tranh vừa",
                    CompetitionLevel.High => "Cạnh tranh cao",
                    _ => "Không xác định"
                };
            }
        }

        // Audience satisfaction level
        public string AudienceSatisfaction
        {
            get
            {
                if (AvgRating >= 4.5) return "Rất hài lòng";
                if (AvgRating >= 4.0) return "Hài lòng";
                if (AvgRating >= 3.0) return "Bình thường";
                if (AvgRating >= 2.0) return "Không hài lòng";
                return "Rất không hài lòng";
            }
        }
    }

    // ===== ENUMS =====
    public enum TrendingDirection
    {
        Up,
        Down,
        Stable
    }

    public enum CompetitionLevel
    {
        Low,
        Medium,
        High
    }

    // ===== COMPARISON MODEL =====
    public class MovieComparisonViewModel
    {
        public MovieScoreViewModel CurrentPeriod { get; set; }
        public MovieScoreViewModel PreviousPeriod { get; set; }

        public decimal RevenueChange => CurrentPeriod.TotalRevenue - PreviousPeriod.TotalRevenue;
        public double RevenueChangePercentage
        {
            get
            {
                if (PreviousPeriod.TotalRevenue == 0) return 0;
                return ((double)RevenueChange / (double)PreviousPeriod.TotalRevenue) * 100;
            }
        }

        public int TicketChange => CurrentPeriod.TicketsSold - PreviousPeriod.TicketsSold;
        public double RatingChange => CurrentPeriod.AvgRating - PreviousPeriod.AvgRating;

        public bool IsImproving => RevenueChange > 0 && TicketChange > 0;
        public bool IsDecreasing => RevenueChange < 0 && TicketChange < 0;
    }
}