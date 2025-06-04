using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using Stripe;
using DKMovies.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace DKMovies.Controllers
{
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public PaymentController(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;

            // Initialize Stripe
            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> CreateCheckoutSession(int ticketId)
        {
            try
            {
                // Get ticket with all related data
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
                    return Forbid("Bạn không có quyền thanh toán vé này.");
                }

                // Check ticket status
                if (ticket.Status != TicketStatus.PENDING)
                {
                    TempData["ToastError"] = "Vé này không ở trạng thái chờ thanh toán.";
                    return RedirectToAction("OrderConfirmation", "Tickets", new { ticketId });
                }

                // Check payment window (15 minutes)
                if (ticket.PurchaseTime.AddMinutes(15) < DateTime.Now)
                {
                    await CancelExpiredTicket(ticket);

                    TempData["ToastError"] = "Vé đã hết hạn thanh toán (15 phút). Vui lòng đặt vé lại.";
                    return RedirectToAction("OrderTicket", "Tickets", new { id = ticket.ShowTime.MovieID });
                }

                // ✅ Build Stripe line items - FIXED: Removed Images field completely
                var lineItems = new List<SessionLineItemOptions>();

                // Add movie tickets
                var seatNames = ticket.TicketSeats.Select(ts => $"{ts.Seat.RowLabel}{ts.Seat.SeatNumber}").ToList();
                var seatDescription = $"Ghế: {string.Join(", ", seatNames)}";

                lineItems.Add(new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(ticket.ShowTime.Price * 100), // Convert to cents
                        Currency = "usd",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Vé xem phim - {ticket.ShowTime.Movie.Title}",
                            Description = $"{seatDescription}\nSuất chiếu: {ticket.ShowTime.StartTime:dd/MM/yyyy HH:mm}\nRạp: {ticket.ShowTime.Auditorium.Theater.Name} - {ticket.ShowTime.Auditorium.Name}"
                            // ✅ REMOVED: Images field completely - this was causing the error
                        }
                    },
                    Quantity = ticket.TicketSeats.Count
                });

                // ✅ Add concession items - FIXED: Removed Images field
                if (ticket.OrderItems?.Any() == true)
                {
                    var concessionGroups = ticket.OrderItems
                        .GroupBy(oi => oi.TheaterConcession.Concession.Name)
                        .ToList();

                    foreach (var group in concessionGroups)
                    {
                        var firstItem = group.First();
                        var totalQuantity = group.Sum(g => g.Quantity);

                        lineItems.Add(new SessionLineItemOptions
                        {
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                UnitAmount = (long)(firstItem.PriceAtPurchase * 100),
                                Currency = "usd",
                                ProductData = new SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = firstItem.TheaterConcession.Concession.Name,
                                    Description = firstItem.TheaterConcession.Concession.Description ?? "Đồ ăn, thức uống"
                                    // ✅ REMOVED: Images field completely
                                }
                            },
                            Quantity = totalQuantity
                        });
                    }
                }

                // Create Stripe checkout session
                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string>
                    {
                        "card"
                    },
                    LineItems = lineItems,
                    Mode = "payment",
                    SuccessUrl = Url.Action("PaymentSuccess", "Payment", new { ticketId }, Request.Scheme),
                    CancelUrl = Url.Action("PaymentCancel", "Payment", new { ticketId }, Request.Scheme),
                    CustomerEmail = ticket.User.Email,
                    Metadata = new Dictionary<string, string>
                    {
                        { "ticket_id", ticketId.ToString() },
                        { "user_id", ticket.UserID.ToString() }
                    },
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30), // ✅ FIXED: Minimum 30 minutes required by Stripe
                    PaymentIntentData = new SessionPaymentIntentDataOptions
                    {
                        Description = $"Thanh toán vé xem phim #{ticketId} - {ticket.ShowTime.Movie.Title}"
                    }
                };

                var service = new SessionService();
                var session = await service.CreateAsync(options);

                // Store Stripe session ID
                ticket.StripeSessionId = session.Id;
                _context.Update(ticket);
                await _context.SaveChangesAsync();

                // Redirect to Stripe checkout
                Response.Headers.Add("Location", session.Url);
                return new StatusCodeResult(303);
            }
            catch (StripeException ex)
            {
                // ✅ Enhanced error logging for debugging
                Console.WriteLine($"❌ Stripe Error: {ex.Message}");
                Console.WriteLine($"   Error Type: {ex.StripeError?.Type}");
                Console.WriteLine($"   Error Code: {ex.StripeError?.Code}");
                Console.WriteLine($"   Error Param: {ex.StripeError?.Param}");

                TempData["ToastError"] = $"Lỗi thanh toán: {ex.Message}";
                return RedirectToAction("PaymentSelection", "Tickets", new { ticketId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ General Error: {ex.Message}");
                Console.WriteLine($"   Stack Trace: {ex.StackTrace}");

                TempData["ToastError"] = "Có lỗi xảy ra khi tạo phiên thanh toán. Vui lòng thử lại.";
                return RedirectToAction("PaymentSelection", "Tickets", new { ticketId });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PaymentSuccess(int ticketId)
        {
            try
            {
                var ticket = await _context.Tickets
                    .Include(t => t.ShowTime)
                        .ThenInclude(st => st.Movie)
                    .Include(t => t.User)
                    .Include(t => t.TicketSeats)
                        .ThenInclude(ts => ts.Seat)
                    .Include(t => t.OrderItems)
                        .ThenInclude(oi => oi.TheaterConcession)
                            .ThenInclude(tc => tc.Concession)
                    .FirstOrDefaultAsync(t => t.ID == ticketId);

                if (ticket == null)
                {
                    TempData["ToastError"] = "Không tìm thấy vé.";
                    return RedirectToAction("Index", "MoviesList");
                }

                // Verify Stripe payment if session exists
                if (!string.IsNullOrEmpty(ticket.StripeSessionId))
                {
                    var sessionService = new SessionService();
                    var session = await sessionService.GetAsync(ticket.StripeSessionId);

                    if (session.PaymentStatus == "paid")
                    {
                        // Only update if not already paid (prevent duplicate processing)
                        if (ticket.Status == TicketStatus.PENDING)
                        {
                            // Update ticket status
                            ticket.Status = TicketStatus.PAID;
                            ticket.PaymentTime = DateTime.Now;

                            // Create payment record
                            var ticketPayment = new TicketPayment
                            {
                                TicketID = ticket.ID,
                                MethodID = 1, // Stripe payment method ID
                                PaymentStatus = "Completed",
                                PaidAmount = ticket.TotalPrice,
                                PaidAt = DateTime.Now
                            };

                            _context.TicketPayments.Add(ticketPayment);
                            _context.Update(ticket);
                            await _context.SaveChangesAsync();

                            // Send confirmation email
                            await SendConfirmationEmail(ticket);

                            TempData["ToastSuccess"] = "Thanh toán thành công! Vé của bạn đã được xác nhận.";
                        }
                        else
                        {
                            TempData["ToastInfo"] = "Vé đã được thanh toán trước đó.";
                        }
                    }
                    else
                    {
                        TempData["ToastWarning"] = "Thanh toán chưa hoàn tất. Vui lòng kiểm tra lại.";
                    }
                }

                return RedirectToAction("OrderConfirmation", "Tickets", new { ticketId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Payment Success Error: {ex.Message}");
                TempData["ToastError"] = "Có lỗi xảy ra khi xác nhận thanh toán.";
                return RedirectToAction("OrderConfirmation", "Tickets", new { ticketId });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PaymentCancel(int ticketId)
        {
            try
            {
                var ticket = await _context.Tickets
                    .Include(t => t.ShowTime)
                    .FirstOrDefaultAsync(t => t.ID == ticketId);

                if (ticket != null)
                {
                    TempData["ToastWarning"] = "Thanh toán đã bị hủy. Bạn vẫn có thể hoàn tất thanh toán trước khi hết hạn.";

                    // Check if still within payment window
                    if (ticket.PurchaseTime.AddMinutes(15) >= DateTime.Now)
                    {
                        return RedirectToAction("PaymentSelection", "Tickets", new { ticketId });
                    }
                    else
                    {
                        // Cancel expired ticket
                        await CancelExpiredTicket(ticket);
                        TempData["ToastError"] = "Vé đã hết hạn thanh toán và bị hủy.";
                        return RedirectToAction("OrderTicket", "Tickets", new { id = ticket.ShowTime.MovieID });
                    }
                }

                return RedirectToAction("Index", "MoviesList");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Payment Cancel Error: {ex.Message}");
                TempData["ToastError"] = "Có lỗi xảy ra.";
                return RedirectToAction("Index", "MoviesList");
            }
        }

        // ✅ Webhook to handle Stripe events
        [HttpPost]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    _configuration["Stripe:WebhookSecret"]
                );

                Console.WriteLine($"🔍 Received Stripe webhook: {stripeEvent.Type}");

                // ✅ Handle checkout session completed
                if (stripeEvent.Type == "checkout.session.completed")
                {
                    var session = stripeEvent.Data.Object as Session;

                    if (session?.Metadata?.ContainsKey("ticket_id") == true)
                    {
                        var ticketId = int.Parse(session.Metadata["ticket_id"]);

                        var ticket = await _context.Tickets
                            .Include(t => t.User)
                            .Include(t => t.ShowTime)
                                .ThenInclude(st => st.Movie)
                            .Include(t => t.TicketSeats)
                                .ThenInclude(ts => ts.Seat)
                            .Include(t => t.OrderItems)
                                .ThenInclude(oi => oi.TheaterConcession)
                                    .ThenInclude(tc => tc.Concession)
                            .FirstOrDefaultAsync(t => t.ID == ticketId);

                        if (ticket != null && ticket.Status == TicketStatus.PENDING)
                        {
                            ticket.Status = TicketStatus.PAID;
                            ticket.PaymentTime = DateTime.Now;

                            var ticketPayment = new TicketPayment
                            {
                                TicketID = ticket.ID,
                                MethodID = 1, // Stripe
                                PaymentStatus = "Completed",
                                PaidAmount = ticket.TotalPrice,
                                PaidAt = DateTime.Now
                            };

                            _context.TicketPayments.Add(ticketPayment);
                            _context.Update(ticket);
                            await _context.SaveChangesAsync();

                            Console.WriteLine($"✅ Webhook: Updated ticket {ticketId} to PAID status");

                            // Send confirmation email
                            await SendConfirmationEmail(ticket);
                        }
                    }
                }
                // ✅ Handle expired sessions
                else if (stripeEvent.Type == "checkout.session.expired")
                {
                    var session = stripeEvent.Data.Object as Session;

                    if (session?.Metadata?.ContainsKey("ticket_id") == true)
                    {
                        var ticketId = int.Parse(session.Metadata["ticket_id"]);
                        var ticket = await _context.Tickets
                            .Include(t => t.OrderItems)
                            .FirstOrDefaultAsync(t => t.ID == ticketId);

                        if (ticket != null && ticket.Status == TicketStatus.PENDING)
                        {
                            await CancelExpiredTicket(ticket);
                            Console.WriteLine($"✅ Webhook: Cancelled expired ticket {ticketId}");
                        }
                    }
                }

                return Ok();
            }
            catch (StripeException ex)
            {
                Console.WriteLine($"❌ Stripe webhook error: {ex.Message}");
                return BadRequest($"Webhook error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Webhook internal error: {ex.Message}");
                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
                return StatusCode(500, $"Internal error: {ex.Message}");
            }
        }

        // ✅ Helper method to cancel expired ticket and restore stock
        private async Task CancelExpiredTicket(Ticket ticket)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                Console.WriteLine($"🔄 Cancelling expired ticket {ticket.ID}");

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
                            Console.WriteLine($"📦 Restored {orderItem.Quantity} stock for concession {concession.ID}");
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                Console.WriteLine($"✅ Successfully cancelled ticket {ticket.ID}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"❌ Failed to cancel ticket {ticket.ID}: {ex.Message}");
                throw;
            }
        }

        // ✅ Enhanced email confirmation with better error handling
        private async Task SendConfirmationEmail(Ticket ticket)
        {
            try
            {
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                var fromEmail = smtpSettings["FromEmail"];
                var smtpHost = smtpSettings["Host"];
                var smtpPort = int.Parse(smtpSettings["Port"]);
                var smtpUser = smtpSettings["Username"];
                var smtpPass = smtpSettings["Password"];

                // Skip email if settings are not configured
                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(smtpHost))
                {
                    Console.WriteLine("⚠️ Email settings not configured, skipping email");
                    return;
                }

                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    Credentials = new NetworkCredential(smtpUser, smtpPass),
                    EnableSsl = true
                };

                var seatNames = ticket.TicketSeats.Select(ts => $"{ts.Seat.RowLabel}{ts.Seat.SeatNumber}").ToList();

                var emailBody = new StringBuilder();
                emailBody.AppendLine($"Xin chào {ticket.User.FullName ?? ticket.User.Username},");
                emailBody.AppendLine();
                emailBody.AppendLine("Cảm ơn bạn đã đặt vé tại DK Movies! Dưới đây là thông tin vé của bạn:");
                emailBody.AppendLine();
                emailBody.AppendLine("=== THÔNG TIN VÉ ===");
                emailBody.AppendLine($"Mã vé: #{ticket.ID}");
                emailBody.AppendLine($"Phim: {ticket.ShowTime.Movie.Title}");
                emailBody.AppendLine($"Rạp: {ticket.ShowTime.Auditorium.Theater.Name}");
                emailBody.AppendLine($"Phòng chiếu: {ticket.ShowTime.Auditorium.Name}");
                emailBody.AppendLine($"Suất chiếu: {ticket.ShowTime.StartTime:dd/MM/yyyy HH:mm}");
                emailBody.AppendLine($"Ghế: {string.Join(", ", seatNames)}");
                emailBody.AppendLine($"Trạng thái: {(ticket.Status == TicketStatus.PAID ? "Đã thanh toán" : "Đã xác nhận")}");

                if (ticket.OrderItems?.Any() == true)
                {
                    emailBody.AppendLine();
                    emailBody.AppendLine("=== ĐỒ ĂN & THỨC UỐNG ===");
                    foreach (var item in ticket.OrderItems)
                    {
                        emailBody.AppendLine($"- {item.TheaterConcession.Concession.Name} x{item.Quantity} = {item.Quantity * item.PriceAtPurchase:N0} VND");
                    }
                }

                emailBody.AppendLine();
                emailBody.AppendLine($"Tổng tiền: {ticket.TotalPrice:N0} VND");
                emailBody.AppendLine($"Thời gian đặt: {ticket.PurchaseTime:dd/MM/yyyy HH:mm}");

                if (ticket.PaymentTime.HasValue)
                {
                    emailBody.AppendLine($"Thời gian thanh toán: {ticket.PaymentTime.Value:dd/MM/yyyy HH:mm}");
                }

                emailBody.AppendLine();
                emailBody.AppendLine("Vui lòng đến rạp trước giờ chiếu ít nhất 15 phút để làm thủ tục vào phòng.");
                emailBody.AppendLine();
                emailBody.AppendLine("Trân trọng,");
                emailBody.AppendLine("DK Movies Team");

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail, "DK Movies"),
                    Subject = $"Xác nhận đặt vé #{ticket.ID} - {ticket.ShowTime.Movie.Title}",
                    Body = emailBody.ToString(),
                    IsBodyHtml = false
                };

                mailMessage.To.Add(ticket.User.Email);
                await client.SendMailAsync(mailMessage);

                Console.WriteLine($"✅ Confirmation email sent to {ticket.User.Email}");
            }
            catch (Exception ex)
            {
                // Log email error but don't throw (payment should still succeed)
                Console.WriteLine($"⚠️ Email sending failed: {ex.Message}");
            }
        }
    }
}