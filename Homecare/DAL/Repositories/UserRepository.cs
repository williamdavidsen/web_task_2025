// DAL/Repositories/UserRepository.cs
using Homecare.DAL.Interfaces;
using Homecare.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Homecare.DAL.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _db;
        private readonly ILogger<UserRepository> _logger;

        public UserRepository(AppDbContext db, ILogger<UserRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<User?> GetAsync(int id)
        {
            try
            {
                // student note: DOMAIN users (not Identity)
                return await _db.DomainUsers
                                .AsNoTracking()
                                .FirstOrDefaultAsync(u => u.UserId == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UserRepository] GetAsync({Id}) failed", id);
                return null;
            }
        }

        public async Task<List<User>> GetByRoleAsync(UserRole role)
        {
            try
            {
                return await _db.DomainUsers
                                .Where(u => u.Role == role)
                                .OrderBy(u => u.Name)
                                .AsNoTracking()
                                .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UserRepository] GetByRoleAsync({Role}) failed", role);
                return new List<User>();
            }
        }
    }
}
