using Microsoft.AspNetCore.Mvc;
using System.Net;
using Microsoft.Extensions.Options;
using explorer_backend.Models.API;
using explorer_backend.Configs;
using explorer_backend.Models.Data;
using explorer_backend.Persistence.Repositories;

namespace explorer_backend.Controllers;

[ApiController]
[Route("/api/[controller]")]
[Produces("application/json")]
public class BlocksController : ControllerBase
{

    private readonly ILogger _logger;
    private readonly IOptions<APIConfig> _apiConfig;
    private readonly IBlocksRepository _blocksRepository;

    public BlocksController(ILogger<BlocksController> logger, IOptions<APIConfig> apiConfig, IBlocksRepository blocksRepository)
    {
        _logger = logger;
        _apiConfig = apiConfig;
        _blocksRepository = blocksRepository;
    }

    [HttpGet(Name = "GetBlocks")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(List<SimplifiedBlock>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int offset, int count, SortDirection sort)
    {
        if (offset < 0)
            return Problem("offset should be higher or equal to zero", statusCode: 400);
        if (count < 1)
            return Problem("count should be more or equal to one", statusCode: 400);
        if (count > _apiConfig.Value.MaxBlocksPullCount)
            return Problem($"count should be less or equal than {_apiConfig.Value.MaxBlocksPullCount}", statusCode: 400);

        return Ok(await _blocksRepository.GetSimplifiedBlocks(offset, count, sort));
    }
}
