namespace TestConsole.Helpers;

public class EmailAddressGenerator : DistinguishedNameGenerator
{
    public static async Task<List<string>> GenerateRandomEmailsAsync(int numEmails)
    {
        var emailAddresses = new List<string>();
        for (var i = 0; i < numEmails; i++)
        {
            var randomWord = await GetRandomWordAsync();
            // var domain = await GenerateRandomFqdnAsync();
            const string domain = "markmonitor.com";
            emailAddresses.Add($"{randomWord}@{domain}".ToLower());
        }

        return emailAddresses;
    }
}