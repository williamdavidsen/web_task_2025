
using Homecare.DAL.Interfaces;
using Homecare.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Homecare.DAL.Repositories
{
    public class CareTaskRepository : ICareTaskRepository
    {
        private readonly AppDbContext _db;
        private readonly ILogger<CareTaskRepository> _logger;

        public CareTaskRepository(AppDbContext db, ILogger<CareTaskRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<CareTask>> GetAllAsync()
        {
            try
            {
                return await _db.CareTasks
                    .OrderBy(t => t.Description)
                    .AsNoTracking()
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CareTaskRepository] GetAllAsync failed");
                return new List<CareTask>();
            }
        }
        public async Task<CareTask?> GetAsync(int id)
        {
            try
            {
                return await _db.CareTasks.FirstOrDefaultAsync(t => t.CareTaskId == id);
            }

            catch (Exception e)
            {

                _logger.LogError(e, "[CareTaskRepository] GetAsync({Id}) failed", id);
                return null;
            }
        }
    }


}
