// DKMovies/Middleware/AdminAccessMiddleware.cs
using System.Security.Claims;

namespace DKMovies.Middleware
{
    public class AdminAccessMiddleware
    {
        private readonly RequestDelegate _next;

        public AdminAccessMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLower();

            // Kiểm tra nếu là route admin
            if (path != null && path.StartsWith("/admin"))
            {
                // Bỏ qua static files (css, js, images)
                if (path.Contains(".css") || path.Contains(".js") || path.Contains(".png") ||
                    path.Contains(".jpg") || path.Contains(".jpeg") || path.Contains(".gif") ||
                    path.Contains(".ico") || path.Contains(".svg"))
                {
                    await _next(context);
                    return;
                }

                // Kiểm tra đăng nhập
                if (!context.User.Identity.IsAuthenticated)
                {
                    context.Response.Redirect("/Account/Login");
                    return;
                }

                // Kiểm tra có phải admin không
                var userType = context.User.FindFirst("UserType")?.Value;
                if (userType != "Admin")
                {
                    // User thường cố truy cập admin → Access Denied
                    context.Response.Redirect("/Account/AccessDenied");
                    return;
                }

                // Ghi log (optional)
                var username = context.User.Identity.Name;
                var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
                Console.WriteLine($"Admin access: {username} ({role}) → {path}");
            }

            // Tiếp tục pipeline
            await _next(context);
        }
    }
}