using Homecare.Models;

namespace Homecare.DAL.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetAsync(int id);
        Task<List<User>> GetByRoleAsync(UserRole role);
    }
}
