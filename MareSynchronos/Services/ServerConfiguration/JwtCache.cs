namespace MareSynchronos.Services.ServerConfiguration;

public record JwtCache(string ApiUrl, string PlayerName, uint WorldId, string SecretKey);
