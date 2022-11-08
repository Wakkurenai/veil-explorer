using Microsoft.AspNetCore.Mvc;
using ExplorerBackend.Models.API;
using ExplorerBackend.Services.Core;
using ExplorerBackend.Services.Caching;
using ExplorerBackend.Persistence.Repositories;

namespace ExplorerBackend.Controllers;

[ApiController]
[Route("/api/[controller]")]
[Produces("application/json")]
public class SearchController : ControllerBase
{
    private readonly IBlocksRepository _blocksRepository;
    private readonly ITransactionsRepository _transactionsRepository;
    private readonly ChaininfoSingleton _chaininfoSingleton;
    private readonly IUtilityService _utilityService;

    public SearchController(IBlocksRepository blocksRepository, ITransactionsRepository transactionsRepository, ChaininfoSingleton chaininfoSingleton, IUtilityService utilityService)
    {
        _blocksRepository = blocksRepository;
        _transactionsRepository = transactionsRepository;
        _chaininfoSingleton = chaininfoSingleton;
        _utilityService = utilityService;
    }

    [HttpPost(Name = "Search")]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(SearchRequest body, CancellationToken cancellationToken)
    {
        var response = new SearchResponse
        {
            Found = false,
            Type = EntityType.UNKNOWN,
            Query = body.Query
        };

        if (body.Query != null)
        {
            if (_utilityService.IsNumeric(body.Query))
            {
                response.Found = true;
                response.Type = EntityType.BLOCK_HEIGHT;
            }
            else
            {
                if (_utilityService.VerifyAddress(body.Query))
                {
                    response.Found = true;
                    response.Type = EntityType.ADDRESS;
                }
                else if (body.Query.Length == 64 && _utilityService.VerifyHex(body.Query))
                {
                    var probeTx = _chaininfoSingleton.UnconfirmedTxs?.Where(tx => tx.txid == body.Query).FirstOrDefault();
                    if (probeTx != null)
                    {
                        response.Found = true;
                        response.Type = EntityType.TRANSACTION_HASH;
                        return Ok(response);
                    }

                    var tx = await _transactionsRepository.ProbeTransactionByHashAsync(body.Query, cancellationToken);
                    if (tx != null)
                    {
                        response.Found = true;
                        response.Type = EntityType.TRANSACTION_HASH;
                    }
                    else
                    {
                        var block = await _blocksRepository.ProbeBlockByHashAsync(body.Query, cancellationToken);
                        if (block != null)
                        {
                            response.Found = true;
                            response.Type = EntityType.BLOCK_HASH;
                        }
                    }
                }
            }
        }

        return Ok(response);
    }
}
