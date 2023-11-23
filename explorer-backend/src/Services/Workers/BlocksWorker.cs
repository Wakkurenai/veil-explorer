using System.Text;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;
using ExplorerBackend.Hubs;
using ExplorerBackend.Configs;
using ExplorerBackend.Services.Caching;
using ExplorerBackend.Persistence.Repositories;
using ExplorerBackend.Models.API;
using ExplorerBackend.Services.Core;
using ExplorerBackend.Models.Node.Response;
using ExplorerBackend.Models.Data;

namespace ExplorerBackend.Services.Workers;

public class BlocksWorker : BackgroundService
{
    private int _latestBlock;
    private bool _firstRun;
    private readonly int _blocksPerBatch;
    private readonly int _blocksOrphanCheck;
    private readonly bool _RPCMode;
    private readonly ILogger _logger;
    private readonly IHubContext<EventsHub> _hubContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<ExplorerConfig> _explorerConfig;
    private readonly ChaininfoSingleton _chainInfoSingleton;
    private readonly BlocksCacheSingleton _blocksCacheSingleton;
    private readonly SimplifiedBlocksCacheSingleton _simplifiedBlocksCacheSingleton;
    private readonly NodeRequester _nodeRequester;
    private readonly IBlocksService _blocksService;

    public BlocksWorker(ILogger<BlocksWorker> logger, IHubContext<EventsHub> hubContext, IServiceProvider serviceProvider,
        IOptionsMonitor<ExplorerConfig> explorerConfig, ChaininfoSingleton chaininfoSingleton,
        BlocksCacheSingleton blocksCacheSingleton, SimplifiedBlocksCacheSingleton simplifiedBlocksCacheSingleton, 
        NodeRequester nodeRequester, IBlocksService blocksService)
    {
        _logger = logger;
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
        _explorerConfig = explorerConfig;
        _chainInfoSingleton = chaininfoSingleton;
        _blocksCacheSingleton = blocksCacheSingleton;
        _simplifiedBlocksCacheSingleton = simplifiedBlocksCacheSingleton;
        _nodeRequester = nodeRequester;
        _blocksService = blocksService;
        _blocksPerBatch = _explorerConfig.CurrentValue.BlocksPerBatch == 0 ? 10 : _explorerConfig.CurrentValue.BlocksPerBatch;
        _blocksOrphanCheck = _explorerConfig.CurrentValue.BlocksOrphanCheck == 0 ? 12 : _explorerConfig.CurrentValue.BlocksOrphanCheck;
        _RPCMode = _explorerConfig.CurrentValue.RPCMode;
        _firstRun = true;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (_RPCMode)
        {
            try
            {
                var latestBlock = await _nodeRequester.GetLatestBlock(cancellationToken);
               
                _latestBlock = latestBlock!.Height;
            }
            catch (ArgumentNullException) { return; }
            catch (Exception)
            {
                _logger.LogError("Can't get blocks");
                return;
            }
        }
        else
        {
            // set initial state, this fixes issue when after restart blocks not retrieved correctly from API before new block come from node
            await using var scope = _serviceProvider.CreateAsyncScope();
            var blocksRepository = scope.ServiceProvider.GetRequiredService<IBlocksRepository>();
            var latestSyncedBlock = await blocksRepository.GetLatestBlockAsync(true, cancellationToken);

            if (latestSyncedBlock != null)
                _chainInfoSingleton.CurrentSyncedBlock = latestSyncedBlock.height;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_explorerConfig.CurrentValue.RPCMode)
                {
                    await RPCModeIteration(cancellationToken);
                }
                else
                {
                    await DBModeIteration(cancellationToken);
                }

                // TimeSpan not reuired here since we use milliseconds, still put it there to change in future if required
                await Task.Delay(TimeSpan.FromMilliseconds(_explorerConfig.CurrentValue.PullBlocksDelay), cancellationToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Can't handle blocks");
            }
        }
    }

    private async Task DBModeIteration(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateAsyncScope();

        var blocksRepository = scope.ServiceProvider.GetRequiredService<IBlocksRepository>();
        var transactionsRepository = scope.ServiceProvider.GetRequiredService<ITransactionsRepository>();
        var rawTxsRepository = scope.ServiceProvider.GetRequiredService<IRawTxsRepository>();

        var latestSyncedBlock = await blocksRepository.GetLatestBlockAsync(true, cancellationToken);
        int currentIndexedBlock = (latestSyncedBlock != null ? latestSyncedBlock.height : 0) + 1;

        for (int i = currentIndexedBlock; i < currentIndexedBlock + _blocksPerBatch; i++)
        {
            try
            {
                var blockHash = await _nodeRequester.GetBlockHash((uint)i, cancellationToken);

                if (blockHash == null || blockHash.Result == null)
                    break;

                var block = await _nodeRequester.GetBlock(blockHash.Result, cancellationToken, simplifiedTxInfo: 2); // 1 -> block, 2 -> block + tx

                if (block == null)
                {
                    _logger.LogInformation("Can't pull block");
                    break;
                }

                // save data to db
                // check if block already exists in DB
                var targetBlock = await blocksRepository.GetBlockAsync(i, cancellationToken);
                // transform block rpc to block data
                if (targetBlock == null)
                {
                    targetBlock = _blocksService.RPCBlockToDb(block);
                    // save block
                    if (!await blocksRepository.InsertBlockAsync(targetBlock, cancellationToken))
                    {
                        _logger.LogError(null, "Can't save block #{blockNumber}", i);
                        break;
                    }
                }

                // save block's transactions
                var txFailed = await _blocksService.InsertTransactionsAsync(i, block.Txs!, cancellationToken);
                if (txFailed) break;
  
                if (!await blocksRepository.SetBlockSyncStateAsync(i, true, cancellationToken))
                {
                    _logger.LogError(null, "Can't update block #{blockNumber}", i);
                    break;
                }

                try
                {
                    await OnNewBlockHubUpdate(block, block.NTx, cancellationToken);
                }
                catch { }
                // check orphans
                // make sense to set BlocksOrphanCheck to zero on initial sync
                for (int j = targetBlock.height - _blocksOrphanCheck; j < targetBlock.height; j++)
                {
                    var blockHashCheck = await _nodeRequester.GetBlockHash((uint)j, cancellationToken);

                    if (blockHashCheck == null || blockHashCheck.Result == null)
                        continue;

                    // better to get blocks from db in single query, however this is not "hot path" so...
                    var blockFromDB = await blocksRepository.GetBlockAsync(j, cancellationToken);
                    if (blockFromDB?.hash_hex == blockHashCheck.Result)
                        continue;

                    if (!await _blocksService.UpdateDbBlockAsync(j, blockHashCheck.Result, cancellationToken))
                        _logger.LogError("Can't update orphan block #{blockNumber}", j);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Can't process block #{blockNumber}", i);
                break;
            }
        }
    }

    private async Task RPCModeIteration(CancellationToken cancellationToken)
    {
        try
        {   
            var block = await _nodeRequester.GetBlockHash((uint)_latestBlock, cancellationToken);
            if (block is null || block.Result is null)
                return;
            
            var newBlock = await _nodeRequester.GetBlock(block.Result, cancellationToken);
            if(newBlock is null)
                return;

            _latestBlock = newBlock.Height;
            
            var setBlockCache = _blocksCacheSingleton.SetServerCacheDataAsync(newBlock.Height, newBlock.Hash!, newBlock, cancellationToken);
            var setSimplifiedBlockCacke = _simplifiedBlocksCacheSingleton.SetBlockCache(newBlock, true);
            var updateHub = OnNewBlockHubUpdate(newBlock, newBlock.NTx, cancellationToken);

            try
            {
                await Task.WhenAll(setBlockCache, updateHub, setSimplifiedBlockCacke);
            }
            catch { }
            // checking prev blocks
            for (int i = _latestBlock - _blocksOrphanCheck; i < _latestBlock; i++)
            {
                var prevBlocksHash = await _nodeRequester.GetBlockHash((uint)i, cancellationToken);

                if (prevBlocksHash is null || prevBlocksHash.Result is null)
                    continue;
                
                bool isPrevBlockHashValid = await _blocksCacheSingleton.ValidateCacheAsync(i.ToString(), prevBlocksHash.Result);

                if(isPrevBlockHashValid is false)
                {
                    var prevBlock = await _nodeRequester.GetBlock(prevBlocksHash.Result, cancellationToken, 2);

                    var updateCache = _blocksCacheSingleton.UpdateCachedDataAsync(i.ToString(), prevBlocksHash.Result, prevBlock!, cancellationToken);
                    var updateSimplifiedCache = _simplifiedBlocksCacheSingleton.SetBlockCache(prevBlock!);

                    await Task.WhenAll(updateCache, updateSimplifiedCache);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Can't pull new block");
        }
    }

    private async Task OnNewBlockHubUpdate(GetBlockResult block, int txCount, CancellationToken cancellationToken)
    {
        _chainInfoSingleton.CurrentSyncedBlock = block.Height;
        await _hubContext.Clients.Group(EventsHub.BlocksDataChannel).SendAsync("blocksUpdated", new SimplifiedBlock
        {
            Height = block.Height,
            Size = block.Size,
            Weight = block.Weight,
            ProofType = block.Proof_type switch
            {
                "Proof-of-Work (X16RT)" => BlockType.POW_X16RT,
                "Proof-of-work (ProgPow)" => BlockType.POW_ProgPow,
                "Proof-of-work (RandomX)" => BlockType.POW_RandomX,
                "Proof-of-work (Sha256D)" => BlockType.POW_Sha256D,
                "Proof-of-Stake" => BlockType.POS,
                _ => BlockType.UNKNOWN
            },
            Time = block.Time,
            MedianTime = block.Mediantime,
            TxCount = txCount
        }, cancellationToken);

        await _chainInfoSingleton.BlockchainDataSemaphore.WaitAsync(cancellationToken);
        _chainInfoSingleton.BlockchainDataShouldBroadcast = true;
        _chainInfoSingleton.BlockchainDataSemaphore.Release();
    }
}