using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChatServer.Database;
using ChatServer.Models;
using Microsoft.IdentityModel.Tokens;

namespace ChatServer.Services
{
    public class AuthService
    {
        private readonly UserRepository _userRepository;
        private readonly JwtSettings _jwtSettings;

        public AuthService(UserRepository userRepository, JwtSettings jwtSettings)
        {
            _userRepository = userRepository;
            _jwtSettings = jwtSettings;
        }

        public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
        {
            try
            {
                // Kiểm tra email đã tồn tại
                var existingUser = await _userRepository.GetByEmailAsync(request.Email);
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
                    CreatedAt = DateTime.UtcNow
                };

                await _userRepository.CreateAsync(user);

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
                var user = await _userRepository.GetByEmailAsync(request.Email);
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
                await _userRepository.UpdateOnlineStatusAsync(user.Id, true);

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
            var key = Encoding.ASCII.GetBytes(_jwtSettings.SecretKey);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Name, user.DisplayName)
                }),
                Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryInMinutes),
                Issuer = _jwtSettings.Issuer,
                Audience = _jwtSettings.Audience,
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
                IsOnline = user.IsOnline
            };
        }
    }
}
