namespace Cameramg.Security;
public class JwtOptions
{
    public string Issuer { get; set; } = "Cameramg";
    public string Audience { get; set; } = "Camera";
    public string Secret { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 480;
    public int ExpirationHours { get; set; } = 8;
}
