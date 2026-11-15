using System.Text;
using IntelligenceDB.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace IntelligenceDB.Infrastructure.Security;

/// <summary>Encrypts secrets with ASP.NET Core Data Protection (AES-256-CBC + HMACSHA256 by default).</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    // Changing this string makes every stored secret unreadable. Bump the version only with a re-encryption plan.
    private const string Purpose = "IntelligenceDB.TargetSecrets.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public byte[] Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        return _protector.Protect(Encoding.UTF8.GetBytes(plainText));
    }

    public string Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        return Encoding.UTF8.GetString(_protector.Unprotect(protectedData));
    }
}
