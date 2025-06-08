using DKMovies.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DKMovies.Models
{
    // 1. COUNTRIES
    public class Country
    {
        [Key]
        [Display(Name = "Country ID")]
        public int ID { get; set; }

        [Required, MaxLength(100)]
        [Display(Name = "Country Name")]
        public string Name { get; set; }

        [MaxLength(255)]
        [Display(Name = "Description")]
        public string Description { get; set; }

        [NotMapped]
        public ICollection<Director> Directors { get; set; }
        [NotMapped]
        public ICollection<Movie> Movies { get; set; }
    }

    // 2. GENRES
    public class Genre
    {
        [Key]
        [Display(Name = "Genre ID")]
        public int ID { get; set; }

        [Required, MaxLength(100)]
        [Display(Name = "Genre Name")]
        public string Name { get; set; }

        [MaxLength(255)]
        [Display(Name = "Description")]
        public string Description { get; set; }

        public ICollection<MovieGenre> MovieGenres { get; set; }
    }

    public class Sale
    {
        [Key]
        public int ID { get; set; }

        [Required]
        public int UserID { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Đang xử lý";

        // Navigation
        public ICollection<SaleDetail> SaleDetails { get; set; }
    }

    public class SaleDetail
    {
        public int ID { get; set; }

        public int SaleID { get; set; }
        public Sale Sale { get; set; }

        public int TheaterConcessionID { get; set; }
        public TheaterConcession TheaterConcession { get; set; }

        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    // 3. RATINGS
    public class Rating
    {
        [Key]
        [Display(Name = "Rating ID")]
        public int ID { get; set; }

        [Required, MaxLength(10)]
        [Display(Name = "Rating Value")]
        public string Value { get; set; }

        [MaxLength(255)]
        [Display(Name = "Description")]
        public string Description { get; set; }

        public ICollection<Movie> Movies { get; set; }
    }

    // 4. LANGUAGES
    public class Language
    {
        [Key]
        [Display(Name = "Language ID")]
        public int ID { get; set; }

        [Required, MaxLength(100)]
        [Display(Name = "Language Name")]
        public string Name { get; set; }

        [Display(Name = "Description")]
        public string Description { get; set; }

        public ICollection<Movie> Movies { get; set; }
        public ICollection<ShowTime> ShowTimes { get; set; }
    }

    // 5. DIRECTORS
    public class Director
    {
        [Key]
        [Display(Name = "Director ID")]
        public int ID { get; set; }

        [Required, MaxLength(255)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; }

        [Display(Name = "Date of Birth")]
        public DateTime? DateOfBirth { get; set; }

        [Display(Name = "Biography")]
        public string Biography { get; set; }

        [Display(Name = "Country")]
        public int? CountryID { get; set; }

        [ForeignKey("CountryID")]
        public Country Country { get; set; }

        [MaxLength(500)]
        [Display(Name = "Profile Image Path")]
        public string? ProfileImagePath { get; set; }

        public ICollection<Movie> Movies { get; set; }
    }

    // 6. USERS

    public class User
    {
        [Key]
        [Display(Name = "User ID")]
        public int ID { get; set; }

        [Required, MaxLength(100)]
        [Display(Name = "Username")]
        public string Username { get; set; }

        [Required, MaxLength(255)]
        [Display(Name = "Email")]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [Display(Name = "Password Hash")]
        public string PasswordHash { get; set; }

        [MaxLength(255)]
        [Display(Name = "Full Name")]
        public string? FullName { get; set; }

        [MaxLength(20)]
        [Display(Name = "Phone Number")]
        [Phone]
        public string? Phone { get; set; }

        [Display(Name = "Birth Date")]
        public DateTime? BirthDate { get; set; }

        [MaxLength(10)]
        [Display(Name = "Gender")]
        public string? Gender { get; set; }

        [MaxLength(500)]
        [Display(Name = "Profile Image Path")]
        public string? ProfileImagePath { get; set; }

        [Display(Name = "Created At")]
        public DateTime CreatedAt { get; set; }

        // ✅ Role Authorization
        [Display(Name = "Role")]
        public int RoleID { get; set; } = 1; // Default to User role

        [ForeignKey("RoleID")]
        public virtual Role? Role { get; set; }

        // ✅ Security and verification fields
        [Display(Name = "Email Confirmed")]
        public bool EmailConfirmed { get; set; } = false;

        [MaxLength(100)]
        [Display(Name = "Confirmation Code")]
        public string? ConfirmationCode { get; set; }

        [Display(Name = "Two-Factor Enabled")]
        public bool TwoFactorEnabled { get; set; } = false;

        [MaxLength(100)]
        [Display(Name = "2FA Code")]
        public string? TwoFactorCode { get; set; }

        [Display(Name = "2FA Expiry Time")]
        public DateTime? TwoFactorExpiry { get; set; }

        // ✅ Account Status
        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Last Login")]
        public DateTime? LastLoginAt { get; set; }

        // ✅ Navigation properties
        public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
        public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();
        public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
        public virtual ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
        public virtual ICollection<WatchlistItem> WatchlistItems { get; set; } = new List<WatchlistItem>();
        public virtual ICollection<MovieUserFavourite> MovieUserFavourites { get; set; } = new List<MovieUserFavourite>();
        public virtual ICollection<WatchListSingular> WatchList { get; set; } = new List<WatchListSingular>();

        // ✅ Helper methods for role checking
        [NotMapped]
        public bool IsAdmin => RoleID == 2;

        [NotMapped]
        public bool IsUser => RoleID == 1;

        [NotMapped]
        public string RoleName => Role?.Name ?? (RoleID == 1 ? "User" : RoleID == 2 ? "Admin" : "Unknown");

        // ✅ Helper methods for display
        [NotMapped]
        [Display(Name = "Display Name")]
        public string DisplayName => !string.IsNullOrEmpty(FullName) ? FullName : Username;

        [NotMapped]
        [Display(Name = "Profile Picture")]
        public string ProfilePictureUrl => !string.IsNullOrEmpty(ProfileImagePath)
            ? $"/images/profiles/{ProfileImagePath}"
            : "/images/default-avatar.png";

        [NotMapped]
        [Display(Name = "Age")]
        public int? Age
        {
            get
            {
                if (BirthDate == null) return null;
                var today = DateTime.Today;
                var age = today.Year - BirthDate.Value.Year;
                if (BirthDate.Value.Date > today.AddYears(-age)) age--;
                return age;
            }
        }

        [NotMapped]
        [Display(Name = "Member Since")]
        public string MemberSince => CreatedAt.ToString("MMM yyyy");

        [NotMapped]
        [Display(Name = "Account Status")]
        public string AccountStatus
        {
            get
            {
                if (!IsActive) return "Deactivated";
                if (!EmailConfirmed) return "Pending Verification";
                return "Active";
            }
        }

        // ✅ Validation methods
        public bool IsValidForLogin()
        {
            return IsActive && EmailConfirmed;
        }

        public bool CanPerformAction(string action)
        {
            if (!IsValidForLogin()) return false;

            return action.ToLower() switch
            {
                "book_tickets" => true,
                "write_reviews" => true,
                "manage_profile" => true,
                "manage_movies" => IsAdmin,
                "manage_users" => IsAdmin,
                "manage_theaters" => IsAdmin,
                "view_reports" => IsAdmin,
                _ => false
            };
        }

        // ✅ Update last login
        public void UpdateLastLogin()
        {
            LastLoginAt = DateTime.UtcNow;
        }

        // ✅ Generate display initials for avatar
        [NotMapped]
        public string Initials
        {
            get
            {
                if (!string.IsNullOrEmpty(FullName))
                {
                    var nameParts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (nameParts.Length >= 2)
                        return $"{nameParts[0][0]}{nameParts[^1][0]}".ToUpper();
                    if (nameParts.Length == 1)
                        return nameParts[0][0].ToString().ToUpper();
                }
                return Username[0].ToString().ToUpper();
            }
        }
    }

    // ✅ Role model (if not exists)
    public class Role
    {
        [Key]
        public int ID { get; set; }

        [Required, MaxLength(50)]
        [Display(Name = "Role Name")]
        public string Name { get; set; }

        [MaxLength(255)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        // Navigation property
        public virtual ICollection<User> Users { get; set; } = new List<User>();
    }

    // 7. THEATERS
    public class Theater
    {
        [Key]
        [Display(Name = "Theater ID")]
        public int ID { get; set; }

        [Required, MaxLength(255)]
        [Display(Name = "Theater Name")]
        public string Name { get; set; }

        [Required, MaxLength(255)]
        [Display(Name = "Location")]
        public string Location { get; set; }

        [MaxLength(20)]
        [Phone]
        [Display(Name = "Phone Number")]
        public string Phone { get; set; }

        public ICollection<Auditorium> Auditoriums { get; set; }
        public ICollection<Employee> Employees { get; set; }
    }

    // 8. AUDITORIUMS
    public class Auditorium
    {
        [Key]
        [Display(Name = "Auditorium ID")]
        public int ID { get; set; }

        [Display(Name = "Theater")]
        public int TheaterID { get; set; }

        [ForeignKey("TheaterID")]
        [Display(Name = "Theater")]
        public Theater Theater { get; set; }

        [Required, MaxLength(50)]
        [Display(Name = "Auditorium Name")]
        public string Name { get; set; }

        [Display(Name = "Capacity")]
        public int Capacity { get; set; }

        public ICollection<Seat> Seats { get; set; }
        public ICollection<ShowTime> ShowTimes { get; set; }
    }

    // 9. SEATS
    public class Seat
    {
        [Key]
        [Display(Name = "Seat ID")]
        public int ID { get; set; }

        [Display(Name = "Auditorium")]
        public int AuditoriumID { get; set; }

        [ForeignKey("AuditoriumID")]
        [Display(Name = "Auditorium")]
        public Auditorium Auditorium { get; set; }

        [Required, MaxLength(1)]
        [Display(Name = "Row Label")]
        public string RowLabel { get; set; }

        [Display(Name = "Seat Number")]
        public int SeatNumber { get; set; }

        public ICollection<TicketSeat> TicketSeats { get; set; }
    }

    // 10. MOVIES
    public class Movie
    {
        [Key]
        [Display(Name = "Movie ID")]
        public int ID { get; set; }

        [Required, MaxLength(255)]
        [Display(Name = "Movie Title")]
        public string Title { get; set; }

        [Display(Name = "Description")]
        public string Description { get; set; }

        [Display(Name = "Duration (Minutes)")]
        public int DurationMinutes { get; set; }

        [ForeignKey("RatingID")]
        [Display(Name = "Rating")]
        public int RatingID { get; set; }

        public Rating Rating { get; set; }

        [Display(Name = "Release Date")]
        public DateTime? ReleaseDate { get; set; }

        [ForeignKey("LanguageID")]
        [Display(Name = "Language")]
        public int LanguageID { get; set; }

        public Language Language { get; set; }

        [ForeignKey("CountryID")]
        [Display(Name = "Country")]
        public int? CountryID { get; set; }

        public Country Country { get; set; }

        [ForeignKey("DirectorID")]
        [Display(Name = "Director")]
        public int? DirectorID { get; set; }

        public Director Director { get; set; }

        [Display(Name = "Poster Image Path")]
        public string? PosterImagePath { get; set; }

        [Display(Name = "Wallpaper Image Path")]
        public string? WallpaperImagePath { get; set; }

        public string TrailerUrl { get; set; }
        public ICollection<MovieGenre> MovieGenres { get; set; }
        public ICollection<ShowTime> ShowTimes { get; set; }
        public ICollection<Review> Reviews { get; set; }
    }

    // 11. MOVIEGENRE
    public class MovieGenre
    {
        [Key]
        [Display(Name = "Movie-Genre ID")]
        public int ID { get; set; }

        [Display(Name = "Movie ID")]
        public int MovieID { get; set; }

        [Display(Name = "Genre ID")]
        public int GenreID { get; set; }

        [ForeignKey("MovieID")]
        public Movie Movie { get; set; }

        [ForeignKey("GenreID")]
        public Genre Genre { get; set; }
    }

    public class MovieUserFavourite
    {
        [Key]
        [Display(Name = "Favourite ID")]
        public int ID { get; set; }

        [Display(Name = "User")]
        public int UserID { get; set; }

        [ForeignKey("UserID")]
        public User User { get; set; }

        [Display(Name = "Movie ID")]
        public int MovieID { get; set; }

        [ForeignKey("MovieID")]
        public Movie Movie { get; set; }
    }

    public class WatchListSingular
    {
        [Key]
        [Display(Name = "Watchlist ID")]
        public int ID { get; set; }

        [Display(Name = "User")]
        public int UserID { get; set; }

        [ForeignKey("UserID")]
        public User User { get; set; }

        [Display(Name = "Movie ID")]
        public int MovieID { get; set; }

        [ForeignKey("MovieID")]
        public Movie Movie { get; set; }

        [Display(Name = "Added At")]
        public DateTime AddedAt { get; set; } = DateTime.Now;
    }

    // 12. SHOWTIMES
    [Table("ShowTimes")]
    public class ShowTime
    {
        [Key]
        [Display(Name = "Showtime ID")]
        public int ID { get; set; }

        [Display(Name = "Movie ID")]
        public int MovieID { get; set; } = 0;
        [ValidateNever]
        [ForeignKey("MovieID")]
        public Movie Movie { get; set; }

        [Display(Name = "Auditorium ID")]
        public int AuditoriumID { get; set; } = 0;
        [ValidateNever]
        [ForeignKey("AuditoriumID")]
        public Auditorium Auditorium { get; set; }

        [Display(Name = "Start Time")]
        public DateTime StartTime { get; set; }

        [Display(Name = "Duration (minutes)")]
        public int DurationMinutes { get; set; }

        [Display(Name = "Subtitle Language ID")]
        public int? SubtitleLanguageID { get; set; }
        [ValidateNever]
        [ForeignKey("SubtitleLanguageID")]
        public Language? SubtitleLanguage { get; set; }

        [Display(Name = "3D")]
        public bool Is3D { get; set; }

        [Display(Name = "Price")]
        public decimal Price { get; set; }
        [ValidateNever]
        public ICollection<Ticket> Tickets { get; set; }
    }

    public enum TicketStatus
    {
        PENDING,    // Chờ thanh toán/xác nhận
        CONFIRMED,  // Đã xác nhận (thanh toán tại quầy)
        PAID,       // Đã thanh toán online
        CANCELLED   // Đã hủy
    }

    public class Ticket
    {
        [Key]
        [Display(Name = "Ticket ID")]
        public int ID { get; set; }

        [Display(Name = "User ID")]
        public int UserID { get; set; }

        [ForeignKey("UserID")]
        public User User { get; set; }

        [Display(Name = "Showtime ID")]
        public int ShowTimeID { get; set; }

        [ForeignKey("ShowTimeID")]
        public ShowTime ShowTime { get; set; }

        [Display(Name = "Purchase Time")]
        public DateTime PurchaseTime { get; set; }

        [Display(Name = "Status")]
        public TicketStatus Status { get; set; }

        // ✅ THÊM: Payment fields theo database
        [MaxLength(50)]
        [Display(Name = "Payment Method")]
        public string? PaymentMethod { get; set; }

        [Display(Name = "Payment Time")]
        public DateTime? PaymentTime { get; set; }

        [MaxLength(255)]
        [Display(Name = "Stripe Session ID")]
        public string? StripeSessionId { get; set; }

        [Required]
        [Column(TypeName = "decimal(10,2)")]
        [Display(Name = "Total Price")]
        public decimal TotalPrice { get; set; }

        // Navigation properties
        public ICollection<TicketPayment> TicketPayments { get; set; } = new List<TicketPayment>();
        public ICollection<TicketSeat> TicketSeats { get; set; } = new List<TicketSeat>();
        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }

    public class TicketSeat
    {
        [Key]
        [Display(Name = "Ticket Seat ID")]
        public int ID { get; set; }

        [Display(Name = "Ticket ID")]
        public int TicketID { get; set; }

        [ForeignKey("TicketID")]
        public Ticket Ticket { get; set; }

        [Display(Name = "Seat ID")]
        public int SeatID { get; set; }

        [ForeignKey("SeatID")]
        public Seat Seat { get; set; }
    }

    // 14. PAYMENT METHODS
    public class PaymentMethod
    {
        [Key]
        [Display(Name = "Payment Method ID")]
        public int ID { get; set; }

        [Required, MaxLength(50)]
        [Display(Name = "Payment Method Name")]
        public string Name { get; set; }

        [MaxLength(255)]
        [Display(Name = "Description")]
        public string Description { get; set; }

        public ICollection<TicketPayment> TicketPayments { get; set; }
    }

    // 15. TICKET PAYMENTS
    public class TicketPayment
    {
        [Key]
        [Display(Name = "Payment ID")]
        public int ID { get; set; }

        [Display(Name = "Ticket ID")]
        public int TicketID { get; set; }

        [ForeignKey("TicketID")]
        public Ticket Ticket { get; set; }

        [Display(Name = "Payment Method ID")]
        public int MethodID { get; set; }

        [ForeignKey("MethodID")]
        public PaymentMethod PaymentMethod { get; set; }

        [Required, MaxLength(50)]
        [Display(Name = "Payment Status")]
        public string PaymentStatus { get; set; }

        [Display(Name = "Paid Amount")]
        public decimal? PaidAmount { get; set; }

        [Display(Name = "Paid At")]
        public DateTime? PaidAt { get; set; }
    }

    // 16. EMPLOYEE ROLES
    public class EmployeeRole
    {
        [Key]
        [Display(Name = "Role ID")]
        public int ID { get; set; }

        [Required, MaxLength(100)]
        [Display(Name = "Role Name")]
        public string Name { get; set; }

        [MaxLength(255)]
        [Display(Name = "Description")]
        public string Description { get; set; }

        public ICollection<Employee> Employees { get; set; }
    }

    // 17. EMPLOYEES
    public class Employee
    {
        [Key]
        [Display(Name = "Employee ID")]
        public int ID { get; set; }

        [Display(Name = "Theater ID")]
        public int TheaterID { get; set; }

        [ForeignKey("TheaterID")]
        public Theater Theater { get; set; }

        // ✅ SỬA: Giữ RoleID để phù hợp với database
        [Display(Name = "Role ID")]
        public int RoleID { get; set; }

        [ForeignKey("RoleID")]
        public EmployeeRole Role { get; set; }

        [Required, MaxLength(255)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; }

        [Required, MaxLength(255)]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [MaxLength(20)]
        [Display(Name = "Phone Number")]
        public string Phone { get; set; }

        [MaxLength(10)]
        [Display(Name = "Gender")]
        public string Gender { get; set; }

        // ✅ SỬA: Giữ DateOfBirth để phù hợp với database
        [Display(Name = "Date of Birth")]
        public DateTime? DateOfBirth { get; set; }

        [MaxLength(50)]
        [Display(Name = "Citizen ID")]
        public string CitizenID { get; set; }

        [MaxLength(255)]
        [Display(Name = "Address")]
        public string Address { get; set; }

        [Display(Name = "Hire Date")]
        public DateTime HireDate { get; set; }

        [Display(Name = "Salary")]
        public decimal Salary { get; set; }

        [MaxLength(500)]
        [Display(Name = "Profile Image Path")]
        public string? ProfileImagePath { get; set; }

        // ✅ THÊM: Thuộc tính không có trong database hiện tại nhưng cần cho logic
        [NotMapped]
        [Display(Name = "Employee Code")]
        public string EmployeeCode => $"EMP{ID:D4}"; // Auto-generated: EMP0001, EMP0002...

        [NotMapped]
        [Display(Name = "Is Active")]
        public bool IsActive => true; // Default true, có thể thêm vào DB sau

        [NotMapped]
        [Display(Name = "Created At")]
        public DateTime CreatedAt => HireDate; // Sử dụng HireDate làm Created date

        // ✅ THÊM: Helper property để display role name
        [NotMapped]
        [Display(Name = "Position")]
        public string Position => Role?.Name ?? "Unknown";

        public ICollection<Admin> Admins { get; set; }
    }


    // 19. CONCESSIONS
    public class Concession
    {
        [Key]
        [Display(Name = "Concession ID")]
        public int ID { get; set; }

        [Required]
        [MaxLength(100)]
        [Display(Name = "Concession Name")]
        public string Name { get; set; }

        [MaxLength(500)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [MaxLength(255)]
        [Display(Name = "Image Path")]
        public string? ImagePath { get; set; }

        // ✅ SỬA: Giữ IsActive để phù hợp với database
        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        // ✅ THÊM: Category để phù hợp với database
        [MaxLength(50)]
        [Display(Name = "Category")]
        public string? Category { get; set; } = "Khác";

        // ✅ KHÔNG CÓ: Price và StockQuantity (được quản lý qua TheaterConcession)
        // Database không có trường Price và StockQuantity trực tiếp trong Concessions
        // Chúng được quản lý thông qua bảng TheaterConcessions

        // Navigation properties
        public ICollection<TheaterConcession> TheaterConcessions { get; set; } = new List<TheaterConcession>();

        // ✅ THÊM: Helper properties để hiển thị
        [NotMapped]
        [Display(Name = "Average Price")]
        public decimal AveragePrice => TheaterConcessions?.Any() == true
            ? TheaterConcessions.Average(tc => tc.Price)
            : 0;

        [NotMapped]
        [Display(Name = "Total Stock")]
        public int TotalStock => TheaterConcessions?.Sum(tc => tc.StockLeft) ?? 0;

        [NotMapped]
        [Display(Name = "Available Theaters")]
        public int AvailableTheaters => TheaterConcessions?.Count(tc => tc.IsAvailable) ?? 0;
    }



    // 20. THEATER CONCESSIONS
    public class TheaterConcession
    {
        [Key]
        public int ID { get; set; }

        [Required]
        public int TheaterID { get; set; }

        [Required]
        public int ConcessionID { get; set; }

        [Required]
        [Column(TypeName = "decimal(8,2)")]
        public decimal Price { get; set; }

        // ✅ SỬA: Giữ StockLeft để phù hợp với database
        [Required]
        [Display(Name = "Stock Left")]
        public int StockLeft { get; set; }

        public bool IsAvailable { get; set; } = true;

        // Navigation properties
        [ForeignKey("TheaterID")]
        public virtual Theater Theater { get; set; }

        [ForeignKey("ConcessionID")]
        public virtual Concession Concession { get; set; }

        public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
        public virtual ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
        public virtual ICollection<SaleDetail> SaleDetails { get; set; } = new List<SaleDetail>();
    }


    public class CartItem
    {
        [Key]
        public int ID { get; set; }

        [Required]
        public int UserID { get; set; }

        [ForeignKey("UserID")]
        public User User { get; set; }

        [Required]
        public int TheaterConcessionID { get; set; }

        [ForeignKey("TheaterConcessionID")]
        public TheaterConcession TheaterConcession { get; set; }

        [Required]
        [Range(1, 100)]
        public int Quantity { get; set; }

        [Display(Name = "Thời gian thêm")]
        public DateTime AddedAt { get; set; } = DateTime.Now;

        [NotMapped]
        public decimal SubTotal => (TheaterConcession?.Price ?? 0) * Quantity;
    }

    // 21. ORDERS
    public class Order
    {
        [Key]
        [Display(Name = "Order ID")]
        public int ID { get; set; }

        [Display(Name = "User ID")]
        public int UserID { get; set; }

        [ForeignKey("UserID")]
        public User User { get; set; }

        [Display(Name = "Order Time")]
        public DateTime OrderTime { get; set; }

        [Display(Name = "Total Amount")]
        public decimal TotalAmount { get; set; }

        [Display(Name = "Order Status")]
        public string OrderStatus { get; set; }

        public ICollection<OrderItem> OrderItems { get; set; }
        public ICollection<OrderPayment> OrderPayments { get; set; }
    }

    // 22. ORDER ITEMS
    public class OrderItem
    {
        [Key]
        [Display(Name = "Order Item ID")]
        public int ID { get; set; }

        // ✅ SỬA: Có thể thuộc về Order hoặc Ticket
        [Display(Name = "Order ID")]
        public int? OrderID { get; set; }

        [ForeignKey("OrderID")]
        public Order? Order { get; set; }

        [Display(Name = "Ticket ID")]
        public int? TicketID { get; set; }

        [ForeignKey("TicketID")]
        public Ticket? Ticket { get; set; }

        // ✅ SỬA: Link với TheaterConcession theo database
        [Display(Name = "Theater Concession ID")]
        public int TheaterConcessionID { get; set; }

        [ForeignKey("TheaterConcessionID")]
        public TheaterConcession TheaterConcession { get; set; }

        [Required]
        [Range(1, 50)]
        [Display(Name = "Quantity")]
        public int Quantity { get; set; }

        [Required]
        [Column(TypeName = "decimal(8,2)")]
        [Display(Name = "Price At Purchase")]
        public decimal PriceAtPurchase { get; set; }
    }

    // 23. ORDER PAYMENTS
    public class OrderPayment
    {
        [Key]
        [Display(Name = "Payment ID")]
        public int ID { get; set; }

        [Display(Name = "Order ID")]
        public int OrderID { get; set; }

        [ForeignKey("OrderID")]
        public Order Order { get; set; }

        [Display(Name = "Payment Method ID")]
        public int MethodID { get; set; }

        [ForeignKey("MethodID")]
        public PaymentMethod PaymentMethod { get; set; }

        [Display(Name = "Payment Status")]
        public string PaymentStatus { get; set; }

        [Display(Name = "Paid Amount")]
        public decimal? PaidAmount { get; set; }

        [Display(Name = "Paid At")]
        public DateTime? PaidAt { get; set; }
    }


    // 24. REVIEWS
    public class Review
    {
        [Key]
        [Display(Name = "Review ID")]
        public int ID { get; set; }

        [Display(Name = "Movie ID")]
        [Required]
        public int MovieID { get; set; }

        [ForeignKey("MovieID")]
        public Movie Movie { get; set; }

        [Display(Name = "User ID")]
        [Required]
        public int UserID { get; set; }

        [ForeignKey("UserID")]
        public User User { get; set; }

        [Display(Name = "Rating")]
        [Range(1, 5, ErrorMessage = "Rating phải từ 1 đến 5.")]
        public int Rating { get; set; }

        [Display(Name = "Comment")]
        [Required(ErrorMessage = "Bạn phải nhập nội dung đánh giá.")]
        [StringLength(1000, ErrorMessage = "Đánh giá không vượt quá 1000 ký tự.")]
        public string Comment { get; set; }

        [Display(Name = "Created At")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Display(Name = "Is Approved")]
        public bool IsApproved { get; set; } = false;
    }

    public class WatchlistItem
    {
        public int ID { get; set; }

        [Required]
        public int MovieID { get; set; }
        public Movie Movie { get; set; }

        [Required]
        public int UserID { get; set; }
        public User User { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }


    // 25. ADMINS
    public class Admin
    {
        [Key]
        [Display(Name = "Admin ID")]
        public int ID { get; set; }

        [Display(Name = "Employee ID")]
        public int EmployeeID { get; set; }

        [ForeignKey("EmployeeID")]
        public Employee Employee { get; set; }

        [Required, MaxLength(100)]
        [Display(Name = "Username")]
        public string Username { get; set; }

        [Required]
        [Display(Name = "Password Hash")]
        public string PasswordHash { get; set; }

        [Display(Name = "Created At")]
        public DateTime CreatedAt { get; set; }
    }

}
