using Microsoft.AspNetCore.Identity;
using PAWS.domain.Entities;
using System;

namespace PAWS.infrastructure.Services;

public class PasswordService : IPasswordService
{
    // ASP.NET's built-in hasher. It needs a "user" object in its method signatures,
    // but the default hasher never reads it, so one shared empty User is enough.
    private readonly PasswordHasher<User> _hasher = new();
    private readonly User _hasherUser = new();

    // A real hash of a random value nobody knows. Only used by PretendToVerify.
    private readonly string _dummyHash;

    public PasswordService()
    {
        _dummyHash = _hasher.HashPassword(_hasherUser, Guid.NewGuid().ToString("N"));
    }

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("A password is required.", nameof(password));
        }

        return _hasher.HashPassword(_hasherUser, password);
    }

    public bool Verify(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
        {
            return false;
        }

        try
        {
            var result = _hasher.VerifyHashedPassword(_hasherUser, hashedPassword, providedPassword);

            // Success = correct. SuccessRehashNeeded = correct, but the hash was made with
            // older settings. We accept both. (Upgrading old hashes is not needed for this project.)
            return result != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            // The stored text was not a valid hash at all. Treat it as "no match".
            return false;
        }
    }

    public void PretendToVerify(string providedPassword)
    {
        _hasher.VerifyHashedPassword(_hasherUser, _dummyHash, providedPassword ?? string.Empty);
    }
}