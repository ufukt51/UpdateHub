namespace UpdateHub.Models;

/// <summary>
/// Bir parser'ın ürettiği ham kayıt. <see cref="Available"/> null ise paket güncel demektir.
/// </summary>
public sealed record ParsedPackage(
    string Id,
    string Name,
    string Current,
    string? Available,
    bool IsBroken = false)
{
    public bool HasUpdate => IsBroken || Available is not null;
}
