using ExplorerBackend.Services.Core;
using ExplorerBackend.Services.Caching;

namespace ExplorerBackend.Models.System;

public class ScanTxOutsetBridge
{
    public NodeApiCacheSingleton? NodeApiCacheLink { get; set; }
    public NodeRequester? NodeRequesterLink { get; set; }
}