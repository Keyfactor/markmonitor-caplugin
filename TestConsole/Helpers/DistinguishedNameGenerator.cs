using System.Text.Json;

namespace TestConsole.Helpers;

public abstract class DistinguishedNameGenerator
{
    private static readonly HttpClient HttpClient = new();

    // Predefined list of country codes
    private static readonly string[] CountryCodes = { "US", "CA", "GB", "FR", "DE", "AU", "JP", "CN", "IN", "BR" };

    // Random number generator
    private static readonly Random random = new();

    // Base URL for random word API (you can replace this with a different API if needed)
    private const string RandomWordApiUrl = "https://random-word-api.herokuapp.com/word?number=1";

    // Predefined list of English words for fallback
    private static readonly string[] FallbackWords =
    {
        "apple", "banana", "cherry", "date", "elderberry", "fig", "grape", "honeydew", "kiwi", "lemon",
        "mango", "nectarine", "orange", "papaya", "quince", "raspberry", "strawberry", "tangerine", "ugli", "vanilla",
        "watermelon", "xigua", "yam", "zucchini", "apricot", "blackberry", "blueberry", "cantaloupe", "dragonfruit",
        "grapefruit", "guava", "jackfruit", "kumquat", "lime", "lychee", "mandarin", "mulberry", "olive", "peach",
        "pear", "persimmon", "pineapple", "plum", "pomegranate", "pumpkin", "rhubarb", "starfruit", "tomato", "avocado",
        "coconut", "cranberry", "currant", "gooseberry", "lemon", "lime", "melon", "nectarine", "papaya", "peach",
        "pear", "plum", "prune", "raisin", "tangerine", "watermelon", "apricot", "blackberry", "blueberry",
        "cantaloupe",
        "cherry", "clementine", "date", "dragonfruit", "elderberry", "fig", "grape", "grapefruit", "guava", "honeydew",
        "jackfruit", "kiwi", "kumquat", "lemon", "lime", "lychee", "mandarin", "mango", "mulberry", "nectarine", "olive"
    };

    public static async Task<string> GenerateRandomDNAsync()
    {
        // Generate random components
        var randomFqdn = await GenerateRandomFqdnAsync();
        var organization = await GetRandomWordAsync();
        var organizationalUnit = await GetRandomWordAsync();
        var locality = await GetRandomWordAsync();
        var stateOrProvince = await GetRandomWordAsync();
        var country = GetRandomCountryCode();

        // Construct the DN string
        var distinguishedName =
            $"CN={randomFqdn}, O={organization}, OU={organizationalUnit}, L={locality}, ST={stateOrProvince}, C={country}";

        return distinguishedName;
    }

    private static async Task<string> GenerateRandomFqdnAsync()
    {
        var word1 = await GetRandomWordAsync();
        var word2 = await GetRandomWordAsync();
        var tld = GetRandomTld();

        return $"{word1}.{word2}.{tld}".ToLower();
    }

    private static async Task<string> GetRandomWordAsync()
    {
        try
        {
            // Get a random word from the API
            var response = await HttpClient.GetAsync(RandomWordApiUrl);
            response.EnsureSuccessStatusCode();

            // Read response content as a string
            var jsonResponse = await response.Content.ReadAsStringAsync();

            // Deserialize the JSON response to a string array
            var words = JsonSerializer.Deserialize<string[]>(jsonResponse);

            return words != null ? words[0] : GetFallbackWord();
        }
        catch
        {
            // Fallback to predefined list if API call fails
            return GetFallbackWord();
        }
    }

    private static string GetFallbackWord()
    {
        // Choose a random word from the predefined list
        var index = random.Next(FallbackWords.Length);
        return FallbackWords[index];
    }

    private static string GetRandomCountryCode()
    {
        // Choose a random country code from the list
        var index = random.Next(CountryCodes.Length);
        return CountryCodes[index];
    }

    private static string GetRandomTld()
    {
        // Simple list of common top-level domains (TLDs)
        var topLevelDomains = new[] { "com", "net", "org", "io", "dev", "co", "ai" };
        var index = random.Next(topLevelDomains.Length);
        return topLevelDomains[index];
    }
}