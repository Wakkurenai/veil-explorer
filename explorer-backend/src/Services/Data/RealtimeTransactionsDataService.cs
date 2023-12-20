using ExplorerBackend.Models.Data;
using ExplorerBackend.Models.Node.Response;
using ExplorerBackend.Services.Caching;
using ExplorerBackend.Services.Core;

namespace ExplorerBackend.Services.Data;

public class RealtimeTransactionsDataService(NodeRequester nodeRequester, IUtilityService utilityService, BlocksCacheSingleton cache)
    : ITransactionsDataService
{
    private readonly BlocksCacheSingleton _cache = cache;
    private readonly NodeRequester _nodeRequester = nodeRequester;
    private readonly IUtilityService _utilityService = utilityService;

    public async Task<Transaction?> GetTransactionByIdAsync(string txid, CancellationToken cancellationToken = default)
    {
        GetRawTransaction? tx = await _nodeRequester.GetRawTransaction(txid, cancellationToken);
        return new()
        {
            block_height = 0,
            txid_hex = tx!.Result!.txid,
            hash_hex = tx.Result.hash,
            version = tx.Result.version,
            size = tx.Result.size,
            vsize = tx.Result.vsize,
            weight = tx.Result.weight,
            locktime = tx.Result.locktime,
            blockhash = tx.Result.blockhash
        };        
    }

    public async Task<TransactionExtended?> GetTransactionFullByIdAsync(string txid, CancellationToken cancellationToken = default)
    {
        GetRawTransaction? tx = await _nodeRequester.GetRawTransaction(txid, cancellationToken);
        return new()
        {
            block_height = 0,
            txid_hex = tx!.Result!.txid,
            hash_hex = tx.Result.hash,
            version = tx.Result.version,
            size = tx.Result.size,
            vsize = tx.Result.vsize,
            weight = tx.Result.weight,
            locktime = tx.Result.locktime,
            blockhash = tx.Result.blockhash,
            data = _utilityService.HexToByteArray(tx.Result.hex!),
        };
    }

    public async Task<List<TransactionExtended>?> GetTransactionsForBlockAsync(int blockHeight, int offset, int count, bool fetchAll, CancellationToken ct = default)
    {
        GetBlockResult? rawBlock = await _cache.GetCachedBlockByHeightAsync<GetBlockResult>(blockHeight.ToString(), ct);;
        List<TransactionExtended>? list = [];
        foreach (var tx in rawBlock!.Txs!)
        {
            list.Add(new()
            {
                block_height = blockHeight,
                txid_hex = tx!.txid,
                hash_hex = tx.hash,
                version = tx.version,
                size = tx.size,
                vsize = tx.vsize,
                weight = tx.weight,
                locktime = tx.locktime,
                blockhash = tx.blockhash,
                data = _utilityService.HexToByteArray(tx.hex!), 
            });
        }
        return list;
    }

    public async Task<string?> ProbeTransactionByHashAsync(string txid, CancellationToken cancellationToken = default)
    {
        GetRawTransaction? tx = await _nodeRequester.GetRawTransaction(txid, cancellationToken);

        return tx!.Result!.hex!;
    }
    
    public List<TransactionExtended>? ToTransactionExtended(List<GetRawTransactionResult> list, int height)
    {
       if(list is null) return null;

        List<TransactionExtended>? listExtended = [];
        foreach (var tx in list)
        {
            listExtended.Add(new()
            {
                block_height = height,
                txid_hex = tx.txid,
                hash_hex = tx.hash,
                version = tx.version,
                size = tx.size,
                vsize = tx.vsize,
                weight = tx.weight,
                locktime = tx.locktime,
                blockhash = tx.blockhash,
                data = _utilityService.HexToByteArray(tx.hex!), 
            });
        }
        return listExtended;
    }
}