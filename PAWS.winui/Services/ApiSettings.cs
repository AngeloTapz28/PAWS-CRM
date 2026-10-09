namespace PAWS.winui.Services;

// The ONE place that stores the address of the PAWS API.
// Every page and service reads the address from here.
// This is only a public web address. Never put passwords or secrets in this file.
public static class ApiSettings
{
#if DEBUG
    // While you press F5 in Visual Studio: your local API (PAWS.api running on your PC).
    public const string BaseUrl = "https://localhost:7129/";
#else
    // Release build: the hosted API address. Placeholder until we deploy in a later phase.
    public const string BaseUrl = "https://<NEW_HOSTED_API_ADDRESS>/";
#endif
}
