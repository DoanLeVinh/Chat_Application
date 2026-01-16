using ChatServer.Database;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace ChatServer.Controllers
{
    [ApiController]
    [Route("api/stickers")]
    public class StickerController(MongoDBContext db) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var stickers = await db.Stickers.Find(_ => true).ToListAsync();
            return Ok(stickers);
        }
    }
}
