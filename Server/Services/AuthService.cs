using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChatServer.Database;
using ChatServer.Models;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

namespace ChatServer.Services
{
    public class AuthService
    {
        private readonly MongoDBContext _context;
        private readonly string _secretKey;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _expiryMinutes;

        public AuthService(MongoDBContext context, string secretKey, string issuer, string audience, int expiryMinutes)
        {
            _context = context;
            _secretKey = secretKey;
            _issuer = issuer;
            _audience = audience;
            _expiryMinutes = expiryMinutes;
        }

        public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
        {
            try
            {
                // Kiểm tra email đã tồn tại
                var existingUser = await _context.Users.Find(u => u.Email == request.Email).FirstOrDefaultAsync();
                if (existingUser != null)
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = "Email đã được sử dụng."
                    };
                }

                // Hash mật khẩu
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                // Tạo user mới
                var user = new User
                {
                    Email = request.Email,
                    PasswordHash = passwordHash,
                    DisplayName = request.DisplayName,
                    CreatedAt = DateTime.UtcNow,
                    IsOnline = false
                };

                await _context.Users.InsertOneAsync(user);

                // Tạo JWT token
                var token = GenerateJwtToken(user);

                return new AuthResponse
                {
                    Success = true,
                    Message = "Đăng ký thành công.",
                    Token = token,
                    User = MapToUserDto(user)
                };
            }
            catch (Exception ex)
            {
                return new AuthResponse
                {
                    Success = false,
                    Message = $"Lỗi: {ex.Message}"
                };
            }
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request)
        {
            try
            {
                // Tìm user theo email
                var user = await _context.Users.Find(u => u.Email == request.Email).FirstOrDefaultAsync();
                if (user == null)
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = "Email hoặc mật khẩu không đúng."
                    };
                }

                // Verify password
                if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = "Email hoặc mật khẩu không đúng."
                    };
                }

                // Cập nhật trạng thái online
                var update = Builders<User>.Update
                    .Set(u => u.IsOnline, true)
                    .Set(u => u.LastSeenAt, DateTime.UtcNow);
                await _context.Users.UpdateOneAsync(u => u.Id == user.Id, update);

                // Tạo JWT token
                var token = GenerateJwtToken(user);

                return new AuthResponse
                {
                    Success = true,
                    Message = "Đăng nhập thành công.",
                    Token = token,
                    User = MapToUserDto(user)
                };
            }
            catch (Exception ex)
            {
                return new AuthResponse
                {
                    Success = false,
                    Message = $"Lỗi: {ex.Message}"
                };
            }
        }

        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_secretKey);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Name, user.DisplayName)
                }),
                Expires = DateTime.UtcNow.AddMinutes(_expiryMinutes),
                Issuer = _issuer,
                Audience = _audience,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private UserDto MapToUserDto(User user)
        {
            return new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl,
                IsOnline = user.IsOnline,
                LastSeenAt = user.LastSeenAt
            };
        }
    }
}
