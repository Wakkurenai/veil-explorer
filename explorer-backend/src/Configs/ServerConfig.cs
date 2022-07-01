namespace ExplorerBackend.Configs;

public class ServerConfig
{
    public string? InternalAccessKey { get; set; }
    public List<string>? CorsOrigins { get; set; }
    public Swagger? Swagger { get; set; }
}

public class Swagger
{
    public bool Enabled { get; set; }
    public string? RoutePrefix { get; set; }
}