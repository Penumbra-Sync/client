namespace MareSynchronos.WebAPI;

public record JwtCache(string ApiUrl, string PlayerName, uint WorldId, string SecretKey);
