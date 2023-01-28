namespace MareSynchronos.Models;

public record JwtCache(string ApiUrl, string PlayerName, uint WorldId, string SecretKey);
