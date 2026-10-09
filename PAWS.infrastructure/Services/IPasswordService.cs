namespace PAWS.infrastructure.Services;

// The ONE place that hashes and checks passwords.
public interface IPasswordService
{
    // Turns a password into a hash that is safe to store in the database.
    string Hash(string password);

    // True if the typed password matches the stored hash.
    bool Verify(string hashedPassword, string providedPassword);

    // Does the same amount of work as Verify, and throws the result away.
    // Used by login when the email is not found, so a missing account is not
    // noticeably faster than a wrong password.
    void PretendToVerify(string providedPassword);
}